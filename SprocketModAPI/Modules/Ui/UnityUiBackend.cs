using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Sprocket;
using Sprocket.Selection;
using Sprocket.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SprocketModAPI
{
    internal sealed class UiService : IUiService, IDisposable
    {
        private readonly Action<string> error;
        private readonly int mainThreadId;
        private readonly Queue<Action> pending = new();
        private readonly List<UiScopeCore> scopes = new();
        private readonly UnityUiBackend backend;
        private readonly IUiBackend dispatchedBackend;
        private readonly UiStatusBroadcaster statusBroadcaster;
        private bool disposed;

        internal UiService(Action<string> warn, Action<string> error)
        {
            this.error = error;
            mainThreadId = Environment.CurrentManagedThreadId;
            statusBroadcaster = new UiStatusBroadcaster(warn);
            backend = new UnityUiBackend(warn, error);
            backend.StatusChanged += OnBackendStatusChanged;
            dispatchedBackend = new DispatchingUiBackend(backend, action => Invoke(action), action => Invoke(action), action => Invoke(action), action => Invoke(action));
        }

        public UiCapabilitySnapshot Capabilities => backend.Capabilities;
        public event EventHandler<UiStatusChangedEventArgs> StatusChanged
        {
            add => statusBroadcaster.StatusChanged += value;
            remove => statusBroadcaster.StatusChanged -= value;
        }

        private void OnBackendStatusChanged(object? sender, UiStatusChangedEventArgs args)
        {
            if (!disposed) statusBroadcaster.Publish(this, args);
        }

        [MethodImpl(MethodImplOptions.NoInlining)] // GetCallingAssembly 需要真实栈帧：不能被内联进模组方法
        public IUiScope CreateScope(UiOwnerDefinition owner)
        {
            if (owner == null)
                throw new ArgumentException("UI owner definition is required.", nameof(owner));
            if (string.IsNullOrWhiteSpace(owner.ModId))
                owner.ModId = ModIdentity.ResolveModId(Assembly.GetCallingAssembly());
            if (string.IsNullOrWhiteSpace(owner.ModId))
                throw new ArgumentException("UI owner ModId could not be resolved.", nameof(owner));
            if (disposed)
                throw new ObjectDisposedException(nameof(UiService));
            var scope = new UiScopeCore(owner.ModId, dispatchedBackend);
            scopes.Add(scope);
            return scope;
        }

        internal void Update()
        {
            if (disposed)
                return;
            while (true)
            {
                Action? command;
                lock (pending)
                    command = pending.Count == 0 ? null : pending.Dequeue();
                if (command == null)
                    break;
                try { command.Invoke(); }
                catch (Exception exception) { error($"[SMA-UI] main-thread command failed: {exception}"); }
            }
            backend.Update();
        }

        internal void SceneUnloaded(string sceneName)
        {
            backend.SceneUnloaded(sceneName);
        }

        internal void SceneLoaded(string sceneName)
        {
            backend.SceneLoaded(sceneName);
        }

        internal T Invoke<T>(Func<T> action)
        {
            if (Environment.CurrentManagedThreadId == mainThreadId)
                return action();

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (pending)
            {
                pending.Enqueue(() =>
                {
                    try { completion.SetResult(action()); }
                    catch (Exception exception) { completion.SetException(exception); }
                });
            }
            return completion.Task.GetAwaiter().GetResult();
        }

        internal void Invoke(Action action)
        {
            Invoke(() =>
            {
                action();
                return true;
            });
        }

        public void Dispose()
        {
            if (disposed)
                return;
            // FIX: Application shutdown can destroy the native MainMenu before BepInEx
            // unloads the plugin. A final Update then dereferenced that
            // dead IL2CPP Behaviour; close the service before native cleanup.
            disposed = true;
            foreach (UiScopeCore scope in scopes)
                scope.Dispose();
            scopes.Clear();
            backend.Dispose();
            backend.StatusChanged -= OnBackendStatusChanged;
            statusBroadcaster.Dispose();
            lock (pending)
            {
                pending.Clear();
            }
        }
    }

    internal sealed class UnityUiBackend : IUiBackend
    {
        // 能力探测与创建都针对这个版本的原生主菜单做的：换版本要重新核对 MenuPanel 字段与方法。
        private static readonly Version SupportedGameVersion = new(0, 2, 55, 5);

        // 原生切屏时由 `MainMenu.SetActiveMenu` 的协程重画 `MenuPanel`（先清空再补回原生按钮）。
        // 重画期间建出来的按钮会被那次清空带走，所以观测到换屏后先等布局落定再补按钮。
        private const int MenuSettleFrames = 2;

        // 原生 `MenuPanel.Button` 无条件把入参挂到 `UnityEvent` 上，传 null 会在原生侧抛空引用。
        private static readonly Action NoOpClick = () => { };

        private readonly UiDebugLog debug;
        private readonly Action<string> warn;
        private readonly Action<string> error;
        private readonly List<IUiMenuButtonHandle> handles = new();
        private bool disposed;
        private MainMenu? mainMenu;
        private MenuPanel? menuPanel;
        private IntPtr observedScreenPointer;
        private int settleFrames;
        private int menuGeneration;
        private string currentSceneName = "";

        internal UnityUiBackend(Action<string> warn, Action<string> error)
        {
            this.warn = warn;
            this.error = error;
            debug = new UiDebugLog(ApiSelfSettings.Current, error);
            menuGeneration++;
            Capabilities = new UiCapabilitySnapshot { GameVersion = SupportedGameVersion };
        }

        public UiCapabilitySnapshot Capabilities { get; private set; }
        public event EventHandler<UiStatusChangedEventArgs>? StatusChanged;

        // 已销毁的原生对象仍可能留下非空代理：`?.` 只检查引用，不会走 Unity 的判空语义，
        // 直接访问 `isActiveAndEnabled` / `Visible` 会抛原生空引用。这里统一吞掉。
        private static bool IsActiveAndEnabled(Behaviour? behaviour)
        {
            if (behaviour == null)
                return false;

            try
            {
                return behaviour.isActiveAndEnabled;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsVisible(MenuPanel? panel)
        {
            if (panel == null)
                return false;

            try
            {
                return panel.Visible;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal void Update()
        {
            if (disposed)
                return;
            MenuPanel? panel = EnsurePanel(allowSearch: IsMainMenuSceneLoaded() || menuPanel != null);
            bool hasPool = panel != null && SafeButtonPool(panel) != null;
            UiCapability available = hasPool ? UiCapability.MenuButton : UiCapability.None;

            IntPtr screenPointer = SafePointer(mainMenu?.activeMenu);
            if (screenPointer != observedScreenPointer)
            {
                observedScreenPointer = screenPointer;
                menuGeneration++;
                settleFrames = MenuSettleFrames;
                debug.Lifecycle($"menu-transition generation={menuGeneration} pointer=0x{screenPointer.ToInt64():X}");
            }

            bool settled = settleFrames == 0;
            if (!settled)
                settleFrames--;

            // 原生按钮就位（`ActiveButtonCount > 0`）才算主菜单可供模组安放按钮：这时锚点与布局都已画好。
            bool ready = settled && IsPanelReady(panel);
            if (ready)
            {
                RegisterHandles(panel!);
            }
            else if (panel != null)
            {
                debug.EveryFrame($"menu-skip generation={menuGeneration} settled={settled} scene={currentSceneName} panelActive={IsActiveAndEnabled(panel)} panelVisible={IsVisible(panel)} buttons={SafeActiveButtonCount(panel)}");
            }

            PublishStatusIfChanged(ready, available);
        }

        // 主菜单是否已加载，直接问 Unity 而不是等 API 自己的场景差集：模组常常在进入主菜单的那一帧
        // 就请求按钮，而那份差集可能要到下一帧才更新。
        private static bool IsMainMenuSceneLoaded()
        {
            try
            {
                Scene scene = SceneManager.GetSceneByName("MainMenu");
                return scene.IsValid() && scene.isLoaded;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // 面板每帧全场景扫描太贵，所以只在主菜单场景里（或调用方此刻就要用）解析。
        // `MainMenu.panel` 由原生在初始化过程中赋值，场景加载完但还没赋值时退回扫描活的 `MenuPanel`。
        private MenuPanel? EnsurePanel(bool allowSearch)
        {
            if (mainMenu != null && !IsActiveAndEnabled(mainMenu))
                ResetNativeState();
            if (mainMenu == null && allowSearch)
                mainMenu = FindMainMenu();

            MenuPanel? panel = mainMenu == null ? null : SafeMenuPanel(mainMenu);
            if (allowSearch && (panel == null || SafeButtonPool(panel) == null))
                panel = FindLiveMenuPanel();
            menuPanel = panel != null && SafeButtonPool(panel) != null ? panel : null;
            return menuPanel;
        }

        private static MainMenu? FindMainMenu()
        {
            // `MainMenu` 是主菜单场景的 `MainSceneRoot`；场景加载完就在，未加载时找不到。
            MainMenu[] candidates = Resources.FindObjectsOfTypeAll<MainMenu>();
            foreach (MainMenu candidate in candidates)
            {
                if (candidate != null && IsActiveAndEnabled(candidate))
                    return candidate;
            }

            return null;
        }

        private static MenuPanel? FindLiveMenuPanel()
        {
            MenuPanel[] candidates = Resources.FindObjectsOfTypeAll<MenuPanel>();
            foreach (MenuPanel candidate in candidates)
            {
                if (candidate != null && IsActiveAndEnabled(candidate) && SafeButtonPool(candidate) != null)
                    return candidate;
            }

            return null;
        }

        private static MenuPanel? SafeMenuPanel(MainMenu menu)
        {
            try
            {
                return menu.panel;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static ObjectPool<Tab>? SafeButtonPool(MenuPanel panel)
        {
            try
            {
                return panel.buttonPool;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int SafeActiveButtonCount(MenuPanel? panel)
        {
            if (panel == null)
                return 0;

            try
            {
                return panel.ActiveButtonCount;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static IntPtr SafePointer(UnityEngine.Object? value)
        {
            if (value == null)
                return IntPtr.Zero;

            try
            {
                return value.Pointer;
            }
            catch (Exception)
            {
                return IntPtr.Zero;
            }
        }

        private static string SafeLabel(Tab? tab)
        {
            if (tab == null)
                return "";

            try
            {
                return tab.Label ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        // 原生 Tab 是否仍在面板的对象池 active 列表里。`GetActive(i)` 按槽位顺序数第 i 个
        // active 元素，`ActiveCount` 是 active 元素个数（原生 `MenuPanel.Clear` 与
        // `ObjectPool.ReturnAllToPool` 都把它清零），所以这里能直接还原面板当前的按钮集合。
        private static bool IsActiveInPool(MenuPanel panel, Tab candidate)
        {
            ObjectPool<Tab>? pool = SafeButtonPool(panel);
            if (pool == null)
                return false;

            try
            {
                int count = pool.ActiveCount;
                for (int index = 0; index < count; index++)
                {
                    Tab? active = pool.GetActive(index);
                    if (active != null && active.Pointer == candidate.Pointer)
                        return true;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        private static List<IntPtr> ActivePointers(MenuPanel panel)
        {
            List<IntPtr> pointers = new();
            ObjectPool<Tab>? pool = SafeButtonPool(panel);
            if (pool == null)
                return pointers;

            try
            {
                int count = pool.ActiveCount;
                for (int index = 0; index < count; index++)
                {
                    Tab? active = pool.GetActive(index);
                    if (active != null)
                        pointers.Add(active.Pointer);
                }
            }
            catch (Exception)
            {
                pointers.Clear();
            }

            return pointers;
        }

        private static List<Tab> ActiveTabs(MenuPanel panel)
        {
            List<Tab> tabs = new();
            ObjectPool<Tab>? pool = SafeButtonPool(panel);
            if (pool == null)
                return tabs;

            try
            {
                int count = pool.ActiveCount;
                for (int index = 0; index < count; index++)
                {
                    Tab? active = pool.GetActive(index);
                    if (active != null)
                        tabs.Add(active);
                }
            }
            catch (Exception)
            {
                tabs.Clear();
            }

            return tabs;
        }

        private void RegisterHandles(MenuPanel panel)
        {
            foreach (IUiMenuButtonHandle handle in new List<IUiMenuButtonHandle>(handles))
            {
                if (handle is not UnityMenuButtonHandle nativeHandle)
                    continue;
                if (nativeHandle.TryRegister(panel))
                    debug.Lifecycle($"register generation={menuGeneration} text={nativeHandle.Text}");
                if (nativeHandle.ConsumeActivityChange(out bool activeSelf, out bool activeInHierarchy, out string path))
                    // 这是**成功**注册后的活动状态诊断，不是失败：默认关闭的 UI trace，避免把正常
                    // 生命周期当错误刷进 Latest.log（真正的失败仍然走 error/warn）。
                    debug.Lifecycle($"native-registration text={nativeHandle.Text} activeSelf={activeSelf} activeInHierarchy={activeInHierarchy} path={path}");
            }
        }

        // 清掉指向主菜单场景对象的引用。场景卸载后这些代理都会变成死的 IL2CPP 对象。
        private void ResetNativeState()
        {
            mainMenu = null;
            menuPanel = null;
            observedScreenPointer = IntPtr.Zero;
            settleFrames = 0;
            menuGeneration++;
        }

        internal void SceneLoaded(string sceneName)
        {
            currentSceneName = sceneName ?? "";
            if (!string.Equals(currentSceneName, "MainMenu", StringComparison.OrdinalIgnoreCase))
                ResetNativeState();
            MenuPanel? panel = EnsurePanel(allowSearch: IsMainMenuSceneLoaded());
            PublishStatusIfChanged(false, panel != null ? UiCapability.MenuButton : UiCapability.None);
            debug.Lifecycle($"scene-loaded scene={currentSceneName}");
        }

        public UiCreateResult<IUiMenuButtonHandle> CreateMenuButton(string ownerId, UiMenuButtonDefinition definition)
        {
            if (disposed)
                return UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.OwnerDisposed, "UI backend is disposed.");
            if (definition.Parent == null && string.IsNullOrWhiteSpace(definition.BelowNativeButtonText))
                return UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.InvalidParent, "Menu button parent is null.");
            Canvas? canvas = definition.Parent?.GetComponentInParent<Canvas>();
            if (definition.Parent != null && (canvas == null || !IsActiveAndEnabled(canvas) || canvas.GetComponent<GraphicRaycaster>() == null))
                return UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.InvalidParent, "Menu button parent must be under an active Canvas with GraphicRaycaster.");
            // 原生 `MenuPanel` 与它的按钮池由游戏在场景加载过程中逐步建好；请求按钮的模组往往只在
            // 进入主菜单那一帧调用一次，不能因为这一刻还没就绪就让它拿不到按钮。所以这里只登记意图，
            // 真正的原生按钮由 `Update` 在面板就绪后建一次。
            MenuPanel? panel = EnsurePanel(allowSearch: true);
            if (panel == null && !IsMainMenuSceneLoaded())
                return UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.SceneUnavailable, "The main menu scene is not loaded.");

            try
            {
                string text = definition.Text ?? "";
                Action onClick = definition.OnClick == null
                    ? NoOpClick
                    : WrapCallback(ownerId, "MenuButton", definition.OnClick)!;
                var handle = new UnityMenuButtonHandle(warn, (UnityAction)onClick, text, definition.Enabled, definition.Selected, definition.BelowNativeButtonText, () => handles.RemoveAll(item => item.IsDisposed));
                handles.Add(handle);
                return UiCreateResult<IUiMenuButtonHandle>.Success(handle);
            }
            catch (Exception exception)
            {
                error($"[SMA-UI] create menu button failed owner={ownerId}: {exception}");
                return UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.CreationFailed, exception.Message);
            }
        }

        // 原生按钮就位（`ActiveButtonCount > 0`）才算面板可供模组安放按钮：这时锚点与布局都已画好。
        private static bool IsPanelReady(MenuPanel? panel)
            => panel != null && IsActiveAndEnabled(panel) && SafeButtonPool(panel) != null && SafeActiveButtonCount(panel) > 0;

        private Action? WrapCallback(string ownerId, string capability, Action? callback)
        {
            if (callback == null)
                return null;
            return () =>
            {
                try { callback(); }
                catch (Exception exception) { error($"[SMA-UI] callback failed owner={ownerId} capability={capability}: {exception}"); }
            };
        }

        private static Tab? FindActiveButton(MenuPanel panel, string label)
        {
            foreach (Tab candidate in ActiveTabs(panel))
            {
                if (string.Equals(SafeLabel(candidate), label, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            return null;
        }

        // 原生 `MenuPanel.Button` 从 `buttonPool.Get()` 取一个未激活的槽位并激活它（`GetNextFreeIndex`
        // 每次调用都重扫并把游标复位），所以新按钮就是池 active 列表里相对调用前多出来的那个元素。
        private static Tab? CreateTab(MenuPanel panel, string text, UnityAction? action, bool interactable)
        {
            List<IntPtr> before = ActivePointers(panel);
            panel.Button(text, action!, interactable);
            foreach (Tab candidate in ActiveTabs(panel))
            {
                if (before.Contains(candidate.Pointer))
                    continue;
                if (string.Equals(SafeLabel(candidate), text, StringComparison.Ordinal))
                    return candidate;
            }

            return null;
        }

        // 把模组按钮插到指定原生按钮下方。`ListLayout` 的顺序由 `Add` 调用顺序决定（没有 Insert），
        // 所以先清内部列表再按目标顺序补回全部 active 按钮：原生切屏只对按钮池 `ReturnAllToPool`
        // 再重新 `Add`，布局列表里会留下上一轮的条目，这一步同时把它们换掉。
        private static void PlaceBelow(MenuPanel panel, Tab tab, string? anchorText)
        {
            if (string.IsNullOrWhiteSpace(anchorText))
                return;
            ListLayout? layout = SafeLayout(panel);
            if (layout == null)
                return;

            List<Tab> ordered = ActiveTabs(panel);
            ordered.RemoveAll(candidate => candidate.Pointer == tab.Pointer);
            int anchorIndex = ordered.FindIndex(candidate => string.Equals(SafeLabel(candidate), anchorText, StringComparison.OrdinalIgnoreCase));
            if (anchorIndex < 0)
                return;
            ordered.Insert(anchorIndex + 1, tab);

            layout.Clear();
            foreach (Tab current in ordered)
            {
                RectTransform? rect = current.GetComponent<RectTransform>();
                if (rect != null)
                    layout.Add(rect);
            }
            layout.ForceRelayout();
        }

        private static ListLayout? SafeLayout(MenuPanel panel)
        {
            try
            {
                return panel.layout;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            Transform? current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        public void SceneUnloaded() => SceneUnloaded("MainMenu");

        internal void SceneUnloaded(string sceneName)
        {
            bool unloadingMainMenu = string.Equals(sceneName, "MainMenu", StringComparison.OrdinalIgnoreCase);
            if (unloadingMainMenu)
            {
                foreach (IUiMenuButtonHandle handle in new List<IUiMenuButtonHandle>(handles))
                {
                    if (handle is UnityMenuButtonHandle nativeHandle)
                        nativeHandle.InvalidateForScene();
                    else
                        handle.Dispose();
                }

                ResetNativeState();
                currentSceneName = "";
            }
            else if (menuPanel != null)
            {
                currentSceneName = "MainMenu";
                debug.Lifecycle($"scene-unloaded-overlay scene={sceneName} resume=MainMenu");
            }
            PublishStatusIfChanged(false, menuPanel != null ? UiCapability.MenuButton : UiCapability.None);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            StatusChanged = null;
            SceneUnloaded("MainMenu");
        }

        private void PublishStatusIfChanged(bool isMainMenuReady, UiCapability? available = null)
        {
            UiCapabilitySnapshot next = new UiCapabilitySnapshot
            {
                GameVersion = SupportedGameVersion,
                Available = available ?? Capabilities.Available,
                SceneName = currentSceneName,
                IsMainMenuReady = isMainMenuReady,
                MenuGeneration = menuGeneration
            };
            UiCapabilitySnapshot previous = Capabilities;
            if (SameStatus(previous, next))
                return;
            Capabilities = next;
            StatusChanged?.Invoke(this, new UiStatusChangedEventArgs(previous, next));
        }

        private static bool SameStatus(UiCapabilitySnapshot left, UiCapabilitySnapshot right) =>
            left.GameVersion == right.GameVersion && left.Available == right.Available
            && string.Equals(left.SceneName, right.SceneName, StringComparison.Ordinal)
            && left.IsMainMenuReady == right.IsMainMenuReady && left.MenuGeneration == right.MenuGeneration;

        private sealed class UnityMenuButtonHandle : IUiMenuButtonHandle
        {
            private readonly Action<string>? warn;
            private readonly UnityAction? action;
            private readonly Action collect;
            private readonly string? belowNativeButtonText;
            private MenuPanel? panel;
            private Tab? tab;
            private string registeredText;
            private bool disposed;
            private bool enabled;
            private bool selected;
            private bool lastActive;
            private bool activityInitialized;
            private bool anchorWarned;

            internal UnityMenuButtonHandle(Action<string>? warn, UnityAction? action, string registeredText, bool enabled, bool selected, string? belowNativeButtonText, Action collect)
            {
                this.warn = warn;
                this.action = action;
                this.registeredText = registeredText;
                this.enabled = enabled;
                this.selected = selected;
                this.belowNativeButtonText = belowNativeButtonText;
                this.collect = collect;
            }

            // 每个句柄在同一个面板实例里最多拥有一个原生按钮：只有确认它已不在池里
            // （或已被原生重画复用成别的按钮）才补建一个。
            internal bool TryRegister(MenuPanel current)
            {
                if (disposed)
                    return false;
                if (panel == null || panel.Pointer != current.Pointer)
                {
                    // 面板实例换了：旧按钮随上一个场景/布局销毁，重新登记。
                    panel = current;
                    tab = null;
                }
                else if (OwnsNativeTab(current))
                {
                    return false;
                }

                // 锚点由原生 `DrawMainMenu` 随切屏重画；它还没出现时先不建，否则按钮会落在列表末尾，
                // 也就是"静默放到错误位置"。
                if (!string.IsNullOrWhiteSpace(belowNativeButtonText) && FindActiveButton(current, belowNativeButtonText!) == null)
                {
                    WarnAnchorMissingOnce();
                    return false;
                }

                Tab? created = CreateTab(current, registeredText, action, enabled);
                if (created == null)
                    return false;
                tab = created;
                PlaceBelow(current, created, belowNativeButtonText);
                current.Apply();
                ApplyNativeState();
                return true;
            }

            private void WarnAnchorMissingOnce()
            {
                if (anchorWarned || warn == null)
                    return;
                anchorWarned = true;
                warn($"[SMA-UI] native menu button anchor '{belowNativeButtonText}' is not on the current main-menu panel; the button waits for it.");
            }

            private bool OwnsNativeTab(MenuPanel current)
            {
                Tab? current2 = tab;
                if (current2 == null)
                    return false;
                bool owned = MenuButtonOwnership.IsOwned(registeredText, SafeLabel(current2), IsActiveInPool(current, current2));
                if (!owned)
                    tab = null;
                return owned;
            }

            internal void InvalidateForScene()
            {
                tab = null;
                panel = null;
            }

            internal bool ConsumeActivityChange(out bool activeSelf, out bool activeInHierarchy, out string path)
            {
                activeSelf = false;
                activeInHierarchy = false;
                path = "none";
                try
                {
                    activeSelf = tab != null && tab.gameObject != null && tab.gameObject.activeSelf;
                    activeInHierarchy = tab != null && tab.gameObject != null && tab.gameObject.activeInHierarchy;
                    path = tab == null ? "none" : GetHierarchyPath(tab.transform);
                }
                catch (Exception) { }
                bool changed = !activityInitialized || activeSelf != lastActive;
                activityInitialized = true;
                lastActive = activeSelf;
                return changed;
            }

            public bool IsDisposed => disposed;
            public bool Enabled
            {
                get => !disposed && enabled;
                set
                {
                    if (disposed) return;
                    enabled = value;
                    ApplyNativeState();
                }
            }

            public string Text
            {
                get => !disposed && tab != null ? SafeLabel(tab) : registeredText;
                set
                {
                    if (disposed) return;
                    registeredText = value ?? "";
                    Tab? current = tab;
                    if (current == null) return;
                    try { current.Label = registeredText; }
                    catch (Exception) { tab = null; }
                }
            }

            public bool Selected
            {
                get => !disposed && selected;
                set
                {
                    if (disposed) return;
                    selected = value;
                    ApplyNativeState();
                }
            }

            internal void ApplyNativeState()
            {
                Tab? current = tab;
                if (current == null)
                    return;
                try { current.SetState(!enabled ? TabState.Disabled : selected ? TabState.Selected : TabState.Normal); }
                catch (Exception) { tab = null; }
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                // Native pooled objects are game-owned; MenuPanel lifecycle controls their destruction.
                try { collect(); } catch (Exception) { }
            }
        }
    }

    internal sealed class DispatchingUiBackend : IUiBackend
    {
        private readonly IUiBackend inner;
        private readonly Func<Func<UiCreateResult<IUiMenuButtonHandle>>, UiCreateResult<IUiMenuButtonHandle>> invokeMenu;
        private readonly Action<Action> invokeVoid;
        private readonly Func<Func<bool>, bool> invokeBool;
        private readonly Func<Func<string>, string> invokeString;

        internal DispatchingUiBackend(
            IUiBackend inner,
            Func<Func<UiCreateResult<IUiMenuButtonHandle>>, UiCreateResult<IUiMenuButtonHandle>> invokeMenu,
            Action<Action> invokeVoid,
            Func<Func<bool>, bool> invokeBool,
            Func<Func<string>, string> invokeString)
        {
            this.inner = inner;
            this.invokeMenu = invokeMenu;
            this.invokeVoid = invokeVoid;
            this.invokeBool = invokeBool;
            this.invokeString = invokeString;
        }

        public UiCapabilitySnapshot Capabilities => inner.Capabilities;
        public event EventHandler<UiStatusChangedEventArgs> StatusChanged
        {
            add => inner.StatusChanged += value;
            remove => inner.StatusChanged -= value;
        }
        public UiCreateResult<IUiMenuButtonHandle> CreateMenuButton(string ownerId, UiMenuButtonDefinition definition)
        {
            UiCreateResult<IUiMenuButtonHandle> result = invokeMenu(() => inner.CreateMenuButton(ownerId, definition));
            return result.Succeeded
                ? UiCreateResult<IUiMenuButtonHandle>.Success(new DispatchingButtonHandle(result.Value!, invokeVoid, invokeBool, invokeString))
                : result;
        }

        public void SceneUnloaded() => inner.SceneUnloaded();
        public void Dispose() { }

        private sealed class DispatchingButtonHandle : IUiMenuButtonHandle
        {
            private readonly IUiMenuButtonHandle inner;
            private readonly Action<Action> invokeVoid;
            private readonly Func<Func<bool>, bool> invokeBool;
            private readonly Func<Func<string>, string> invokeString;

            internal DispatchingButtonHandle(IUiMenuButtonHandle inner, Action<Action> invokeVoid, Func<Func<bool>, bool> invokeBool, Func<Func<string>, string> invokeString)
            {
                this.inner = inner;
                this.invokeVoid = invokeVoid;
                this.invokeBool = invokeBool;
                this.invokeString = invokeString;
            }

            public bool IsDisposed => invokeBool(() => inner.IsDisposed);
            public bool Enabled { get => invokeBool(() => inner.Enabled); set => invokeVoid(() => inner.Enabled = value); }
            public string Text { get => invokeString(() => inner.Text); set => invokeVoid(() => inner.Text = value); }
            public bool Selected { get => invokeBool(() => inner.Selected); set => invokeVoid(() => inner.Selected = value); }
            public void Dispose() => invokeVoid(inner.Dispose);
        }
    }
}

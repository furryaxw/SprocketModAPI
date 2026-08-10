using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Il2CppTMPro;
using Il2CppSprocket.UI;
using Il2CppSprocket.Selection;
using Il2CppSprocket;
using Il2CppSprocket.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace SprocketModAPI
{
    internal sealed class UiService : IUiService, IDisposable
    {
        private readonly Action<string> warn;
        private readonly Action<string> error;
        private readonly int mainThreadId;
        private readonly Queue<Action> pending = new();
        private readonly List<UiScopeCore> scopes = new();
        private readonly UnityUiBackend backend;
        private readonly IUiBackend dispatchedBackend;
        private bool disposed;

        internal UiService(Action<string> warn, Action<string> error)
        {
            this.warn = warn;
            this.error = error;
            mainThreadId = Environment.CurrentManagedThreadId;
            backend = new UnityUiBackend(error);
            dispatchedBackend = new DispatchingUiBackend(
                backend,
                action => Invoke(action),
                action => Invoke(action),
                action => Invoke(action),
                action => Invoke(action),
                action => Invoke(action));
        }

        public UiCapabilitySnapshot Capabilities => backend.Capabilities;

        public IUiScope CreateScope(UiOwnerDefinition owner)
        {
            if (owner == null || string.IsNullOrWhiteSpace(owner.ModId))
                throw new ArgumentException("UI owner ModId is required.", nameof(owner));
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
            Update();
            disposed = true;
            foreach (UiScopeCore scope in scopes)
                scope.Dispose();
            scopes.Clear();
            backend.Dispose();
            lock (pending)
            {
                pending.Clear();
            }
        }
    }

    internal sealed class UnityUiBackend : IUiBackend
    {
        private readonly Action<string> error;
        private readonly List<IUiMenuButtonHandle> handles = new();
        private bool disposed;
        private Tab? menuButtonTemplate;
        private Button? buttonTemplate;
        private MenuPanel? menuPanel;
        private MainMenu? mainMenu;
        private IntPtr observedMenuPointer;
        private int menuTransitionFrames;
        private int menuGeneration;
        private int traceFrame;
        private string currentSceneName = "";

        internal UnityUiBackend(Action<string> error)
        {
            this.error = error;
            Capabilities = new UiCapabilitySnapshot { GameVersion = new Version(0, 2, 53, 2), Available = UiCapability.Button };
            RefreshCapabilities();
        }

        public UiCapabilitySnapshot Capabilities { get; private set; }

        internal void Update()
        {
            if (disposed)
                return;
            traceFrame++;
            if (!string.Equals(currentSceneName, "MainMenu", StringComparison.OrdinalIgnoreCase))
                return;
            if (mainMenu == null)
            {
                MainSceneRoot[] roots = Resources.FindObjectsOfTypeAll<MainSceneRoot>();
                foreach (MainSceneRoot root in roots)
                {
                    if (root is MainMenu candidate && candidate.isActiveAndEnabled)
                    {
                        mainMenu = candidate;
                        break;
                    }
                }
            }
            MainMenuScreen? activeMenu = mainMenu?.activeMenu;
            IntPtr activeMenuPointer = activeMenu?.Pointer ?? IntPtr.Zero;
            if (activeMenuPointer != observedMenuPointer)
            {
                observedMenuPointer = activeMenuPointer;
                menuGeneration++;
                menuTransitionFrames = 2;
                error($"[SMA-UI-TRACE] menu-transition generation={menuGeneration} pointer=0x{activeMenuPointer.ToInt64():X} title={activeMenu?.Title ?? "<null>"}");
                foreach (IUiMenuButtonHandle handle in new List<IUiMenuButtonHandle>(handles))
                    if (handle is UnityMenuButtonHandle nativeHandle)
                    {
                        nativeHandle.InvalidateForScene();
                    }
            }
            if (menuTransitionFrames > 0)
            {
                menuTransitionFrames--;
                error($"[SMA-UI-TRACE] menu-blocked generation={menuGeneration} remaining={menuTransitionFrames}");
                return;
            }
            bool mainMenuScene = string.Equals(currentSceneName, "MainMenu", StringComparison.OrdinalIgnoreCase);
            bool mainMenuPanelReady = menuPanel != null && menuPanel.isActiveAndEnabled && mainMenuScene;
            if (activeMenu == null && !mainMenuPanelReady)
            {
                if (traceFrame % 30 == 0)
                    error($"[SMA-UI-TRACE] menu-skip generation={menuGeneration} reason=no-active-menu scene={currentSceneName} panelActive={menuPanel?.isActiveAndEnabled ?? false} panelVisible={menuPanel?.Visible ?? false}");
                return;
            }
            if (activeMenu != null && !string.Equals(activeMenu.Title, "Main Menu", StringComparison.OrdinalIgnoreCase))
            {
                error($"[SMA-UI-TRACE] menu-skip generation={menuGeneration} reason=title title={activeMenu.Title}");
                return;
            }
            foreach (IUiMenuButtonHandle handle in new List<IUiMenuButtonHandle>(handles))
            {
                if (handle is UnityMenuButtonHandle nativeHandle)
                {
                    if (nativeHandle.EnsureRegistered(menuPanel, menuGeneration, CreateRegisteredTab))
                        error($"[SMA-UI-TRACE] register generation={menuGeneration} text={nativeHandle.Text}");
                    if (nativeHandle.ConsumeActivityChange(out bool activeSelf, out bool activeInHierarchy, out string path))
                        error($"[SMA-UI] native-registration text={nativeHandle.Text} activeSelf={activeSelf} activeInHierarchy={activeInHierarchy} path={path}");
                }
            }
        }

        internal void RefreshCapabilities()
        {
            menuButtonTemplate = null;
            buttonTemplate = null;
            menuPanel = null;
            mainMenu = null;
            observedMenuPointer = IntPtr.Zero;
            menuTransitionFrames = 0;
            menuGeneration++;
            try
            {
                MenuPanel[] panels = Resources.FindObjectsOfTypeAll<MenuPanel>();
                foreach (MenuPanel panel in panels)
                {
                    if (panel != null && panel.gameObject.activeInHierarchy && panel.buttonPrefab != null)
                    {
                        menuPanel = panel;
                        menuButtonTemplate = panel.buttonPrefab;
                        Tab[] liveTabs = panel.GetComponentsInChildren<Tab>(true);
                        foreach (Tab liveTab in liveTabs)
                        {
                            if (liveTab == null || !liveTab.gameObject.activeInHierarchy)
                                continue;
                            string livePath = GetHierarchyPath(liveTab.transform);
                            if (livePath.Contains("Menu Buttons/Menu Button", StringComparison.OrdinalIgnoreCase))
                            {
                                menuButtonTemplate = liveTab;
                                break;
                            }
                        }
                        break;
                    }
                }
                Tab[] candidates = Resources.FindObjectsOfTypeAll<Tab>();
                Tab? liveMenuButton = null;
                string liveMenuButtonPath = "";
                foreach (Tab candidate in candidates)
                {
                    if (candidate == null || !candidate.gameObject.activeInHierarchy)
                        continue;
                    string path = GetHierarchyPath(candidate.transform);
                    if (path.Contains("Menu Panel/Content/Menu Buttons/Menu Button", StringComparison.OrdinalIgnoreCase)
                        && path.EndsWith("Menu Button(Clone)", StringComparison.OrdinalIgnoreCase)
                        && path.Length > liveMenuButtonPath.Length)
                    {
                        liveMenuButton = candidate;
                        liveMenuButtonPath = path;
                    }
                }
                if (liveMenuButton != null)
                    menuButtonTemplate = liveMenuButton;
                if (menuButtonTemplate == null) foreach (Tab candidate in candidates)
                {
                    if (candidate == null || candidate.hideFlags != HideFlags.HideAndDontSave)
                        continue;
                    RectTransform? candidateRect = candidate.GetComponent<RectTransform>();
                    if (candidateRect == null || candidateRect.rect.width <= 1f || candidateRect.rect.height <= 1f
                        || candidateRect.rect.width > 600f || candidateRect.rect.height > 100f)
                        continue;
                    if (candidate.Label != null && candidate.OnClick != null)
                    {
                        menuButtonTemplate = candidate;
                        break;
                    }
                }
                Button[] buttonCandidates = Resources.FindObjectsOfTypeAll<Button>();
                foreach (Button candidate in buttonCandidates)
                {
                    if (candidate == null || candidate.hideFlags != HideFlags.HideAndDontSave)
                        continue;
                    RectTransform? rect = candidate.GetComponent<RectTransform>();
                    Image? image = candidate.GetComponent<Image>();
                    if (rect == null || image == null || rect.rect.width <= 1f || rect.rect.height <= 1f
                        || rect.rect.width > 600f || rect.rect.height > 100f)
                        continue;
                    buttonTemplate = candidate;
                    break;
                }
            }
            catch (Exception exception)
            {
                error($"[SMA-UI] Menu Button template probe failed: {exception}");
            }

            error($"[SMA-UI] template probe menuPanel={(menuPanel == null ? "none" : GetHierarchyPath(menuPanel.transform))} tab={(menuButtonTemplate == null ? "none" : GetHierarchyPath(menuButtonTemplate.transform))}");

            Capabilities = new UiCapabilitySnapshot
            {
                GameVersion = new Version(0, 2, 53, 2),
                Available = (buttonTemplate == null ? UiCapability.None : UiCapability.Button)
                    | (menuButtonTemplate == null ? UiCapability.None : UiCapability.MenuButton)
            };
        }

        internal void SceneLoaded(string sceneName)
        {
            currentSceneName = sceneName ?? "";
            if (!string.Equals(currentSceneName, "MainMenu", StringComparison.OrdinalIgnoreCase))
            {
                mainMenu = null;
                menuPanel = null;
                observedMenuPointer = IntPtr.Zero;
                menuTransitionFrames = 0;
            }
            RefreshCapabilities();
            error($"[SMA-UI-TRACE] scene-loaded scene={currentSceneName}");
        }

        public UiCreateResult<IUiButtonHandle> CreateButton(string ownerId, UiButtonDefinition definition)
        {
            if (buttonTemplate == null)
                RefreshCapabilities();
            if (disposed)
                return UiCreateResult<IUiButtonHandle>.Failed(UiFailureCode.OwnerDisposed, "UI backend is disposed.");
            if (definition.Parent == null)
                return UiCreateResult<IUiButtonHandle>.Failed(UiFailureCode.InvalidParent, "Button parent is null.");
            Canvas? canvas = definition.Parent.GetComponentInParent<Canvas>();
            if (canvas == null || !canvas.isActiveAndEnabled || canvas.GetComponent<GraphicRaycaster>() == null)
                return UiCreateResult<IUiButtonHandle>.Failed(UiFailureCode.InvalidParent, "Button parent must be under an active Canvas with GraphicRaycaster.");
            if (!Capabilities.Supports(UiCapability.Button) || buttonTemplate == null)
                return UiCreateResult<IUiButtonHandle>.Failed(UiFailureCode.CapabilityUnavailable, "Button capability is unavailable.");

            try
            {
                UiCreateResult<UnityButtonHandle> result = Create(ownerId, definition.Parent, definition.Text ?? "", definition.Enabled, false, definition.Size, definition.AnchoredPosition, definition.OnClick);
                return result.Succeeded ? UiCreateResult<IUiButtonHandle>.Success(result.Value!) : UiCreateResult<IUiButtonHandle>.Failed(result.Failure, result.Message);
            }
            catch (Exception exception)
            {
                error($"[SMA-UI] create button failed owner={ownerId}: {exception}");
                return UiCreateResult<IUiButtonHandle>.Failed(UiFailureCode.CreationFailed, exception.Message);
            }
        }

        public UiCreateResult<IUiMenuButtonHandle> CreateMenuButton(string ownerId, UiMenuButtonDefinition definition)
        {
            if (menuButtonTemplate == null)
                RefreshCapabilities();
            if (disposed)
                return UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.OwnerDisposed, "UI backend is disposed.");
            if (definition.Parent == null && string.IsNullOrWhiteSpace(definition.BelowNativeButtonText))
                return UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.InvalidParent, "Menu button parent is null.");
            Canvas? canvas = definition.Parent?.GetComponentInParent<Canvas>();
            if (definition.Parent != null && (canvas == null || !canvas.isActiveAndEnabled || canvas.GetComponent<GraphicRaycaster>() == null))
                return UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.InvalidParent, "Menu button parent must be under an active Canvas with GraphicRaycaster.");
            if (!Capabilities.Supports(UiCapability.MenuButton))
                return UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.TemplateNotFound, "No verified Sprocket Tab template is available.");

            try
            {
                return CreateMenu(ownerId, definition);
            }
            catch (Exception exception)
            {
                error($"[SMA-UI] create menu button failed owner={ownerId}: {exception}");
                return UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.CreationFailed, exception.Message);
            }
        }

        private UiCreateResult<IUiMenuButtonHandle> CreateMenu(string ownerId, UiMenuButtonDefinition definition)
        {
            if (menuButtonTemplate == null)
                return UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.TemplateNotFound, "Menu Button template is unavailable.");
            if (menuPanel != null && menuPanel.isActiveAndEnabled)
            {
                UnityAction? factoryAction = definition.OnClick == null ? null : (UnityAction)WrapCallback(ownerId, "MenuButton", definition.OnClick)!;
                Tab? tab = CreateRegisteredTab(menuPanel, definition.Text ?? "", factoryAction, definition.Enabled);
                if (tab == null)
                    return UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.CreationFailed, "MenuPanel.Button created no matching active Tab.");
                if (!TryPlaceBelow(menuPanel, tab, definition.BelowNativeButtonText))
                    return UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.TemplateNotFound, $"Native menu button anchor '{definition.BelowNativeButtonText}' was not found.");
                menuPanel.Apply();
                GameObject nativeRoot = tab.gameObject;
                var nativeHandle = new UnityMenuButtonHandle(nativeRoot, tab, factoryAction, definition.Text ?? "", definition.Enabled, definition.Selected, definition.BelowNativeButtonText, menuGeneration, () => handles.RemoveAll(item => item.IsDisposed));
                handles.Add(nativeHandle);
                return UiCreateResult<IUiMenuButtonHandle>.Success(nativeHandle);
            }
            if (definition.Parent == null)
                return UiCreateResult<IUiMenuButtonHandle>.Failed(UiFailureCode.SceneUnavailable, "Main menu MenuPanel is unavailable.");
            GameObject? root = null;
            try
            {
                root = UnityEngine.Object.Instantiate(menuButtonTemplate.gameObject, definition.Parent, false);
                RectTransform? menuRect = root.GetComponent<RectTransform>();
                if (menuRect != null)
                {
                    menuRect.anchorMin = new Vector2(0.5f, 0.5f);
                    menuRect.anchorMax = new Vector2(0.5f, 0.5f);
                    menuRect.pivot = new Vector2(0.5f, 0.5f);
                    menuRect.localScale = Vector3.one;
                    menuRect.sizeDelta = definition.Size ?? new Vector2(180f, 36f);
                    menuRect.anchoredPosition = definition.AnchoredPosition ?? Vector2.zero;
                }
                Tab tab = root.GetComponent<Tab>();
                if (tab == null)
                    throw new InvalidOperationException("Cloned Menu Button has no Tab component.");
                tab.Label = definition.Text ?? "";
                tab.SetState(definition.Selected ? TabState.Selected : definition.Enabled ? TabState.Normal : TabState.Disabled);
                Action? safeCallback = WrapCallback(ownerId, "MenuButton", definition.OnClick);
                UnityAction? action = safeCallback == null ? null : (UnityAction)safeCallback;
                tab.onClick = new UnityEvent();
                if (action != null)
                    tab.OnClick.AddListener(action);
                var handle = new UnityMenuButtonHandle(root, tab, action, definition.Text ?? "", definition.Enabled, definition.Selected, definition.BelowNativeButtonText, menuGeneration, () => handles.RemoveAll(item => item.IsDisposed));
                handle.Enabled = definition.Enabled;
                handle.Selected = definition.Selected;
                handles.Add(handle);
                return UiCreateResult<IUiMenuButtonHandle>.Success(handle);
            }
            catch
            {
                if (root != null) UnityEngine.Object.Destroy(root);
                throw;
            }
        }

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

        private static Tab? CreateRegisteredTab(MenuPanel panel, string text, UnityAction? action, bool interactable)
        {
            Il2CppSprocket.ObjectPool<Tab>? pool = panel.buttonPool;
            if (pool == null)
                return null;
            int before = pool.ActiveCount;
            panel.Button(text, action!, interactable);
            int after = pool.ActiveCount;
            for (int index = after - 1; index >= 0; index--)
            {
                Tab? candidate = pool.GetActive(index);
                if (candidate != null && candidate.gameObject.activeInHierarchy && candidate.Label == text)
                    return candidate;
            }
            return null;
        }

        private static bool TryPlaceBelow(MenuPanel panel, Tab tab, string? anchorText)
        {
            if (string.IsNullOrWhiteSpace(anchorText))
                return true;
            Il2CppSprocket.ObjectPool<Tab>? pool = panel.buttonPool;
            if (pool == null)
                return false;
            List<Tab> tabs = new();
            for (int index = 0; index < pool.ActiveCount; index++)
            {
                Tab? anchor = pool.GetActive(index);
                if (anchor != null)
                    tabs.Add(anchor);
            }
            int anchorIndex = tabs.FindIndex(candidate => candidate != tab
                && string.Equals(candidate.Label, anchorText, StringComparison.OrdinalIgnoreCase));
            if (anchorIndex < 0)
                return false;
            tabs.Remove(tab);
            tabs.Insert(Math.Min(anchorIndex + 1, tabs.Count), tab);
            ListLayout? layout = panel.GetComponent<ListLayout>();
            if (layout == null)
                return false;
            layout.Clear();
            foreach (Tab ordered in tabs)
            {
                RectTransform? rect = ordered.GetComponent<RectTransform>();
                if (rect != null)
                    layout.Add(rect);
            }
            layout.ForceRelayout();
            return true;
        }

        private UiCreateResult<UnityButtonHandle> Create(string ownerId, Transform parent, string text, bool enabled, bool selected, Vector2? size, Vector2? anchoredPosition, Action? callback)
        {
            GameObject? root = null;
            try
            {
                root = UnityEngine.Object.Instantiate(buttonTemplate!.gameObject, parent, false);
                RectTransform rect = root.GetComponent<RectTransform>() ?? throw new InvalidOperationException("Button template has no RectTransform.");
                Button button = root.GetComponent<Button>() ?? throw new InvalidOperationException("Button template has no Button component.");
                TextMeshProUGUI? label = root.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label == null) throw new InvalidOperationException("Button template has no TMP label.");
                label.text = text ?? "";
                label.raycastTarget = false;
                rect.sizeDelta = size ?? new Vector2(180f, 36f);
                if (anchoredPosition.HasValue) rect.anchoredPosition = anchoredPosition.Value;
                UnityAction? action = callback == null ? null : (UnityAction)callback;
                if (action != null) button.onClick.AddListener(action);
                var handle = new UnityButtonHandle(root, button, label, action, () => handles.RemoveAll(item => item.IsDisposed));
                handle.Enabled = enabled;
                handle.Selected = selected;
                handles.Add(handle);
                return UiCreateResult<UnityButtonHandle>.Success(handle);
            }
            catch
            {
                if (root != null) UnityEngine.Object.Destroy(root);
                throw;
            }
        }

        public void SceneUnloaded() => SceneUnloaded("MainMenu");

        internal void SceneUnloaded(string sceneName)
        {
            foreach (IUiMenuButtonHandle handle in new List<IUiMenuButtonHandle>(handles))
            {
                if (handle is UnityMenuButtonHandle nativeHandle)
                    nativeHandle.InvalidateForScene();
                else
                    handle.Dispose();
            }
            menuButtonTemplate = null;
            buttonTemplate = null;
            bool unloadingMainMenu = string.Equals(sceneName, "MainMenu", StringComparison.OrdinalIgnoreCase);
            if (unloadingMainMenu)
            {
                mainMenu = null;
                observedMenuPointer = IntPtr.Zero;
                menuTransitionFrames = 0;
                menuGeneration++;
                menuPanel = null;
                currentSceneName = "";
            }
            else if (menuPanel != null)
            {
                currentSceneName = "MainMenu";
                error($"[SMA-UI-TRACE] scene-unloaded-overlay scene={sceneName} resume=MainMenu");
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            SceneUnloaded("MainMenu");
        }

        private sealed class UnityButtonHandle : IUiMenuButtonHandle
        {
            private readonly GameObject root;
            private readonly Button button;
            private readonly TextMeshProUGUI label;
            private readonly UnityAction? action;
            private readonly Action collect;
            private bool disposed;
            private bool selected;

            internal UnityButtonHandle(GameObject root, Button button, TextMeshProUGUI label, UnityAction? action, Action collect)
            {
                this.root = root;
                this.button = button;
                this.label = label;
                this.action = action;
                this.collect = collect;
            }

            public bool IsDisposed => disposed;
            public bool Enabled { get => !disposed && button.interactable; set { if (!disposed) button.interactable = value; } }
            public string Text { get => label.text; set { if (!disposed) label.text = value ?? ""; } }
            public bool Selected
            {
                get => !disposed && selected;
                set
                {
                    if (disposed) return;
                    selected = value;
                    Image? image = root.GetComponent<Image>();
                    if (image != null) image.color = value ? new Color(0.76f, 0.54f, 0.22f) : new Color(0.17f, 0.18f, 0.19f);
                }
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try { button.interactable = false; } catch (Exception) { }
                try { if (action != null) button.onClick.RemoveListener(action); } catch (Exception) { }
                try { if (root != null) UnityEngine.Object.Destroy(root); } catch (Exception) { }
                try { collect(); } catch (Exception) { }
            }
        }

        private sealed class UnityMenuButtonHandle : IUiMenuButtonHandle
        {
            private GameObject? root;
            private Tab? tab;
            private readonly UnityAction? action;
            private string registeredText;
            private readonly Action collect;
            private bool disposed;
            private bool enabled;
            private bool selected;
            private bool lastActive;
            private bool activityInitialized;
            private int registrationGeneration;
            private readonly string? belowNativeButtonText;

            internal UnityMenuButtonHandle(GameObject root, Tab tab, UnityAction? action, string registeredText, bool enabled, bool selected, string? belowNativeButtonText, int generation, Action collect)
            {
                this.root = root;
                this.tab = tab;
                this.action = action;
                this.registeredText = registeredText;
                this.enabled = enabled;
                this.selected = selected;
                registrationGeneration = generation;
                this.belowNativeButtonText = belowNativeButtonText;
                this.collect = collect;
            }

            internal bool EnsureRegistered(MenuPanel? panel, int generation, Func<MenuPanel, string, UnityAction?, bool, Tab?> createTab)
            {
                bool active;
                try { active = tab != null && tab.gameObject != null && tab.gameObject.activeInHierarchy; }
                catch (Exception) { active = false; }
                if (disposed || registrationGeneration == generation || panel == null || !panel.isActiveAndEnabled)
                    return false;
                Tab? existing = createTab(panel, registeredText, action, enabled);
                if (existing == null)
                    return false;
                tab = existing;
                root = existing.gameObject;
                registrationGeneration = generation;
                TryPlaceBelow(panel, existing, belowNativeButtonText);
                panel.Apply();
                return true;
            }

            public bool IsDisposed => disposed;
            public bool Enabled
            {
                get => !disposed && enabled;
                set
                {
                    if (disposed) return;
                    enabled = value;
                    ApplyState();
                }
            }

            internal void InvalidateForScene()
            {
                tab = null;
                root = null;
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

            public string Text
            {
                get => !disposed && tab != null ? tab.Label : registeredText;
                set
                {
                    if (disposed) return;
                    registeredText = value ?? "";
                    if (tab != null) tab.Label = registeredText;
                }
            }
            public bool Selected
            {
                get => !disposed && selected;
                set
                {
                    if (disposed) return;
                    selected = value;
                    ApplyState();
                }
            }

            private void ApplyState()
            {
                if (tab != null)
                    tab.SetState(!enabled ? TabState.Disabled : selected ? TabState.Selected : TabState.Normal);
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
        private readonly Func<Func<UiCreateResult<IUiButtonHandle>>, UiCreateResult<IUiButtonHandle>> invokeButton;
        private readonly Func<Func<UiCreateResult<IUiMenuButtonHandle>>, UiCreateResult<IUiMenuButtonHandle>> invokeMenu;
        private readonly Action<Action> invokeVoid;
        private readonly Func<Func<bool>, bool> invokeBool;
        private readonly Func<Func<string>, string> invokeString;

        internal DispatchingUiBackend(
            IUiBackend inner,
            Func<Func<UiCreateResult<IUiButtonHandle>>, UiCreateResult<IUiButtonHandle>> invokeButton,
            Func<Func<UiCreateResult<IUiMenuButtonHandle>>, UiCreateResult<IUiMenuButtonHandle>> invokeMenu,
            Action<Action> invokeVoid,
            Func<Func<bool>, bool> invokeBool,
            Func<Func<string>, string> invokeString)
        {
            this.inner = inner;
            this.invokeButton = invokeButton;
            this.invokeMenu = invokeMenu;
            this.invokeVoid = invokeVoid;
            this.invokeBool = invokeBool;
            this.invokeString = invokeString;
        }

        public UiCapabilitySnapshot Capabilities => inner.Capabilities;
        public UiCreateResult<IUiButtonHandle> CreateButton(string ownerId, UiButtonDefinition definition)
        {
            UiCreateResult<IUiButtonHandle> result = invokeButton(() => inner.CreateButton(ownerId, definition));
            return result.Succeeded
                ? UiCreateResult<IUiButtonHandle>.Success(new DispatchingButtonHandle(result.Value!, invokeVoid, invokeBool, invokeString))
                : result;
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

            internal DispatchingButtonHandle(IUiButtonHandle inner, Action<Action> invokeVoid, Func<Func<bool>, bool> invokeBool, Func<Func<string>, string> invokeString)
            {
                this.inner = (IUiMenuButtonHandle)inner;
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

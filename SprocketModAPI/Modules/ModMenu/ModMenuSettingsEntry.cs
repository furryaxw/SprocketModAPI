using System;
using System.Collections.Generic;
using Il2CppSprocket.Selection;
using Il2CppSprocket.SettingConfiguration;
using Il2CppSprocket.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SprocketModAPI
{
    // 设置页入口：在「设置 → General」页左下角放一个和原生按钮同款的 `MODS` 按钮。
    //
    // 定位方式与键位入口一致——对齐原生 `Content/Action buttons` 的左边界、底边距 84，
    // 只在 General 页激活时显示。这里**复制**了那套观察逻辑而不是复用键位模块的内部类型
    // （模块之间不允许依赖彼此的实现）。
    internal sealed class ModMenuSettingsEntry : IDisposable
    {
        private const float EntryWidth = ModMenuStyle.EntryWidth;
        private const float EntryHeight = ModMenuStyle.EntryHeight;
        private const float EntryBottom = ModMenuStyle.EntryBottom;
        private const float EntryFontSize = ModMenuStyle.EntryFontSize;

        // 入口与原生按钮顶边之间的间隙（屏幕像素）。
        private const float EntryGapAboveRow = 50f;

        // 当前页连续多少帧认不出来才提示降级（`activeMenu` 在场景刚加载那几帧还没就绪）。
        private const int UndetectablePageFrameLimit = 120;
        private const string EntryLabel = ModMenuStyle.EntryLabel;

        // 入口只出现在 General 页，判据是设置页签的 `Tab.State`。
        private const string GeneralPageLabel = "General";

        private readonly ModMenuWindow window;
        private readonly Action<string> warn;
        private readonly ApiLog log;
        private readonly Action<string> info;

        private Canvas? uiCanvas;
        private GameObject? uiRoot;
        private GameObject? uiEntryObject;
        private RectTransform? uiEntryRect;
        private Image? uiEntryImage;
        private Button? uiEntryButton;
        private Outline? uiEntryOutline;
        private Il2CppTMPro.TextMeshProUGUI? uiEntryLabel;
        private GameObject? observedSettingsRoot;
        private RectTransform? observedActionButtons;
        private SettingsMenu? settingsMenu;
        private Il2CppSystem.Reflection.FieldInfo? activeMenuField;
        private Tab[] settingsTabs = Array.Empty<Tab>();
        private bool settingsMenuActive;
        private bool generalPageActive;
        private bool activeMenuFieldMissing;
        private bool undetectablePageWarned;
        private int undetectableFrames;
        private bool entryAlignmentReady;
        // IL2CPP 的泛型方法缓存（`GetComponent<T>` 之类）在场景刚加载时可能还没就绪，
        // 第一次 Attach 会抛 `MethodInfoStoreGeneric_…` 的 TypeInitializationException。
        // 这不是致命错误：隔几帧重试就好，所以不能一次失败就永久放弃入口。
        private const int AttachRetryFrames = 20;
        private const int AttachRetryLimit = 15;
        private int attachFramesLeft;
        private int attachAttempts;
        private bool attachPending;
        private string attachScene = "";

        internal ModMenuSettingsEntry(ModMenuWindow window, Action<string> warn, Action<string> info)
        {
            this.window = window ?? throw new ArgumentNullException(nameof(window));
            this.warn = warn ?? throw new ArgumentNullException(nameof(warn));
            this.log = ApiLog.FromWarn(warn);
            this.info = info ?? throw new ArgumentNullException(nameof(info));
        }

        internal void SceneLoaded(string sceneName)
        {
            if (!string.Equals(sceneName, "SettingsMenu", StringComparison.Ordinal))
                return;

            attachScene = sceneName;
            attachAttempts = 0;
            attachPending = true;
            TryAttach();
        }

        // 每帧调用：刷新当前设置页、**每帧重新对齐**（原生按钮的排版随时可能变，事件驱动会漏），
        // 并在上一次 Attach 失败时按帧重试（IL2CPP 泛型缓存就绪后 Attach 就会成功）。
        internal void Update()
        {
            RefreshGeneralPageState();

            // 不做"成功一次就停"：原生那排在场景刚加载时量到的还是未排版的矩形，
            // 锁住它就得等某个事件来纠正（表现为"得手动开关一下原生那排按钮才正常"）。
            EnsureActionButtons();
            if (observedActionButtons != null)
                ApplyAlignment();

            if (!attachPending)
                return;
            if (attachFramesLeft > 0)
            {
                attachFramesLeft--;
                return;
            }

            TryAttach();
        }

        // 当前页只能从设置页签读：General 页的内容是运行时生成进共享布局的，没有名字叫 "General"
        // 的 GameObject 可以观察。`Tab.Label` 是页名，"Accept/Apply/Cancel" 也是 `Tab`，按名字排除；
        // 当前页的页签会是 `Selected`（不可再点的当前页有时被置成 `Disabled`，一并接受）。
        private void RefreshGeneralPageState()
        {
            if (observedSettingsRoot == null)
                return;

            if (settingsTabs.Length == 0 || HasMissingTab())
            {
                settingsTabs = observedSettingsRoot.GetComponentsInChildren<Tab>(true);
                if (settingsTabs.Length == 0)
                    return;
            }

            string page = ResolveActivePage();

            // 认不出当前页时宁可显示（入口消失比多显示更糟）；认得出就只在 General 显示。
            // `activeMenu` 在场景刚加载那几帧还没就绪，所以只有持续认不出来才算真的降级。
            bool active = page.Length == 0 || string.Equals(page, GeneralPageLabel, StringComparison.OrdinalIgnoreCase);
            if (page.Length == 0)
            {
                undetectableFrames++;
                if (!undetectablePageWarned && undetectableFrames > UndetectablePageFrameLimit)
                {
                    undetectablePageWarned = true;
                    log.Warn("[SMA-MENU] the active settings page cannot be identified; the MODS entry stays visible on every page.");
                }
            }
            else
            {
                undetectableFrames = 0;
            }

            if (active == generalPageActive)
                return;

            generalPageActive = active;
            RefreshVisibility();
        }

        private bool HasMissingTab()
        {
            foreach (Tab tab in settingsTabs)
            {
                if (tab == null)
                    return true;
            }

            return false;
        }

        // 当前页名。优先读 `SettingsMenu.activeMenu.MenuName`（"General"/"Gameplay"/…，都是代码里的字面量）：
        // General 页的内容是运行时生成进共享布局的，没有以页名命名的对象可观察，页签也不在设置根节点下
        // （实测那里只有三个 `Resume` 标签）。这个字段是私有的，只能反射读；读不到再退回页签状态。
        private string ResolveActivePage()
        {
            string byMenu = ReadActivePageName();
            if (byMenu.Length != 0)
                return byMenu;

            return ResolveActivePageByTabs();
        }

        // `SettingsMenu` 组件不在我们认识的那个根节点上（实测该节点自身与其父节点都没有它），
        // 所以最后按类型在整个场景里找。
        private SettingsMenu? FindSettingsMenu(GameObject settingsRoot)
        {
            SettingsMenu? menu = settingsRoot.GetComponent<SettingsMenu>();
            if (menu != null)
                return menu;

            menu = settingsRoot.GetComponentInParent<SettingsMenu>();
            if (menu != null)
                return menu;

            try
            {
                return UnityEngine.Object.FindObjectOfType<SettingsMenu>();
            }
            catch (Exception exception)
            {
                log.Warn($"[SMA-MENU] cannot locate the settings menu component: {exception.Message}");
                return null;
            }
        }

        private string ReadActivePageName()
        {
            SettingsMenu? menu = settingsMenu;
            if (menu == null)
                return "";

            try
            {
                if (activeMenuField == null && !activeMenuFieldMissing)
                {
                    Il2CppSystem.Type type = Il2CppInterop.Runtime.Il2CppType.Of<SettingsMenu>();
                    activeMenuField = type.GetField("activeMenu",
                        Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance);
                    if (activeMenuField == null)
                    {
                        activeMenuFieldMissing = true;
                        log.Warn("[SMA-MENU] SettingsMenu.activeMenu was not found; the settings entry falls back to the tab state.");
                        return "";
                    }
                }

                if (activeMenuField == null)
                    return "";

                Il2CppSystem.Object? active = activeMenuField.GetValue(menu);
                SettingsSubMenu? subMenu = active == null ? null : active.TryCast<SettingsSubMenu>();
                return subMenu == null ? "" : (subMenu.MenuName ?? "").Trim();
            }
            catch (Exception exception)
            {
                activeMenuFieldMissing = true;
                log.Warn($"[SMA-MENU] cannot read the active settings page: {exception.Message}");
                return "";
            }
        }

        private string ResolveActivePageByTabs()
        {
            string disabled = "";
            foreach (Tab tab in settingsTabs)
            {
                if (tab == null)
                    continue;

                string label = (tab.Label ?? "").Trim();
                if (!IsPageTab(label))
                    continue;
                if (tab.State == TabState.Selected)
                    return label;
                if (disabled.Length == 0 && tab.State == TabState.Disabled)
                    disabled = label;
            }

            return disabled;
        }

        // "Accept / Apply / Cancel" 与页签是同一个 `Tab` 类型，但它们是底部动作按钮，不是设置页。
        private static bool IsPageTab(string label)
            => label.Length != 0
                && !string.Equals(label, "Accept", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "Apply", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "Cancel", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "Resume", StringComparison.OrdinalIgnoreCase);

        private void TryAttach()
        {
            attachFramesLeft = AttachRetryFrames;
            try
            {
                Attach(attachScene);
                attachPending = false;
            }
            catch (Exception exception)
            {
                attachAttempts++;
                if (attachAttempts >= AttachRetryLimit)
                {
                    attachPending = false;
                    log.Warn($"[SMA-MENU] settings entry attach failed after {attachAttempts} attempts: {exception.Message}");
                    return;
                }

                // 只报第一次：IL2CPP 的泛型缓存没就绪属于可恢复情况，重复刷同一条没有意义。
                if (attachAttempts == 1)
                    log.Info($"[SMA-MENU] settings entry not ready yet ({exception.Message}); retrying");
            }
        }

        internal void SceneUnloaded(string sceneName)
        {
            if (!string.Equals(sceneName, "SettingsMenu", StringComparison.Ordinal))
                return;

            observedSettingsRoot = null;
            settingsTabs = Array.Empty<Tab>();
            observedActionButtons = null;
            settingsMenuActive = false;
            generalPageActive = false;
            settingsMenu = null;
            entryAlignmentReady = false;
            attachPending = false;
            attachAttempts = 0;
            attachFramesLeft = 0;
            SetEntryVisible(false);
        }

        internal void RefreshVisibility() => SetEntryVisible(IsEntryRequested());

        public void Dispose()
        {
            if (uiRoot != null)
            {
                UnityEngine.Object.Destroy(uiRoot);
                uiRoot = null;
            }

            uiCanvas = null;
            uiEntryObject = null;
            uiEntryRect = null;
            uiEntryImage = null;
            uiEntryButton = null;
            uiEntryOutline = null;
            uiEntryLabel = null;
            observedSettingsRoot = null;
            settingsTabs = Array.Empty<Tab>();
            observedActionButtons = null;
        }

        private void Attach(string sceneName)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                log.Warn("[SMA-MENU] SettingsMenu scene was not ready; the settings entry is unavailable.");
                return;
            }

            GameObject? settingsRoot = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root != null && string.Equals(root.name, "Settings Menu", StringComparison.Ordinal))
                {
                    settingsRoot = root;
                    break;
                }
            }

            if (settingsRoot == null)
            {
                log.Warn("[SMA-MENU] Settings Menu root was not found; the settings entry is unavailable.");
                return;
            }

            observedSettingsRoot = settingsRoot;
            if (settingsRoot.GetComponent<ModMenuSettingsWatcher>() == null)
                settingsRoot.AddComponent<ModMenuSettingsWatcher>();
            settingsMenuActive = settingsRoot.activeInHierarchy;
            settingsMenu = FindSettingsMenu(settingsRoot);
            activeMenuField = null;
            activeMenuFieldMissing = false;
            undetectablePageWarned = false;
            undetectableFrames = 0;

            AttachActionButtonsAlignment(settingsRoot);
            settingsTabs = Array.Empty<Tab>();
            RefreshGeneralPageState();

            EnsureEntryUi();
            // 入口对象刚建出来，必须再对齐一次：前面那次调用发生在建对象之前，只记下了原生按钮的位置，
            // 没有把 `entryAlignmentReady` 置起来——不补这一次，入口会因为"没对齐"被门禁永久隐藏。
            ApplyAlignment();
            RefreshVisibility();
            log.Info("[SMA-MENU] settings entry attached");
        }

        private void AttachActionButtonsAlignment(GameObject settingsRoot)
        {
            Transform? actionButtonsTransform = settingsRoot.transform.Find("Content/Action buttons");
            if (actionButtonsTransform == null)
            {
                entryAlignmentReady = false;
                log.Warn("[SMA-MENU] Settings Menu/Content/Action buttons was not found; the settings entry alignment falls back to the canvas origin.");
                return;
            }

            RectTransform? actionButtons = actionButtonsTransform.GetComponent<RectTransform>();
            if (actionButtons == null)
            {
                entryAlignmentReady = false;
                return;
            }

            observedActionButtons = actionButtons;
            if (actionButtonsTransform.GetComponent<ModMenuActionButtonsWatcher>() == null)
                actionButtonsTransform.gameObject.AddComponent<ModMenuActionButtonsWatcher>();
            ApplyAlignment();
        }

        internal void NotifySettingsMenuActivation(GameObject settingsRoot, bool active)
        {
            if (settingsRoot == null)
                return;
            observedSettingsRoot = settingsRoot;
            settingsMenuActive = active;
            RefreshVisibility();
        }

        internal void NotifyActionButtonsRectChanged(RectTransform actionButtons)
        {
            if (actionButtons == null)
                return;
            observedActionButtons = actionButtons;
            ApplyAlignment();
        }

        internal void NotifyActionButtonsDestroyed()
        {
            observedActionButtons = null;
            entryAlignmentReady = false;
        }

        // 当前状态是否要求入口可见（门禁逻辑在 `ModMenuEntryVisibility` 里，纯函数可离线断言）。
        private bool IsEntryRequested()
            => ModMenuEntryVisibility.ShouldShow(settingsMenuActive, generalPageActive, window.IsVisible);

        // 原生那排「Accept / Apply / Cancel」是 `Sprocket.UI.Tab : Selectable`。先按 `Selectable` 找
        // （能把 Tab/Button 都覆盖住），找不到再退到"第一个比整排窄的直接子物体"，最后才退回整排容器。
        private static Selectable? FindNativeSelectable(RectTransform row)
        {
            Selectable? selectable = row.GetComponentInChildren<Selectable>(true);
            if (selectable != null)
                return selectable;

            for (int index = 0; index < row.childCount; index++)
            {
                Transform child = row.GetChild(index);
                if (child == null)
                    continue;

                Selectable? candidate = child.GetComponent<Selectable>();
                if (candidate != null)
                    return candidate;
            }

            return null;
        }

        // 单个原生按钮的矩形；**找不到就返回 null**（原生按钮比入口晚实例化）。
        // 绝不退回整排容器：容器比一个按钮宽得多，照抄它会做出一个横跨半屏的入口。
        private static RectTransform? FindNativeButtonRect(RectTransform row)
        {
            Selectable? selectable = FindNativeSelectable(row);
            RectTransform? selectableRect = selectable == null ? null : selectable.GetComponent<RectTransform>();
            if (selectableRect != null && selectableRect.rect.width > 0f && selectableRect.rect.height > 0f)
                return selectableRect;

            float rowWidth = row.rect.width;
            for (int index = 0; index < row.childCount; index++)
            {
                Transform child = row.GetChild(index);
                if (child == null)
                    continue;

                RectTransform? childRect = child.GetComponent<RectTransform>();
                if (childRect == null || childRect.rect.width <= 0f || childRect.rect.height <= 0f)
                    continue;
                if (rowWidth <= 0f || childRect.rect.width < rowWidth * 0.9f)
                    return childRect;
            }

            return null;
        }

        private static Vector2 ToScreen(Camera? camera, RectTransform source, Vector2 localPoint)
            => RectTransformUtility.WorldToScreenPoint(camera, source.TransformPoint(new Vector3(localPoint.x, localPoint.y, 0f)));

        private static bool TryScreenToLocal(RectTransform rootRect, Vector2 screen, out Vector2 local)
            => RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, screen, null, out local);

        // 切页会把「Action buttons」整排换掉（旧的可能被销毁或停用），缓存会指向上一页那排，
        // 于是入口停在上一个页面的位置，直到某个事件把引用刷新。每帧确认一次引用是否仍然指向活着的对象。
        private void EnsureActionButtons()
        {
            if (observedActionButtons != null && observedActionButtons.gameObject.activeInHierarchy)
                return;
            if (observedSettingsRoot == null)
                return;

            Transform? row = observedSettingsRoot.transform.Find("Content/Action buttons");
            if (row == null || row.gameObject == null)
                return;

            AttachActionButtonsAlignment(observedSettingsRoot);
        }

        private void ApplyAlignment()
        {
            if (observedActionButtons == null || uiRoot == null || uiEntryRect == null)
                return;

            try
            {
                Canvas? sourceCanvas = observedActionButtons.GetComponentInParent<Canvas>();
                Camera? sourceCamera = sourceCanvas != null && sourceCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? sourceCanvas.worldCamera
                    : null;
                RectTransform? rootRect = uiRoot.GetComponent<RectTransform>();
                if (rootRect == null)
                {
                    entryAlignmentReady = false;
                    return;
                }

                // 横向对齐整排按钮的左边界；尺寸与底边取**单个原生按钮**的矩形——整排容器比一个按钮宽得多，
                // 照抄它会做出一个横跨半屏的入口。两个画布的缩放比例不同，只有换算到本画布的本地坐标，
                // 入口才会和原生按钮一样大。
                //
                // 原生那三个按钮是 `Sprocket.UI.Tab : Selectable`，**不是 `Button`**：
                // 按 `Button` 找只会一无所获，然后把整排容器当成一个按钮。
                RectTransform? sizeSource = FindNativeButtonRect(observedActionButtons);
                if (sizeSource == null)
                {
                    // 原生按钮还没实例化：保持未就绪，`Update` 会逐帧重试（就绪后立刻停）。
                    entryAlignmentReady = false;
                    return;
                }

                // 全部换算都在**屏幕像素**里做：入口画布与设置页画布的缩放不同，用像素就与缩放无关。
                // x 取原生按钮的左边；底边放在原生按钮**顶边上方 50px**。
                Vector2 nativeBottomLeft = ToScreen(sourceCamera, sizeSource, new Vector2(sizeSource.rect.xMin, sizeSource.rect.yMin));
                Vector2 nativeTopRight = ToScreen(sourceCamera, sizeSource, new Vector2(sizeSource.rect.xMax, sizeSource.rect.yMax));
                if (!TryScreenToLocal(rootRect, nativeBottomLeft, out Vector2 localBottomLeft)
                    || !TryScreenToLocal(rootRect, nativeTopRight, out Vector2 localTopRight)
                    || !TryScreenToLocal(rootRect, new Vector2(nativeBottomLeft.x, nativeTopRight.y + EntryGapAboveRow), out Vector2 localTarget))
                {
                    entryAlignmentReady = false;
                    log.Warn("[SMA-MENU] Failed to align the settings entry to the native settings UI.");
                    return;
                }

                float width = MathF.Abs(localTopRight.x - localBottomLeft.x);
                float height = MathF.Abs(localTopRight.y - localBottomLeft.y);
                Vector2 targetSize = new Vector2(width, height);
                Vector2 targetPosition = new Vector2(localTarget.x - rootRect.rect.xMin, localTarget.y - rootRect.rect.yMin);

                // 每帧都会走到这里：几何每帧更新（便宜且幂等），只有几何真的变了才重做字体
                // （开关自动缩放会触发 TMP 重算，不必每帧做）。入口保持纯色，不抄原生底图。
                bool geometryChanged = uiEntryRect.sizeDelta != targetSize || uiEntryRect.anchoredPosition != targetPosition;
                if (geometryChanged)
                {
                    uiEntryRect.sizeDelta = targetSize;
                    uiEntryRect.anchoredPosition = targetPosition;
                    float canvasScale = sizeSource.rect.width > 0f && width > 0f ? width / sizeSource.rect.width : 1f;
                    ApplyNativeLabelFit(canvasScale);
                }

                bool wasReady = entryAlignmentReady;
                entryAlignmentReady = true;
                if (!wasReady)
                {
                    // 对齐是逐帧重试出来的：这一帧才算就绪，必须自己把入口显示出来，
                    // 否则要等到下一次切页才有别的调用顺手刷新可见性（表现为"第一次打开设置看不到"）。
                    RefreshVisibility();
                }
            }
            catch (Exception exception)
            {
                entryAlignmentReady = false;
                log.Warn($"[SMA-MENU] Failed to align the settings entry: {exception.Message}");
            }
        }

        // 字号跟着按钮一起换算：两个字号的"像素大小"取决于各自画布的缩放，直接抄原生字号会大一大截。
        // 打开自动缩放，上限就是换算后的原生字号——标签比原生按钮的名字长时自动缩小，不会溢出。
        private float ApplyNativeLabelFit(float canvasScale)
        {
            if (uiEntryLabel == null)
                return 0f;

            Selectable? native = observedActionButtons == null ? null : FindNativeSelectable(observedActionButtons);
            Il2CppTMPro.TextMeshProUGUI? nativeLabel = native == null
                ? null
                : native.GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
            float maxFontSize = nativeLabel != null && nativeLabel.fontSize > 0f
                ? nativeLabel.fontSize * MathF.Max(canvasScale, 0.1f)
                : EntryFontSize;

            uiEntryLabel.enableAutoSizing = true;
            uiEntryLabel.fontSizeMin = MathF.Min(9f, maxFontSize);
            uiEntryLabel.fontSizeMax = maxFontSize;
            uiEntryLabel.ForceMeshUpdate();
            return maxFontSize;
        }

        // 原生矩形上的一个点 → 本画布的本地坐标（入口的画布与设置页的画布缩放比例不同）。
        private static bool TryLocalPoint(RectTransform rootRect, Camera? camera, RectTransform source, Vector2 point, out Vector2 local)
        {
            Vector3 world = source.TransformPoint(new Vector3(point.x, point.y, 0f));
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, world);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, screen, null, out local);
        }

        private void EnsureEntryUi()
        {
            if (uiCanvas != null)
                return;

            ModMenuStyle.EntryAppearance appearance = SampleNativeAppearance();

            GameObject root = new("Sprocket Mod API Mod Menu Entry");
            UnityEngine.Object.DontDestroyOnLoad(root);
            uiCanvas = root.AddComponent<Canvas>();
            uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiCanvas.overrideSorting = true;
            uiCanvas.sortingOrder = 500;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ModMenuLayout.CanvasWidth, ModMenuLayout.CanvasHeight);
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();

            uiEntryObject = CreateNativeButton(
                root.transform, "Mod Menu Entry", EntryLabel, EntryWidth, EntryHeight, appearance, OpenFromEntry);
            uiEntryRect = uiEntryObject.GetComponent<RectTransform>();
            uiEntryRect.anchorMin = Vector2.zero;
            uiEntryRect.anchorMax = Vector2.zero;
            uiEntryRect.pivot = Vector2.zero;
            uiEntryRect.anchoredPosition = new Vector2(0f, EntryBottom);
            uiEntryRect.sizeDelta = new Vector2(EntryWidth, EntryHeight);

            uiRoot = root;
            SetEntryVisible(false);
            log.Info($"[SMA-MENU] settings entry style source={(appearance.FromNative ? ModMenuStyle.SourceNative : ModMenuStyle.SourceFallback)}");
        }

        // 从「设置 → General」的原生按钮上采样观感，取不到就回落到已验收的原生灰。
        //
        // 采样的是**状态色**（ColorBlock），不是 `Image.color`——原生按钮的底图通常是白色，
        // 真正的灰在 ColorBlock 里；抄错这一层就会做出比原生暗一截的按钮。
        private ModMenuStyle.EntryAppearance SampleNativeAppearance()
        {
            try
            {
                if (observedActionButtons == null)
                    return ModMenuStyle.Fallback;

                Selectable? native = FindNativeSelectable(observedActionButtons);
                if (native == null)
                    return ModMenuStyle.Fallback;

                ColorBlock states = native.colors;
                Image? nativeImage = native.targetGraphic as Image;
                Il2CppTMPro.TextMeshProUGUI? nativeLabel =
                    native.GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
                Outline? nativeOutline = native.GetComponent<Outline>();

                var sample = new ModMenuStyle.EntryAppearance(
                    surface: Rgba(states.normalColor),
                    hover: Rgba(states.highlightedColor),
                    pressed: Rgba(states.pressedColor),
                    border: nativeOutline != null ? Rgba(nativeOutline.effectColor) : default,
                    text: nativeLabel != null ? Rgba(nativeLabel.color) : default,
                    fontSize: nativeLabel != null ? nativeLabel.fontSize : 0f,
                    fadeDuration: states.fadeDuration,
                    hasOutline: nativeOutline != null,
                    fromNative: true);

                if (nativeImage == null || !ModMenuStyle.Usable(sample.Surface))
                {
                    log.Warn("[SMA-MENU] the native settings button has no usable state colours; using the native grey fallback.");
                    return ModMenuStyle.Fallback;
                }

                return ModMenuStyle.Resolve(true, sample);
            }
            catch (Exception exception)
            {
                log.Warn($"[SMA-MENU] failed to sample the native settings button: {exception.Message}");
                return ModMenuStyle.Fallback;
            }
        }

        // Unity 颜色 → 纯数据颜色（观感层不依赖 UnityEngine，离线契约套件才能测它）。
        private static ModMenuStyle.Rgba Rgba(Color color) => new(color.r, color.g, color.b, color.a);

        private static Color Color(ModMenuStyle.Rgba color) => new(color.Red, color.Green, color.Blue, color.Alpha);

        private void OpenFromEntry()
        {
            log.Info("[SMA-MENU] settings entry clicked");
            window.Open();
            RefreshVisibility();
        }

        private void SetEntryVisible(bool visible)
        {
            if (uiRoot == null)
                return;

            bool shouldShow = ModMenuEntryVisibility.ShouldApply(visible, entryAlignmentReady);
            if (uiRoot.activeSelf != shouldShow)
                uiRoot.SetActive(shouldShow);
            if (uiEntryObject != null && uiEntryObject.activeSelf != shouldShow)
                uiEntryObject.SetActive(shouldShow);
        }

        private GameObject CreateNativeButton(
            Transform parent,
            string name,
            string label,
            float width,
            float height,
            ModMenuStyle.EntryAppearance appearance,
            Action onClick)
        {
            GameObject node = new(name);
            node.transform.SetParent(parent, false);
            node.AddComponent<RectTransform>();
            Image image = node.AddComponent<Image>();
            // 关键：底图留白，灰色只放在 ColorBlock 里。两边都塞灰色会被 UGUI 相乘成"灰的平方"，
            // 结果比原生按钮暗一大截（这就是"自绘感"的来源）。
            image.color = Color(ModMenuStyle.White);
            image.raycastTarget = true;

            Button button = node.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color(appearance.Surface);
            colors.highlightedColor = Color(appearance.Hover);
            colors.pressedColor = Color(appearance.Pressed);
            colors.selectedColor = Color(appearance.Surface);
            colors.disabledColor = Color(appearance.Pressed);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = appearance.FadeDuration;
            button.colors = colors;
            button.onClick.AddListener((UnityAction)onClick);

            Outline? outline = null;
            if (appearance.HasOutline)
            {
                outline = node.AddComponent<Outline>();
                outline.effectColor = Color(appearance.Border);
                outline.effectDistance = new Vector2(1f, -1f);
                outline.useGraphicAlpha = false;
            }

            LayoutElement element = node.AddComponent<LayoutElement>();
            element.minWidth = width;
            element.preferredWidth = width;
            element.minHeight = height;
            element.preferredHeight = height;

            GameObject label_node = new("Label");
            label_node.transform.SetParent(node.transform, false);
            label_node.AddComponent<RectTransform>();
            Il2CppTMPro.TextMeshProUGUI text = label_node.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
            text.text = label;
            text.fontSize = appearance.FontSize;
            text.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
            text.color = Color(appearance.Text);
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            RectTransform labelRect = text.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            uiEntryImage = image;
            uiEntryButton = button;
            uiEntryOutline = outline;
            uiEntryLabel = text;

            return node;
        }
    }

    public sealed class ModMenuSettingsWatcher : MonoBehaviour
    {
        private void OnEnable() => ModMenuModule.Entry?.NotifySettingsMenuActivation(gameObject, true);
        private void OnDisable() => ModMenuModule.Entry?.NotifySettingsMenuActivation(gameObject, false);
        private void OnDestroy() => ModMenuModule.Entry?.NotifySettingsMenuActivation(gameObject, false);
    }

    public sealed class ModMenuActionButtonsWatcher : MonoBehaviour
    {
        private void OnEnable() => Notify();
        private void OnRectTransformDimensionsChange() => Notify();
        private void OnTransformParentChanged() => Notify();
        private void OnDestroy() => ModMenuModule.Entry?.NotifyActionButtonsDestroyed();

        private void Notify()
        {
            RectTransform? rect = GetComponent<RectTransform>();
            if (rect != null)
                ModMenuModule.Entry?.NotifyActionButtonsRectChanged(rect);
        }
    }
}

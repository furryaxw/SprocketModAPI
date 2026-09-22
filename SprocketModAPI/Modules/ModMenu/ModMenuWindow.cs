using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SprocketModAPI
{
    // 游戏内 Mod 菜单窗口（自建 UGUI）。左侧是模组列表，右侧在「详情」与「配置页」之间切换。
    //
    // 说明：这里的 UGUI 原语从已验证的键位窗口复制（不共享内部实现，也不改动键位模块的
    // 控制器代码）；窗口本身尚未经过游戏内验收。所有 UI 文本保持 ASCII，避免依赖游戏 TMP 字体的
    // 中文字形。
    internal sealed partial class ModMenuWindow : IDisposable
    {
        // 与键位窗口同一套中性色（R=G=B）：这两块 UI 在同一个画面里并排出现，
        // 带蓝或带绿的灰会显得像另一个画风。
        private static readonly Color BackdropColor = Rgba(17, 17, 17, 0.82f);
        private static readonly Color WindowColor = Rgb(26, 26, 26);
        private static readonly Color SearchSurfaceColor = Rgb(13, 13, 13);
        private static readonly Color HeaderSurfaceColor = Rgb(22, 22, 22);
        private static readonly Color TableBackgroundColor = Rgb(26, 26, 26);
        private static readonly Color RowColor = Rgb(21, 21, 21);
        private static readonly Color RowBorderColor = Rgb(38, 38, 38);
        private static readonly Color SelectedRowColor = Rgb(52, 52, 52);
        private static readonly Color GroupColor = Rgb(30, 30, 30);
        private static readonly Color BorderColor = Rgb(51, 51, 51);
        private static readonly Color ButtonColor = Rgb(58, 58, 58);
        private static readonly Color ButtonHoverColor = Rgb(72, 72, 72);
        private static readonly Color ButtonPressedColor = Rgb(44, 44, 44);
        private static readonly Color ButtonDisabledColor = Rgb(38, 38, 38);
        private static readonly Color ButtonBorderColor = Rgb(90, 90, 90);
        // 强调色沿用键位编辑器的琥珀色：主按钮、当前操作的状态文字与选中行的左侧色条。
        private static readonly Color AccentColor = Rgb(194, 138, 56);
        private static readonly Color AccentHoverColor = Rgb(230, 171, 77);
        private static readonly Color AccentPressedColor = Rgb(140, 92, 31);
        // 「偏离初始值」的 RESET 用冷灰高亮，避免与表示主操作的琥珀色混淆。
        private static readonly Color AttentionColor = Rgb(60, 60, 60);
        private static readonly Color AttentionHoverColor = Rgb(72, 72, 72);
        private static readonly Color AttentionBorderColor = Rgb(96, 96, 96);
        private static readonly Color BoundTextColor = Rgb(204, 204, 204);
        private static readonly Color HeaderTextColor = Rgb(102, 102, 102);
        private static readonly Color RowTextColor = Rgb(221, 221, 221);
        private static readonly Color PlaceholderColor = Rgb(136, 136, 136);
        private static readonly Color ScrollbarColor = Rgb(68, 68, 68);

        private readonly IModMetadataService metadata;
        private readonly IModConfigService config;
        private readonly IInputService input;
        private readonly Action<string> warn;
        private readonly Action<string> error;
        private readonly Action<string> info;
        private readonly string modsDirectory;
        private readonly string pluginsDirectory;
        private readonly string userLibsDirectory;
        private readonly Action<bool>? visibilityChanged;

        private Canvas? uiCanvas;
        private GameObject? uiRoot;
        private GameObject? uiBackdrop;
        private GameObject? uiWindow;
        private TMP_InputField? uiSearchInput;
        private TextMeshProUGUI? uiStatus;
        private TextMeshProUGUI? uiHeaderSubtitle;
        private ScrollRect? uiListScroll;
        private RectTransform? uiListContent;
        private ScrollRect? uiDetailScroll;
        private RectTransform? uiDetailContent;
        private TextMeshProUGUI? uiDetailTitle;
        private TextMeshProUGUI? uiDetailSubtitle;
        private GameObject? uiDetailActions;
        private GameObject? uiConfigPopup;
        private RectTransform? uiConfigPopupContent;
        private readonly List<GameObject> uiConfigPopupObjects = new();
        private readonly List<GameObject> uiConfigPopupRows = new();
        private readonly Dictionary<string, object> configDraft = new(StringComparer.Ordinal);
        private IModConfigRegistration? configPopupRegistration;
        private GameObject? uiRestartPopup;
        private readonly List<GameObject> uiRestartPopupObjects = new();

        // 本次启动内的启停记录：只有偏离启动状态的模组才需要重启。
        private readonly ModMenuRestartTracker restartTracker = new();

        private readonly List<GameObject> uiListObjects = new();
        private readonly List<GameObject> uiDetailObjects = new();

        private IReadOnlyList<ModMenuRow> rows = Array.Empty<ModMenuRow>();
        private string search = "";
        private string selectedLocation = "";
        private bool showingConfig;
        private bool uiVisible;
        private bool guiFailed;
        private string lastConfigLogId = "";
        private string toggleActionId = "";
        private string statusMessage = "Open this window from Settings -> General (MODS, bottom left) or bind a hotkey in the keybindings window.";
        private IDisposable? inputBlock;

        internal ModMenuWindow(
            IModMetadataService metadata,
            IModConfigService config,
            IInputService input,
            string modsDirectory,
            string pluginsDirectory,
            string userLibsDirectory,
            Action<string> warn,
            Action<string> error,
            Action<bool>? visibilityChanged = null,
            Action<string>? info = null)
        {
            this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.modsDirectory = modsDirectory ?? "";
            this.pluginsDirectory = pluginsDirectory ?? "";
            this.userLibsDirectory = userLibsDirectory ?? "";
            this.warn = warn ?? throw new ArgumentNullException(nameof(warn));
            this.error = error ?? throw new ArgumentNullException(nameof(error));
            this.visibilityChanged = visibilityChanged;
            this.info = info ?? (_ => { });
            config.Changed += OnConfigChanged;
        }

        internal bool IsVisible => uiVisible;
        internal bool RestartPending => restartTracker.RestartPending;

        // 开关动作的稳定 ID（`ModId:ActionId`），由 `ModMenuModule` 在注册后回填。
        internal void SetToggleActionId(string actionId) => toggleActionId = actionId ?? "";

        internal void Open()
        {
            if (guiFailed || uiVisible)
                return;

            try
            {
                EnsureUi();
                uiRoot!.SetActive(true);
                uiBackdrop!.SetActive(true);
                uiWindow!.SetActive(true);
                uiVisible = true;
                AcquireInputBlock();
                RefreshRows();
                RebuildAll();
                visibilityChanged?.Invoke(true);
                info($"[SMA-MENU] open mods={rows.Count} disabled={rows.Count(row => row.IsDisabled)} config-pages={rows.Count(row => row.HasConfigPage)} missing-deps={rows.Count(row => row.MissingDependencies.Count != 0)}");
            }
            catch (Exception exception)
            {
                FailPermanently(exception);
            }
        }

        // 关窗前先确认重启：磁盘上的启停要到下次启动才生效，静默关窗会让人以为改动没生效。
        internal void Close()
        {
            if (!uiVisible)
                return;

            if (restartTracker.RestartPending)
            {
                ShowRestartPrompt();
                return;
            }

            CloseWindow();
        }

        private void CloseWindow()
        {
            if (!uiVisible)
                return;

            uiVisible = false;
            lastConfigLogId = "";
            ReleaseInputBlock();
            if (uiBackdrop != null) uiBackdrop.SetActive(false);
            if (uiWindow != null) uiWindow.SetActive(false);
            if (uiRoot != null) uiRoot.SetActive(false);
            uiSearchInput?.DeactivateInputField();
            visibilityChanged?.Invoke(false);
            info("[SMA-MENU] close");
        }

        internal void Toggle()
        {
            if (uiVisible)
                Close();
            else
                Open();
        }

        internal void Update()
        {
            // 窗口完全由事件驱动；唯一的逐帧工作是 Esc：先关弹窗，再关窗口（与原生窗口的习惯一致）。
            if (!uiVisible)
                return;

            try
            {
                UnityEngine.InputSystem.Keyboard? keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
                    return;

                if (uiRestartPopup != null)
                    CloseRestartPrompt();
                else
                    Close();
            }
            catch (Exception exception)
            {
                warn($"[SMA-MENU] escape handling failed: {exception.Message}");
            }
        }

        public void Dispose()
        {
            config.Changed -= OnConfigChanged;
            CloseWindow();
            ReleaseInputBlock();
            uiListObjects.Clear();
            uiDetailObjects.Clear();
            if (uiRoot != null)
            {
                UnityEngine.Object.Destroy(uiRoot);
                uiRoot = null;
            }

            uiCanvas = null;
            uiBackdrop = null;
            uiWindow = null;
            uiSearchInput = null;
            uiStatus = null;
            uiListScroll = null;
            uiListContent = null;
            uiDetailScroll = null;
            uiDetailContent = null;
            uiDetailTitle = null;
            uiDetailSubtitle = null;
            uiDetailActions = null;
            uiConfigPopupObjects.Clear();
            uiConfigPopupRows.Clear();
            uiConfigPopup = null;
            uiConfigPopupContent = null;
            configPopupRegistration = null;
            uiRestartPopupObjects.Clear();
            uiRestartPopup = null;
        }

        private void AcquireInputBlock()
        {
            if (inputBlock != null)
                return;
            try
            {
                // 自己的开关动作必须豁免输入锁，否则打开窗口后同一个键会被自己的锁压住，只能开不能关。
                inputBlock = input is InputService service
                    ? service.AcquireInputBlock(this, "mod menu", toggleActionId)
                    : input.AcquireInputBlock(this, "mod menu");
            }
            catch (Exception exception)
            {
                warn($"[SMA-MENU] input block unavailable: {exception.Message}");
            }
        }

        private void ReleaseInputBlock()
        {
            IDisposable? block = inputBlock;
            inputBlock = null;
            try
            {
                block?.Dispose();
            }
            catch (Exception exception)
            {
                warn($"[SMA-MENU] input block release failed: {exception.Message}");
            }
        }

        private void FailPermanently(Exception exception)
        {
            guiFailed = true;
            error($"[SMA-MENU] mod menu UI disabled after a failure: {exception}");
            try
            {
                if (uiRoot != null)
                    UnityEngine.Object.Destroy(uiRoot);
            }
            catch (Exception)
            {
                // 场景卸载可能已经销毁过对象，忽略。
            }

            uiRoot = null;
            CloseConfigPopup();
            uiCanvas = null;
            uiVisible = false;
            ReleaseInputBlock();
        }

        private void OnConfigChanged(ModConfigChangedEventArgs args)
        {
            if (uiVisible && showingConfig)
                MarkDetailDirty();
        }

        private void MarkDetailDirty() => RebuildDetail();

        private void SetStatus(string message)
        {
            statusMessage = message ?? "";
            if (uiStatus != null)
                uiStatus.text = restartTracker.RestartPending ? $"RESTART REQUIRED  -  {statusMessage}" : statusMessage;
        }

        private void EnsureUi()
        {
            if (uiCanvas != null)
                return;

            GameObject root = new("Sprocket Mod API Mod Menu");
            UnityEngine.Object.DontDestroyOnLoad(root);
            uiCanvas = root.AddComponent<Canvas>();
            uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiCanvas.overrideSorting = true;
            uiCanvas.sortingOrder = 520;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ModMenuLayout.CanvasWidth, ModMenuLayout.CanvasHeight);
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();

            if (EventSystem.current == null)
            {
                EventSystem eventSystem = root.AddComponent<EventSystem>();
                eventSystem.sendNavigationEvents = false;
                root.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            uiBackdrop = CreateImageNode(root.transform, "Modal Backdrop", BackdropColor, true);
            SetStretch(uiBackdrop.GetComponent<RectTransform>());

            uiWindow = CreateImageNode(root.transform, "Window", WindowColor, true);
            RectTransform windowRect = uiWindow.GetComponent<RectTransform>();
            windowRect.anchorMin = new Vector2(0.5f, 1f);
            windowRect.anchorMax = new Vector2(0.5f, 1f);
            windowRect.pivot = new Vector2(0.5f, 1f);
            windowRect.anchoredPosition = new Vector2(0f, -ModMenuLayout.WindowTop);
            windowRect.sizeDelta = new Vector2(ModMenuLayout.WindowWidth, ModMenuLayout.WindowHeight);
            AddBorder(uiWindow);

            VerticalLayoutGroup windowLayout = uiWindow.AddComponent<VerticalLayoutGroup>();
            windowLayout.padding = new RectOffset((int)ModMenuLayout.WindowPadding, (int)ModMenuLayout.WindowPadding, (int)ModMenuLayout.WindowPadding, (int)ModMenuLayout.WindowPadding);
            windowLayout.spacing = ModMenuLayout.SectionSpacing;
            windowLayout.childAlignment = TextAnchor.UpperCenter;
            windowLayout.childControlWidth = true;
            windowLayout.childControlHeight = true;
            windowLayout.childForceExpandWidth = false;
            windowLayout.childForceExpandHeight = false;

            CreateHeaderPanel(uiWindow.transform);
            CreateSearchPanel(uiWindow.transform);
            CreateSplitPanel(uiWindow.transform);
            CreateFooterPanel(uiWindow.transform);

            uiRoot = root;
            uiBackdrop.SetActive(false);
            uiWindow.SetActive(false);
            uiRoot.SetActive(false);
        }

        private void CreateHeaderPanel(Transform parent)
        {
            GameObject panel = CreateLayoutNode(parent, "Header Panel");
            SetPreferredSize(panel, ModMenuLayout.ContentWidth, ModMenuLayout.HeaderHeight);

            HorizontalLayoutGroup layout = panel.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = ModMenuLayout.SectionSpacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            TextMeshProUGUI title = CreateText(panel.transform, "Title", "MOD MENU", 24f, TextAlignmentOptions.Left, Color.white, false);
            SetPreferredSize(title.gameObject, ModMenuLayout.TitleWidth, ModMenuLayout.HeaderHeight);

            uiHeaderSubtitle = CreateText(panel.transform, "Subtitle", "MODS  /  METADATA  /  CONFIG", 12f, TextAlignmentOptions.Left, HeaderTextColor, false);
            SetFlexibleWidth(uiHeaderSubtitle.gameObject, 1f);
        }

        private void CreateSearchPanel(Transform parent)
        {
            GameObject panel = CreateLayoutNode(parent, "Search Panel");
            SetPreferredSize(panel, ModMenuLayout.ContentWidth, ModMenuLayout.SearchHeight);

            HorizontalLayoutGroup layout = panel.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = ModMenuLayout.SearchSpacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            uiSearchInput = CreateInputField(panel.transform, "Input Field", "SEARCH MODS...", "", 0f);
            SetFlexibleWidth(uiSearchInput.gameObject, 1f);
            uiSearchInput.onValueChanged.AddListener((UnityAction<string>)OnSearchChanged);

            CreateButton(panel.transform, "Search Button", "SEARCH", ModMenuLayout.SearchButtonWidth, ModMenuLayout.SearchHeight, 12f, false, BoundTextColor, false, ApplySearch, out _, out _);
        }

        private void CreateSplitPanel(Transform parent)
        {
            GameObject panel = CreateLayoutNode(parent, "Split Panel");
            SetPreferredSize(panel, ModMenuLayout.ContentWidth, ModMenuLayout.ContentHeight);

            HorizontalLayoutGroup layout = panel.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = ModMenuLayout.SplitSpacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            CreateListPanel(panel.transform);
            CreateDetailPanel(panel.transform);
        }

        private void CreateListPanel(Transform parent)
        {
            GameObject panel = CreateScrollArea(parent, "Mod List Panel", out ScrollRect scroll, out RectTransform content);
            SetPreferredSize(panel, ModMenuLayout.ListWidth, ModMenuLayout.ContentHeight);
            uiListScroll = scroll;
            uiListContent = content;
        }

        private void CreateDetailPanel(Transform parent)
        {
            GameObject panel = CreateLayoutNode(parent, "Detail Panel");
            SetFlexibleWidth(panel, 1f);
            SetPreferredHeight(panel, ModMenuLayout.ContentHeight);

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            GameObject header = CreateLayoutNode(panel.transform, "Detail Header");
            SetPreferredHeight(header, ModMenuLayout.DetailHeaderHeight);
            VerticalLayoutGroup headerLayout = header.AddComponent<VerticalLayoutGroup>();
            headerLayout.spacing = 2f;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = true;
            headerLayout.childForceExpandHeight = false;

            // 名称与操作同一行：标题占弹性宽度，把 Disable / Config 顶到右侧；副标题在下一行。
            GameObject titleRow = CreateLayoutNode(header.transform, "Detail Title Row");
            SetPreferredHeight(titleRow, ModMenuLayout.DetailActionsHeight);
            HorizontalLayoutGroup titleLayout = titleRow.AddComponent<HorizontalLayoutGroup>();
            titleLayout.spacing = 8f;
            titleLayout.childAlignment = TextAnchor.MiddleLeft;
            titleLayout.childControlWidth = true;
            titleLayout.childControlHeight = true;
            titleLayout.childForceExpandWidth = false;
            titleLayout.childForceExpandHeight = true;

            uiDetailTitle = CreateText(titleRow.transform, "Detail Title", "SELECT A MOD", 18f, TextAlignmentOptions.Left, Color.white, false);
            SetFlexibleWidth(uiDetailTitle.gameObject, 1f);
            uiDetailSubtitle = CreateText(header.transform, "Detail Subtitle", "", 12f, TextAlignmentOptions.Left, HeaderTextColor, false);

            uiDetailActions = CreateLayoutNode(titleRow.transform, "Detail Actions");
            SetPreferredHeight(uiDetailActions, ModMenuLayout.DetailActionsHeight);
            HorizontalLayoutGroup actionsLayout = uiDetailActions.AddComponent<HorizontalLayoutGroup>();
            actionsLayout.spacing = 8f;
            actionsLayout.childAlignment = TextAnchor.MiddleRight;
            actionsLayout.childControlWidth = true;
            actionsLayout.childControlHeight = true;
            actionsLayout.childForceExpandWidth = false;
            actionsLayout.childForceExpandHeight = true;

            GameObject bodyPanel = CreateScrollArea(panel.transform, "Detail Body", out ScrollRect bodyScroll, out RectTransform bodyContent);
            SetFlexibleHeight(bodyPanel, 1f);
            uiDetailScroll = bodyScroll;
            uiDetailContent = bodyContent;
        }

        private void CreateFooterPanel(Transform parent)
        {
            GameObject panel = CreateLayoutNode(parent, "Footer Panel");
            SetPreferredSize(panel, ModMenuLayout.ContentWidth, ModMenuLayout.FooterHeight);

            HorizontalLayoutGroup layout = panel.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = ModMenuLayout.FooterSpacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            uiStatus = CreateText(panel.transform, "Status Text", statusMessage, 12f, TextAlignmentOptions.Left, AccentColor, false);
            SetFlexibleWidth(uiStatus.gameObject, 1f);
            uiStatus.enableWordWrapping = false;

            CreateButton(panel.transform, "Refresh Button", "REFRESH", ModMenuLayout.RefreshWidth, ModMenuLayout.FooterHeight, 12f, false, BoundTextColor, false, RefreshAndRebuild, out _, out _);
            CreateButton(panel.transform, "Close Button", "CLOSE", ModMenuLayout.CloseWidth, ModMenuLayout.FooterHeight, 12f, true, Color.white, true, Close, out _, out _);
        }

        private void OnSearchChanged(string value)
        {
            search = value ?? "";
            RebuildList();
        }

        private void ApplySearch()
        {
            if (uiSearchInput != null)
                search = uiSearchInput.text ?? "";
            RebuildList();
        }

        private void RefreshAndRebuild()
        {
            SetStatus("Refreshed.");
            RefreshRows();
            RebuildAll();
        }

        private void RefreshRows()
        {
            try
            {
                metadata.Refresh();
            }
            catch (Exception exception)
            {
                warn($"[SMA-MENU] metadata refresh failed: {exception.Message}");
            }

            var disabledPaths = new List<string>();
            disabledPaths.AddRange(ModFileToggle.FindDisabledDlls(modsDirectory));
            disabledPaths.AddRange(ModFileToggle.FindDisabledDlls(pluginsDirectory));

            IReadOnlyCollection<string> configIds;
            try
            {
                configIds = config.Snapshots.Select(page => page.ModId).ToArray();
            }
            catch (Exception exception)
            {
                warn($"[SMA-MENU] config snapshot unavailable: {exception.Message}");
                configIds = Array.Empty<string>();
            }

            rows = ModMenuListModel.Build(metadata.Entries, disabledPaths, configIds,
                ModAssemblyIndex.Collect(modsDirectory, pluginsDirectory, userLibsDirectory));
            if (rows.Count != 0 && !rows.Any(row => string.Equals(row.Location, selectedLocation, StringComparison.OrdinalIgnoreCase)))
                selectedLocation = "";
        }

        private ModMenuRow? SelectedRow
            => rows.FirstOrDefault(row => string.Equals(row.Location, selectedLocation, StringComparison.OrdinalIgnoreCase));

        private void RebuildAll()
        {
            UpdateHeaderSummary();
            RebuildList();
            RebuildDetail();
        }

        // 标题右侧的概览：一眼看出规模与问题数，而不是只有一句静态副标题。
        private void UpdateHeaderSummary()
        {
            if (uiHeaderSubtitle == null)
                return;

            int disabled = rows.Count(row => row.IsDisabled);
            int configPages = rows.Count(row => row.HasConfigPage);
            int missing = rows.Count(row => row.MissingDependencies.Count != 0);
            int conflicts = rows.Count(row => row.HasIncompatiblePresent);

            var builder = new System.Text.StringBuilder();
            builder.Append(rows.Count).Append(rows.Count == 1 ? " MOD" : " MODS");
            builder.Append("   /   ").Append(configPages).Append(" CONFIG");
            if (disabled != 0)
                builder.Append("   /   ").Append(disabled).Append(" DISABLED");
            if (missing != 0)
                builder.Append("   /   ").Append(missing).Append(" MISSING DEPS");
            if (conflicts != 0)
                builder.Append("   /   ").Append(conflicts).Append(" CONFLICT");
            uiHeaderSubtitle.text = builder.ToString();
        }

        private void ClearObjects(List<GameObject> objects)
        {
            foreach (GameObject item in objects)
            {
                if (item == null)
                    continue;
                item.SetActive(false);
                UnityEngine.Object.Destroy(item);
            }

            objects.Clear();
        }
    }
}

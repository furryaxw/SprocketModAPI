using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace SprocketModAPI
{
    // The modal uses UGUI so its full-screen raycast surface prevents pointer
    // events from reaching the native settings page underneath it.
    internal sealed partial class InputService
    {
        private const float CanvasWidth = 1122.519685f;
        private const float CanvasHeight = 793.700787f;
        private const float WindowWidth = 971.338583f;
        private const float WindowHeight = 583.191406f;
        private const float WindowTop = 56.692913f;
        private const float WindowPadding = 24f;
        private const float SectionSpacing = 24f;
        private const float ContentWidth = 921.338583f;
        private const float HeaderHeight = 33.688477f;
        private const float TitleWidth = 236.617188f;
        private const float SearchHeight = 40.159180f;
        private const float SearchSpacing = 12f;
        private const float SearchButtonWidth = 71.851562f;
        private const float MainContentHeight = 350f;
        private const float HeaderRowHeight = 30f;
        private const float ScrollbarWidth = 8f;
        private const float ModHeaderHeight = 30f;
        private const float ActionRowHeight = 40f;
        private const float ModSeparatorHeight = 2f;
        private const int RowHorizontalPadding = 16;
        private const int RowVerticalPadding = 5;
        private const float BindingColumnWidth = (ContentWidth - 2f - ScrollbarWidth - (RowHorizontalPadding * 2f)) / 5f;
        private const float ResetColumnWidth = BindingColumnWidth / 4f;
        private const int PrimaryContentPadding = 80;
        private const float BindingButtonWidth = 104f;
        private const float RowButtonHeight = (30.343750f / 2f) * 1.5f;
        private const float RowResetWidth = 35.406250f;
        private const float FooterHeight = 38.343750f;
        private const float FooterSpacing = 16f;
        private const float ResetAllWidth = 107.431641f;
        private const float CloseWidth = 101.067383f;
        private const float EntryWidth = 190f;
        private const float EntryHeight = 42f;
        private const float EntryBottom = 84f;

        private static readonly Color BackdropColor = Rgba(17, 17, 17, 0.82f);
        private static readonly Color WindowColor = Rgb(26, 26, 26);
        private static readonly Color SearchSurfaceColor = Rgb(13, 13, 13);
        private static readonly Color HeaderSurfaceColor = Rgb(22, 22, 22);
        private static readonly Color TableBackgroundColor = Rgb(26, 26, 26);
        private static readonly Color KeyRowColor = Rgb(21, 21, 21);
        private static readonly Color GroupColor = Rgb(30, 30, 30);
        private static readonly Color BorderColor = Rgb(51, 51, 51);
        private static readonly Color ButtonColor = new(0.17f, 0.18f, 0.19f, 1f);
        private static readonly Color ButtonHoverColor = new(0.25f, 0.26f, 0.27f, 1f);
        private static readonly Color ButtonPressedColor = new(0.1f, 0.105f, 0.11f, 1f);
        private static readonly Color ButtonDisabledColor = new(0.11f, 0.115f, 0.12f, 0.65f);
        private static readonly Color ButtonBorderColor = new(0.38f, 0.4f, 0.41f, 0.9f);
        private static readonly Color AccentColor = new(0.76f, 0.54f, 0.22f, 1f);
        private static readonly Color AccentHoverColor = new(0.9f, 0.67f, 0.3f, 1f);
        private static readonly Color AccentPressedColor = new(0.55f, 0.36f, 0.12f, 1f);
        private static readonly Color BoundTextColor = Rgb(204, 204, 204);
        private static readonly Color UnboundTextColor = Rgb(85, 85, 85);
        private static readonly Color HeaderTextColor = Rgb(102, 102, 102);
        private static readonly Color RowTextColor = Rgb(221, 221, 221);
        private static readonly Color PlaceholderColor = Rgb(136, 136, 136);
        private static readonly Color ScrollbarColor = Rgb(68, 68, 68);
        private static readonly Color ResetIconColor = Rgb(119, 119, 119);

        private Canvas? uiCanvas;
        private GameObject? uiRoot;
        private GameObject? uiEntryObject;
        private RectTransform? uiEntryRect;
        private GameObject? uiBackdrop;
        private GameObject? uiWindow;
        private TMP_InputField? uiSearchInput;
        private TextMeshProUGUI? uiStatus;
        private ScrollRect? uiScroll;
        private GameObject? uiContentObject;
        private readonly List<GameObject> uiRowObjects = new();
        private bool uiVisible;
        private bool guiFailed;
        private bool uiDirty = true;
        private bool keymappingPageActive;
        private string search = "";
        private ActionState? captureAction;
        private int captureSlot;
        private int captureStartedFrame;
        private ModifierKeys captureModifiers;
        private string? modifierOnlyPath;

        internal void UpdateUi()
        {
            if (guiFailed)
                return;

            try
            {
                if (!keymappingPageActive)
                {
                    if (uiRoot != null)
                        uiRoot.SetActive(false);
                    uiVisible = false;
                    CancelCapture();
                    return;
                }

                EnsureUi();
                if (uiDirty)
                    RefreshUiText();

                uiRoot!.SetActive(true);
                uiEntryObject!.SetActive(!uiVisible && entryAlignmentReady);
                uiBackdrop!.SetActive(uiVisible);
                uiWindow!.SetActive(uiVisible);
            }
            catch (Exception exception)
            {
                guiFailed = true;
                uiVisible = false;
                CancelCapture();
                if (uiRoot != null)
                    uiRoot.SetActive(false);
                error($"[SMA] keybinding UI disabled after compatibility error: {exception}");
            }
        }

        private void EnsureUi()
        {
            if (uiCanvas != null)
                return;

            GameObject root = new("SprocketModAPI UI");
            UnityEngine.Object.DontDestroyOnLoad(root);
            uiCanvas = root.AddComponent<Canvas>();
            uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiCanvas.overrideSorting = true;
            uiCanvas.sortingOrder = 500;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(CanvasWidth, CanvasHeight);
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();

            if (EventSystem.current == null)
            {
                EventSystem eventSystem = root.AddComponent<EventSystem>();
                eventSystem.sendNavigationEvents = false;
                root.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            CreateButton(root.transform, "Mod Keybindings Entry", "MOD KEYBINDINGS", EntryWidth, EntryHeight, 12f, true, Color.white, true, OpenWindow, out uiEntryObject, out _);
            uiEntryRect = uiEntryObject.GetComponent<RectTransform>();
            uiEntryRect.anchorMin = Vector2.zero;
            uiEntryRect.anchorMax = Vector2.zero;
            uiEntryRect.pivot = Vector2.zero;
            uiEntryRect.anchoredPosition = new Vector2(0f, EntryBottom);
            uiEntryRect.sizeDelta = new Vector2(EntryWidth, EntryHeight);

            uiBackdrop = CreateImageNode(root.transform, "Modal Backdrop", BackdropColor, true);
            SetStretch(uiBackdrop.GetComponent<RectTransform>());

            uiWindow = CreateImageNode(root.transform, "Window", WindowColor, true);
            RectTransform windowRect = uiWindow.GetComponent<RectTransform>();
            windowRect.anchorMin = new Vector2(0.5f, 1f);
            windowRect.anchorMax = new Vector2(0.5f, 1f);
            windowRect.pivot = new Vector2(0.5f, 1f);
            windowRect.anchoredPosition = new Vector2(0f, -WindowTop);
            windowRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            AddBorder(uiWindow);

            VerticalLayoutGroup windowLayout = uiWindow.AddComponent<VerticalLayoutGroup>();
            windowLayout.padding = new RectOffset((int)WindowPadding, (int)WindowPadding, (int)WindowPadding, (int)WindowPadding);
            windowLayout.spacing = SectionSpacing;
            windowLayout.childAlignment = TextAnchor.UpperCenter;
            windowLayout.childControlWidth = true;
            windowLayout.childControlHeight = true;
            windowLayout.childForceExpandWidth = false;
            windowLayout.childForceExpandHeight = false;

            CreateHeaderPanel(uiWindow.transform);
            CreateSearchPanel(uiWindow.transform);
            CreateMainContentPanel(uiWindow.transform);
            CreateFooterPanel(uiWindow.transform);

            uiRoot = root;
            ApplyObservedActionButtonsAlignment();
            uiBackdrop.SetActive(false);
            uiWindow.SetActive(false);
            uiRoot.SetActive(false);

        }

        private void CreateHeaderPanel(Transform parent)
        {
            GameObject panel = CreateLayoutNode(parent, "Header Panel");
            SetPreferredSize(panel, ContentWidth, HeaderHeight);

            HorizontalLayoutGroup layout = panel.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = SectionSpacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            TextMeshProUGUI title = CreateText(panel.transform, "Title", "MOD KEYBINDINGS", 24f, TextAlignmentOptions.Left, Color.white, false);
            SetPreferredSize(title.gameObject, TitleWidth, HeaderHeight);

            TextMeshProUGUI subtitle = CreateText(panel.transform, "Subtitle", "UNIFIED INPUT SETTINGS", 12f, TextAlignmentOptions.Left, HeaderTextColor, false);
            SetFlexibleWidth(subtitle.gameObject, 1f);
        }

        private void CreateSearchPanel(Transform parent)
        {
            GameObject panel = CreateLayoutNode(parent, "Search Panel");
            SetPreferredSize(panel, ContentWidth, SearchHeight);

            HorizontalLayoutGroup layout = panel.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = SearchSpacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            uiSearchInput = CreateSearchInput(panel.transform);
            SetFlexibleWidth(uiSearchInput.gameObject, 1f);
            uiSearchInput.onValueChanged.AddListener((UnityAction<string>)OnSearchChanged);

            CreateButton(panel.transform, "Search Button", "SEARCH", SearchButtonWidth, SearchHeight, 12f, false, BoundTextColor, false, ApplySearch, out _, out _);
        }

        private void CreateMainContentPanel(Transform parent)
        {
            GameObject panel = CreateImageNode(parent, "Main Content Panel", TableBackgroundColor, true);
            SetPreferredSize(panel, ContentWidth, MainContentHeight);
            AddBorder(panel);

            uiScroll = panel.AddComponent<ScrollRect>();
            uiScroll.horizontal = false;
            uiScroll.vertical = true;
            uiScroll.movementType = ScrollRect.MovementType.Clamped;
            uiScroll.scrollSensitivity = 28f;

            GameObject header = CreateImageNode(panel.transform, "Header Row", HeaderSurfaceColor, false);
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(1f, -(1f + HeaderRowHeight));
            headerRect.offsetMax = new Vector2(-(1f + ScrollbarWidth), -1f);

            HorizontalLayoutGroup headerLayout = header.AddComponent<HorizontalLayoutGroup>();
            headerLayout.padding = new RectOffset(RowHorizontalPadding, RowHorizontalPadding, 0, 0);
            headerLayout.spacing = 0f;
            headerLayout.childAlignment = TextAnchor.MiddleLeft;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = true;

            TextMeshProUGUI actionLabel = AddHeaderLabel(header.transform, "Action Label", "ACTION", TextAlignmentOptions.Left);
            SetFlexibleWidth(actionLabel.gameObject, 1f);
            TextMeshProUGUI primaryLabel = AddHeaderLabel(header.transform, "Primary Label", "PRIMARY", TextAlignmentOptions.Center);
            primaryLabel.margin = new Vector4(PrimaryContentPadding, 0f, 0f, 0f);
            SetPreferredWidth(primaryLabel.gameObject, BindingColumnWidth);
            TextMeshProUGUI secondaryLabel = AddHeaderLabel(header.transform, "Secondary Label", "SECONDARY", TextAlignmentOptions.Center);
            SetPreferredWidth(secondaryLabel.gameObject, BindingColumnWidth);
            TextMeshProUGUI resetLabel = AddHeaderLabel(header.transform, "Reset Label", "RESET", TextAlignmentOptions.Center);
            SetPreferredWidth(resetLabel.gameObject, ResetColumnWidth);

            GameObject viewport = CreateImageNode(panel.transform, "Scroll View Viewport", TableBackgroundColor, true);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(1f, 1f);
            viewportRect.offsetMax = new Vector2(-(1f + ScrollbarWidth), -(1f + HeaderRowHeight));
            Mask mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            uiContentObject = CreateLayoutNode(viewport.transform, "Content");
            RectTransform contentRect = uiContentObject.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            VerticalLayoutGroup contentLayout = uiContentObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 0f;
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            ContentSizeFitter fitter = uiContentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Scrollbar scrollbar = CreateVerticalScrollbar(panel.transform);
            uiScroll.viewport = viewportRect;
            uiScroll.content = contentRect;
            uiScroll.verticalScrollbar = scrollbar;
            uiScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            uiScroll.verticalScrollbarSpacing = 0f;
        }

        private void CreateFooterPanel(Transform parent)
        {
            GameObject panel = CreateLayoutNode(parent, "Footer Panel");
            SetPreferredSize(panel, ContentWidth, FooterHeight);

            HorizontalLayoutGroup layout = panel.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = FooterSpacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            uiStatus = CreateText(panel.transform, "Status Text", "CHANGES SAVE AUTOMATICALLY", 12f, TextAlignmentOptions.Left, HeaderTextColor, false);

            GameObject spacer = CreateLayoutNode(panel.transform, "Spacer");
            SetFlexibleWidth(spacer, 1f);

            CreateButton(panel.transform, "Reset All Button", "RESET ALL", ResetAllWidth, FooterHeight, 12f, false, BoundTextColor, false, ResetAllBindings, out _, out _);
            CreateButton(panel.transform, "Close Button", "CLOSE", CloseWidth, FooterHeight, 12f, true, Color.white, true, CloseWindow, out _, out _);
        }

        private void RefreshUiText()
        {
            if (uiWindow == null || uiContentObject == null || uiScroll == null || uiStatus == null)
                throw new InvalidOperationException("Keybinding UI references are unavailable.");

            float previousScroll = uiScroll.verticalNormalizedPosition;
            foreach (GameObject rowObject in uiRowObjects)
            {
                if (rowObject == null)
                    continue;
                rowObject.SetActive(false);
                UnityEngine.Object.Destroy(rowObject);
            }
            uiRowObjects.Clear();

            ActionState[] list = actions.Values
                .Where(action => string.IsNullOrWhiteSpace(search)
                    || action.Definition.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || action.Definition.ModId.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(action => action.Definition.ModId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(action => action.Definition.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            string? currentMod = null;
            if (list.Length != 0)
                uiRowObjects.Add(CreateModSeparator(uiContentObject.transform));
            foreach (ActionState state in list)
            {
                if (!string.Equals(currentMod, state.Definition.ModId, StringComparison.OrdinalIgnoreCase))
                {
                    if (currentMod != null)
                        uiRowObjects.Add(CreateModSeparator(uiContentObject.transform));
                    currentMod = state.Definition.ModId;
                    uiRowObjects.Add(CreateModHeader(uiContentObject.transform, currentMod));
                }

                uiRowObjects.Add(CreateActionRow(uiContentObject.transform, state));
            }

            if (captureAction != null)
            {
                uiStatus.text = "LISTENING FOR INPUT | ESC TO UNBIND";
                uiStatus.color = AccentColor;
            }
            else
            {
                uiStatus.text = "CHANGES SAVE AUTOMATICALLY";
                uiStatus.color = HeaderTextColor;
            }

            RectTransform contentRect = uiContentObject.GetComponent<RectTransform>();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            Canvas.ForceUpdateCanvases();
            uiScroll.verticalNormalizedPosition = list.Length == 0 ? 1f : previousScroll;
            uiDirty = false;

        }

        private static GameObject CreateModSeparator(Transform parent)
        {
            GameObject separator = CreateImageNode(parent, "Mod Separator", ButtonBorderColor, false);
            SetPreferredHeight(separator, ModSeparatorHeight);
            return separator;
        }

        private GameObject CreateModHeader(Transform parent, string modId)
        {
            GameObject row = CreateImageNode(parent, "Mod", GroupColor, true);
            SetPreferredHeight(row, ModHeaderHeight);
            AddBorder(row);

            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 0, 0);
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            TextMeshProUGUI label = CreateText(row.transform, "Mod Label", "MOD", 12f, TextAlignmentOptions.Left, AccentColor, true);
            SetPreferredWidth(label.gameObject, 30f);
            TextMeshProUGUI name = CreateText(row.transform, "Mod Name", DisplayModName(modId), 14f, TextAlignmentOptions.Left, BoundTextColor, false);
            SetFlexibleWidth(name.gameObject, 1f);
            return row;
        }

        private GameObject CreateActionRow(Transform parent, ActionState state)
        {
            GameObject row = CreateImageNode(parent, "Key Bind", KeyRowColor, true);
            SetPreferredHeight(row, ActionRowHeight);
            AddBorder(row);

            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(RowHorizontalPadding, RowHorizontalPadding, RowVerticalPadding, RowVerticalPadding);
            layout.spacing = 0f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            TextMeshProUGUI action = CreateText(row.transform, "Action", state.Definition.DisplayName, 14f, TextAlignmentOptions.Left, RowTextColor, false);
            SetFlexibleWidth(action.gameObject, 1f);

            GameObject primaryCell = CreateColumnCell(row.transform, "Primary", BindingColumnWidth, PrimaryContentPadding);
            bool capturingPrimary = captureAction == state && captureSlot == 0;
            string primaryLabel = capturingPrimary ? "PRESS A KEY..." : DisplayBinding(state.Primary);
            Color primaryText = capturingPrimary ? Color.white : state.Primary.IsEmpty ? UnboundTextColor : BoundTextColor;
            CreateButton(primaryCell.transform, "Primary Binding", primaryLabel, BindingButtonWidth, RowButtonHeight, 12f, capturingPrimary, primaryText, false, () => BeginCapture(state, 0), out _, out _);

            GameObject secondaryCell = CreateColumnCell(row.transform, "Secondary", BindingColumnWidth);
            bool capturingSecondary = captureAction == state && captureSlot == 1;
            string secondaryLabel = capturingSecondary ? "PRESS A KEY..." : DisplayBinding(state.Secondary);
            Color secondaryText = capturingSecondary ? Color.white : state.Secondary.IsEmpty ? UnboundTextColor : BoundTextColor;
            CreateButton(secondaryCell.transform, "Secondary Binding", secondaryLabel, BindingButtonWidth, RowButtonHeight, 12f, capturingSecondary, secondaryText, false, () => BeginCapture(state, 1), out _, out _);

            GameObject resetCell = CreateColumnCell(row.transform, "Reset", ResetColumnWidth);
            CreateButton(resetCell.transform, "Reset Binding", "", RowResetWidth, RowButtonHeight, 12f, false, ResetIconColor, false, () =>
            {
                state.RestoreDefaults();
                uiDirty = true;
            }, out GameObject resetButton, out _);
            CreateResetIcon(resetButton.transform);

            return row;
        }

        private static GameObject CreateColumnCell(Transform parent, string name, float width, int leftPadding = 0)
        {
            GameObject cell = CreateLayoutNode(parent, name);
            SetPreferredWidth(cell, width);
            HorizontalLayoutGroup layout = cell.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(leftPadding, 0, 0, 0);
            layout.spacing = 0f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return cell;
        }

        private void OpenWindow()
        {
            uiVisible = true;
            uiDirty = true;
        }

        private void CloseWindow()
        {
            uiVisible = false;
            uiSearchInput?.DeactivateInputField();
            CancelCapture();
        }

        private void OnSearchChanged(string value)
        {
            search = value ?? "";
            uiDirty = true;
        }

        private void ApplySearch()
        {
            if (uiSearchInput != null)
                search = uiSearchInput.text ?? "";
            uiDirty = true;
        }

        private void ResetAllBindings()
        {
            foreach (ActionState state in actions.Values)
                state.RestoreDefaults();
            uiDirty = true;
        }

        private void BeginCapture(ActionState state, int slot)
        {
            captureAction = state;
            captureSlot = slot;
            captureStartedFrame = Time.frameCount;
            captureModifiers = ModifierKeys.None;
            modifierOnlyPath = null;
            uiSearchInput?.DeactivateInputField();
            uiDirty = true;
        }

        private void CancelCapture()
        {
            bool wasCapturing = captureAction != null;
            captureAction = null;
            captureModifiers = ModifierKeys.None;
            modifierOnlyPath = null;
            if (wasCapturing)
                uiDirty = true;
        }

        private void UpdateCapture()
        {
            if (!uiVisible || captureAction == null)
                return;
            if (Time.frameCount <= captureStartedFrame + 1)
                return;

            Keyboard? keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    captureAction.SetBinding(captureSlot, null);
                    uiDirty = true;
                    CancelCapture();
                    return;
                }

                captureModifiers = CaptureModifierFlags(keyboard);
                var keys = keyboard.allKeys;
                if (keys != null)
                {
                    foreach (KeyControl key in keys)
                    {
                        if (key == null || !key.wasPressedThisFrame)
                            continue;
                        string? name = key.name;
                        if (string.IsNullOrEmpty(name))
                            continue;
                        string path = $"<Keyboard>/{name}";
                        if (IsModifier(path))
                        {
                            modifierOnlyPath = path;
                            continue;
                        }

                        captureAction.SetBinding(captureSlot, new KeyChord(path, captureModifiers));
                        uiDirty = true;
                        CancelCapture();
                        return;
                    }
                }

                if (modifierOnlyPath != null && CaptureModifierFlags(keyboard) == ModifierKeys.None)
                {
                    captureAction.SetBinding(captureSlot, new KeyChord(modifierOnlyPath));
                    uiDirty = true;
                    CancelCapture();
                    return;
                }
            }

            Mouse? mouse = Mouse.current;
            if (mouse == null)
                return;
            if (mouse.leftButton.wasPressedThisFrame)
                CommitMouse("leftButton");
            else if (mouse.rightButton.wasPressedThisFrame)
                CommitMouse("rightButton");
            else if (mouse.middleButton.wasPressedThisFrame)
                CommitMouse("middleButton");
            else if (mouse.forwardButton.wasPressedThisFrame)
                CommitMouse("forwardButton");
            else if (mouse.backButton.wasPressedThisFrame)
                CommitMouse("backButton");
        }

        private void CommitMouse(string name)
        {
            captureAction!.SetBinding(captureSlot, new KeyChord($"<Mouse>/{name}", captureModifiers));
            uiDirty = true;
            CancelCapture();
        }

        private static bool IsModifier(string path)
            => path.EndsWith("shift", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("ctrl", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("alt", StringComparison.OrdinalIgnoreCase);

        private static string DisplayBinding(KeyChord chord)
        {
            if (chord.IsEmpty)
                return "UNBOUND";

            string path = chord.ControlPath;
            int slash = path.LastIndexOf('/');
            string key = slash >= 0 ? path.Substring(slash + 1) : path;
            key = key switch
            {
                "leftctrl" => "Left Ctrl",
                "rightctrl" => "Right Ctrl",
                "leftshift" => "Left Shift",
                "rightshift" => "Right Shift",
                "leftalt" => "Left Alt",
                "rightalt" => "Right Alt",
                "leftbutton" => "Mouse Left",
                "rightbutton" => "Mouse Right",
                "middlebutton" => "Mouse Middle",
                _ => key.Length == 1 ? key.ToUpperInvariant() : key
            };
            return chord.Modifiers == ModifierKeys.None ? key : $"{chord.Modifiers}: {key}";
        }

        private static string DisplayModName(string modId)
        {
            string value = modId.StartsWith("sprocket-", StringComparison.OrdinalIgnoreCase) ? modId.Substring(9) : modId;
            return string.Join(" ", value.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
        }

        private static ModifierKeys CaptureModifierFlags(Keyboard keyboard)
        {
            ModifierKeys modifiers = ModifierKeys.None;
            if (keyboard.leftShiftKey.isPressed) modifiers |= ModifierKeys.LeftShift;
            if (keyboard.rightShiftKey.isPressed) modifiers |= ModifierKeys.RightShift;
            if (keyboard.leftCtrlKey.isPressed) modifiers |= ModifierKeys.LeftCtrl;
            if (keyboard.rightCtrlKey.isPressed) modifiers |= ModifierKeys.RightCtrl;
            if (keyboard.leftAltKey.isPressed) modifiers |= ModifierKeys.LeftAlt;
            if (keyboard.rightAltKey.isPressed) modifiers |= ModifierKeys.RightAlt;
            return modifiers;
        }

        private static TMP_InputField CreateSearchInput(Transform parent)
        {
            GameObject field = CreateImageNode(parent, "Input Field", SearchSurfaceColor, true);
            AddBorder(field);

            GameObject textArea = CreateLayoutNode(field.transform, "Text Area");
            RectTransform textAreaRect = textArea.GetComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(16f, 0f);
            textAreaRect.offsetMax = new Vector2(-16f, 0f);
            textArea.AddComponent<RectMask2D>();

            TextMeshProUGUI placeholder = CreateText(textArea.transform, "Placeholder", "SEARCH KEYBINDINGS...", 12f, TextAlignmentOptions.Left, PlaceholderColor, false);
            SetStretch(placeholder.GetComponent<RectTransform>());

            TextMeshProUGUI text = CreateText(textArea.transform, "Text", "", 14f, TextAlignmentOptions.Left, BoundTextColor, false);
            SetStretch(text.GetComponent<RectTransform>());
            text.raycastTarget = true;

            TMP_InputField input = field.AddComponent<TMP_InputField>();
            input.textViewport = textAreaRect;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.targetGraphic = field.GetComponent<Image>();
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.characterLimit = 64;
            input.text = "";
            return input;
        }

        private static Scrollbar CreateVerticalScrollbar(Transform parent)
        {
            GameObject track = CreateImageNode(parent, "Vertical Scrollbar", WindowColor, true);
            RectTransform trackRect = track.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(1f, 0f);
            trackRect.anchorMax = new Vector2(1f, 1f);
            trackRect.pivot = new Vector2(1f, 0.5f);
            trackRect.anchoredPosition = new Vector2(-1f, 0f);
            trackRect.sizeDelta = new Vector2(ScrollbarWidth, -4f);

            GameObject slidingArea = CreateLayoutNode(track.transform, "Sliding Area");
            SetStretch(slidingArea.GetComponent<RectTransform>());
            GameObject handle = CreateImageNode(slidingArea.transform, "Handle", ScrollbarColor, true);
            SetStretch(handle.GetComponent<RectTransform>());

            Scrollbar scrollbar = track.AddComponent<Scrollbar>();
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.handleRect = handle.GetComponent<RectTransform>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            return scrollbar;
        }

        private static TextMeshProUGUI AddHeaderLabel(Transform parent, string name, string value, TextAlignmentOptions alignment)
        {
            return CreateText(parent, name, value, 12f, alignment, BoundTextColor, false);
        }

        private static void CreateResetIcon(Transform parent)
        {
            GameObject icon = CreateLayoutNode(parent, "Reset Icon");
            RectTransform iconRect = icon.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta = new Vector2(12f, 12f);

            CreateIconSegment(icon.transform, new Vector2(0f, 3f), new Vector2(8f, 1.5f), 0f);
            CreateIconSegment(icon.transform, new Vector2(4f, 0f), new Vector2(1.5f, 6f), 0f);
            CreateIconSegment(icon.transform, new Vector2(1f, -3f), new Vector2(6f, 1.5f), 0f);
            CreateIconSegment(icon.transform, new Vector2(-2.75f, 4.25f), new Vector2(4f, 1.5f), 45f);
            CreateIconSegment(icon.transform, new Vector2(-2.75f, 1.75f), new Vector2(4f, 1.5f), -45f);
        }

        private static void CreateIconSegment(Transform parent, Vector2 position, Vector2 size, float rotation)
        {
            GameObject segment = CreateImageNode(parent, "Segment", ResetIconColor, false);
            RectTransform rect = segment.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localEulerAngles = new Vector3(0f, 0f, rotation);
        }

        private static GameObject CreateLayoutNode(Transform parent, string name)
        {
            GameObject node = new(name);
            node.transform.SetParent(parent, false);
            node.AddComponent<RectTransform>();
            return node;
        }

        private static GameObject CreateImageNode(Transform parent, string name, Color color, bool raycast)
        {
            GameObject node = CreateLayoutNode(parent, name);
            Image image = node.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return node;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, string value, float fontSize, TextAlignmentOptions alignment, Color color, bool bold)
        {
            GameObject node = CreateLayoutNode(parent, name);
            TextMeshProUGUI text = node.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.characterSpacing = 0f;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, float width, float height, float fontSize, bool primary, Color textColor, bool bold, Action onClick, out GameObject buttonObject, out TextMeshProUGUI labelText)
        {
            buttonObject = CreateImageNode(parent, name, Color.white, true);
            SetPreferredSize(buttonObject, width, height);

            Image image = buttonObject.GetComponent<Image>();
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = primary ? AccentColor : ButtonColor;
            colors.highlightedColor = primary ? AccentHoverColor : ButtonHoverColor;
            colors.pressedColor = primary ? AccentPressedColor : ButtonPressedColor;
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = ButtonDisabledColor;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener((UnityAction)onClick);

            Outline outline = buttonObject.AddComponent<Outline>();
            outline.effectColor = primary ? AccentColor : ButtonBorderColor;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;

            labelText = CreateText(buttonObject.transform, "Label", label, fontSize, TextAlignmentOptions.Center, textColor, bold);
            SetStretch(labelText.GetComponent<RectTransform>());
            return button;
        }

        private static void AddBorder(GameObject target)
        {
            Outline outline = target.AddComponent<Outline>();
            outline.effectColor = BorderColor;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;
        }

        private static void SetStretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetPreferredSize(GameObject target, float width, float height)
        {
            LayoutElement element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            element.minWidth = width;
            element.preferredWidth = width;
            element.flexibleWidth = 0f;
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
        }

        private static void SetPreferredWidth(GameObject target, float width)
        {
            LayoutElement element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            element.minWidth = width;
            element.preferredWidth = width;
            element.flexibleWidth = 0f;
        }

        private static void SetPreferredHeight(GameObject target, float height)
        {
            LayoutElement element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
        }

        private static void SetFlexibleWidth(GameObject target, float weight)
        {
            LayoutElement element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            element.minWidth = 0f;
            element.preferredWidth = 0f;
            element.flexibleWidth = weight;
        }

        private static void SetColumnRatio(GameObject target, float ratio)
        {
            LayoutElement element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            element.minWidth = 0f;
            element.preferredWidth = 0f;
            element.flexibleWidth = ratio;
        }

        private static Color Rgb(int red, int green, int blue)
            => new(red / 255f, green / 255f, blue / 255f, 1f);

        private static Color Rgba(int red, int green, int blue, float alpha)
            => new(red / 255f, green / 255f, blue / 255f, alpha);
    }
}

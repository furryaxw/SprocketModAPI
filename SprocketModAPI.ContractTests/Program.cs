using System;
using System.IO;
using SprocketModAPI;

internal static class Program
{
    private static int Main()
    {
        Check(SprocketApi.ApiVersion == new Version(1, 0), "API version");
        Check(typeof(SprocketApi).Assembly.GetName().Version == new Version(0, 1, 0, 0), "release assembly version");
        Check(SprocketApi.IsCompatible(new Version(1, 0)), "same version compatible");
        Check(!SprocketApi.IsCompatible(new Version(2, 0)), "different major rejected");
        Check(!SprocketApi.IsCompatible(new Version(1, 1)), "newer minor rejected");

        var modifierOnly = new KeyChord("<Keyboard>/leftCtrl");
        Check(!modifierOnly.IsEmpty, "modifier-only binding");
        Check(modifierOnly == new KeyChord("<keyboard>/leftctrl"), "control path normalization");
        var combo = new KeyChord("<Keyboard>/z", ModifierKeys.LeftCtrl);
        Check(combo.Modifiers == ModifierKeys.LeftCtrl, "combination modifiers");

        bool rejected = false;
        try { _ = new KeyChord("Z"); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "display strings rejected as persistence paths");
        Check(typeof(IInputActionHandle).GetMethod("SetBinding") != null, "two-slot mutation contract");
        CheckUiLayoutSource();
        Console.WriteLine("SprocketModAPI contract tests passed (41/41).");
        return 0;
    }

    private static void CheckUiLayoutSource()
    {
        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string ui = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "InputManagerUi.cs"));
        string observers = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "SettingsPageObservers.cs"));
        string api = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Api.cs"));
        string readme = File.ReadAllText(Path.Combine(repositoryRoot, "README.md"));
        string releaseNotes = File.ReadAllText(Path.Combine(repositoryRoot, "RELEASE_NOTES.md"));

        Check(!ui.Contains("[SMA-UI-") && !observers.Contains("[SMA-UI-") && !api.Contains("[SMA-UI-"), "temporary UI diagnostics removed");
        Check(readme.Contains("当前发行版本为 `0.1.0`，公共 API 版本为 `1.0`"), "README release and API versions are current");
        Check(releaseNotes.StartsWith("# Sprocket Mod API v0.1.0", StringComparison.Ordinal), "release notes version is current");

        int headerPanel = ui.IndexOf("CreateHeaderPanel(uiWindow.transform)", StringComparison.Ordinal);
        int searchPanel = ui.IndexOf("CreateSearchPanel(uiWindow.transform)", StringComparison.Ordinal);
        int mainPanel = ui.IndexOf("CreateMainContentPanel(uiWindow.transform)", StringComparison.Ordinal);
        int footerPanel = ui.IndexOf("CreateFooterPanel(uiWindow.transform)", StringComparison.Ordinal);
        Check(headerPanel >= 0 && headerPanel < searchPanel && searchPanel < mainPanel && mainPanel < footerPanel, "window child hierarchy order");
        Check(ui.IndexOf("\"Header Row\"", StringComparison.Ordinal) < ui.IndexOf("\"Scroll View Viewport\"", StringComparison.Ordinal)
            && ui.IndexOf("\"Scroll View Viewport\"", StringComparison.Ordinal) < ui.IndexOf("viewport.transform, \"Content\"", StringComparison.Ordinal), "main-content child hierarchy order");

        Check(ui.Contains("new RectOffset((int)WindowPadding, (int)WindowPadding, (int)WindowPadding, (int)WindowPadding)"), "window padding is 24px on all sides");
        Check(ui.Contains("layout.padding = new RectOffset(RowHorizontalPadding, RowHorizontalPadding, RowVerticalPadding, RowVerticalPadding)"), "compact row uses symmetric horizontal and vertical padding");
        Check(ui.Contains("headerLayout.padding = new RectOffset(RowHorizontalPadding, RowHorizontalPadding, 0, 0)"), "header text is vertically centered without a bottom inset");
        Check(ui.Contains("private const float BindingColumnWidth = (ContentWidth - 2f - ScrollbarWidth - (RowHorizontalPadding * 2f)) / 5f"), "Primary and Secondary retain their original column width");
        Check(ui.Contains("private const float ResetColumnWidth = BindingColumnWidth / 4f"), "Reset column is one quarter of its original width");
        Check(ui.Contains("SetFlexibleWidth(action.gameObject, 1f)") && ui.Contains("CreateColumnCell(row.transform, \"Primary\", BindingColumnWidth, PrimaryContentPadding)"), "Action absorbs all width freed by the right-aligned fixed columns");
        Check(ui.Contains("private const float HeaderRowHeight = 30f")
            && ui.Contains("private const float ModHeaderHeight = 30f")
            && ui.Contains("private const float ActionRowHeight = 40f"), "header, Mod, and key rows use the confirmed fixed heights");
        Check(ui.Contains("SearchSurfaceColor = Rgb(13, 13, 13)")
            && ui.Contains("HeaderSurfaceColor = Rgb(22, 22, 22)")
            && ui.Contains("TableBackgroundColor = Rgb(26, 26, 26)")
            && ui.Contains("KeyRowColor = Rgb(21, 21, 21)"), "search, header, table, and key rows use distinct confirmed colors");
        Check(ui.Contains("CreateImageNode(parent, \"Main Content Panel\", TableBackgroundColor, true)")
            && ui.Contains("CreateImageNode(panel.transform, \"Scroll View Viewport\", TableBackgroundColor, true)")
            && ui.Contains("CreateImageNode(parent, \"Key Bind\", KeyRowColor, true)"), "table empty space differs from key rows");
        Check(ui.Contains("private const float ModSeparatorHeight = 2f")
            && ui.Contains("CreateImageNode(parent, \"Mod Separator\", ButtonBorderColor, false)")
            && ui.Contains("if (list.Length != 0)")
            && ui.Contains("if (currentMod != null)"), "visible separators replace blank space above the first Mod and between mod groups");
        Check(ui.Contains("private const int PrimaryContentPadding = 80")
            && ui.Contains("primaryLabel.margin = new Vector4(PrimaryContentPadding, 0f, 0f, 0f)"), "Primary content moves 40px toward Secondary");
        Check(ui.Contains("private static readonly Color BackdropColor = Rgba(17, 17, 17, 0.82f)"), "modal backdrop is semi-transparent");
        Check(ui.Contains("uiEntryRect.sizeDelta = new Vector2(EntryWidth, EntryHeight)"), "entry RectTransform keeps the 190x42 aspect ratio");
        Check(ui.Contains("return CreateText(parent, name, value, 12f, alignment, BoundTextColor, false)"), "list headers use the updated color");
        Check(ui.Contains("CreateResetIcon(resetButton.transform)") && !ui.Contains("\\u21B6"), "Reset uses a font-independent icon");
        Check(ui.Contains("ContentSizeFitter.FitMode.PreferredSize"), "scroll content uses preferred-size fitting");
        Check(ui.Contains("\"Input Field\"") && ui.Contains("AddComponent<TMP_InputField>()"), "search uses TMP_InputField");
        Check(ui.Contains("\"Close Button\", \"CLOSE\"") && !ui.Contains("\"X\", Window"), "footer Close is the only window close control");
        Check(ui.Contains("Rgb(26, 26, 26)") && ui.Contains("Rgb(21, 21, 21)") && ui.Contains("Rgb(51, 51, 51)"), "required panel palette is present");
        Check(ui.Contains("ButtonColor = new(0.17f, 0.18f, 0.19f, 1f)")
            && ui.Contains("ButtonHoverColor = new(0.25f, 0.26f, 0.27f, 1f)")
            && ui.Contains("ButtonPressedColor = new(0.1f, 0.105f, 0.11f, 1f)"), "Multiplayer normal button colors are copied exactly");
        Check(ui.Contains("AccentColor = new(0.76f, 0.54f, 0.22f, 1f)")
            && ui.Contains("AccentHoverColor = new(0.9f, 0.67f, 0.3f, 1f)")
            && ui.Contains("AccentPressedColor = new(0.55f, 0.36f, 0.12f, 1f)"), "Multiplayer accent button colors are copied exactly");
        Check(ui.Contains("ButtonDisabledColor = new(0.11f, 0.115f, 0.12f, 0.65f)")
            && ui.Contains("ButtonBorderColor = new(0.38f, 0.4f, 0.41f, 0.9f)")
            && ui.Contains("colors.fadeDuration = 0.08f"), "Multiplayer disabled, border, and transition values are copied exactly");
        Check(!ui.Contains("CreateFixedPanel") && !ui.Contains("SetFixed("), "legacy fixed-coordinate row layout removed");
        Check(observers.Contains("Content/Action buttons") && observers.Contains("OnRectTransformDimensionsChange"), "entry alignment listens to Action buttons rect changes");
        Check(observers.Contains("WorldToScreenPoint") && observers.Contains("ScreenPointToLocalPointInRectangle"), "entry left edge converts through screen space");
        Check(ui.Contains("uiEntryObject!.SetActive(!uiVisible && entryAlignmentReady)"), "entry stays hidden until event-driven alignment is ready");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Contract failed: {name}");
    }
}

using System;
using System.IO;
using SprocketModAPI;

internal static class Program
{
    private static int Main()
    {
        CheckPublicBehavior();
        CheckServiceRegistryBehavior();
        KeybindingStoreTests.Run();
        BindingConflictIndexTests.Run();
        BindingNormalizationTests.Run();
        InputRoutingTests.Run();
        UiServiceTests.Run();
        Console.WriteLine("Behavior contracts passed.");
        CheckSourceLayout();
        Console.WriteLine("Source-layout contracts passed.");
        return 0;
    }

    private static void CheckPublicBehavior()
    {
        Check(SprocketApi.ApiVersion == new Version(1, 1), "API version");
        Version? releaseVersion = typeof(SprocketApi).Assembly.GetName().Version;
        Check(releaseVersion == new Version(0, 2, 0, 1), $"release assembly version ({releaseVersion})");
        Check(SprocketApi.IsCompatible(new Version(1, 0)), "same version compatible");
        Check(!SprocketApi.IsCompatible(new Version(2, 0)), "different major rejected");
        Check(SprocketApi.IsCompatible(new Version(1, 1)), "newer UI minor is compatible");
        Check(!SprocketApi.IsCompatible(new Version(1, 2)), "newer minor rejected");

        var modifierOnly = new KeyChord("<Keyboard>/leftCtrl");
        Check(!modifierOnly.IsEmpty, "modifier-only binding");
        Check(modifierOnly == new KeyChord("<keyboard>/leftctrl"), "control path normalization");
        var combo = new KeyChord("<Keyboard>/z", ModifierKeys.LeftCtrl);
        Check(combo.Modifiers == ModifierKeys.LeftCtrl, "combination modifiers");

        bool rejected = false;
        try { _ = new KeyChord("Z"); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "display strings rejected as persistence paths");
        Check(typeof(IInputActionHandle).GetMethod("SetBinding") != null, "two-slot mutation contract");
    }

    private static void CheckServiceRegistryBehavior()
    {
        string? loggedError = null;
        var registry = new ServiceRegistry(message => loggedError = message);
        var service = new TestService();
        IDisposable registration = registry.Register<ITestService>(service);

        Check(registry.TryGet(out ITestService? resolved) && ReferenceEquals(service, resolved), "registered service resolves by interface");

        bool duplicateRejected = false;
        try { registry.Register<ITestService>(new TestService()); }
        catch (InvalidOperationException) { duplicateRejected = true; }
        Check(duplicateRejected && loggedError != null && loggedError.Contains("Duplicate service registration"), "duplicate service registration rejected and logged");

        SprocketApi.Attach(registry);
        Check(SprocketApi.TryGetService<ITestService>(out ITestService? publicService) && ReferenceEquals(service, publicService), "public lookup uses service registry");
        SprocketApi.Detach(registry);
        Check(!SprocketApi.TryGetService<ITestService>(out _), "public lookup clears on registry detach");

        registration.Dispose();
        Check(service.Disposed, "service registration owns service lifetime");
        Check(!registry.TryGet<ITestService>(out _), "unregistered service retains no stale reference");
        registry.Dispose();

        var shutdownRegistry = new ServiceRegistry(_ => { });
        var shutdownService = new TestService();
        shutdownRegistry.Register<ITestService>(shutdownService);
        shutdownRegistry.Dispose();
        Check(shutdownService.Disposed, "registry shutdown disposes remaining services");
        Check(!shutdownRegistry.TryGet<ITestService>(out _), "registry shutdown clears remaining service references");
    }

    private static void CheckSourceLayout()
    {
        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string keybindingsRoot = Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "Keybindings");
        string coreRoot = Path.Combine(repositoryRoot, "SprocketModAPI", "Core");
        string ui = File.ReadAllText(Path.Combine(keybindingsRoot, "KeybindingUiController.cs"));
        string observers = File.ReadAllText(Path.Combine(keybindingsRoot, "SettingsPageObservers.cs"));
        string conflictIndex = File.ReadAllText(Path.Combine(keybindingsRoot, "BindingConflictIndex.cs"));
        string input = File.ReadAllText(Path.Combine(keybindingsRoot, "InputService.cs"));
        string module = File.ReadAllText(Path.Combine(keybindingsRoot, "KeybindingsModule.cs"));
        string nativeLease = File.ReadAllText(Path.Combine(keybindingsRoot, "NativeSettingsInputLease.cs"));
        string api = File.ReadAllText(Path.Combine(coreRoot, "Api.cs"));
        string uiModule = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "Ui", "UiModule.cs"));
        string uiContracts = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "Ui", "Contracts.cs"));
        string uiBackend = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "Ui", "UnityUiBackend.cs"));
        string contractProject = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI.ContractTests", "SprocketModAPI.ContractTests.csproj"));
        string keybindingsApi = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "keybindings-api.md"));
        string uiApi = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "ui-api.md"));
        string readme = File.ReadAllText(Path.Combine(repositoryRoot, "README.md"));
        string releaseNotes = File.ReadAllText(Path.Combine(repositoryRoot, "RELEASE_NOTES.md"));

        Check(api.Contains("IRuntimeModule[]") && api.Contains("foreach (IRuntimeModule module in modules)"), "Core hosts modules through common lifecycle");
        Check(!api.Contains("InputService") && !api.Contains("UiService") && !api.Contains("KeybindingUiController"), "Core does not own module implementations");
        Check(uiModule.Contains("Register<IUiService>"), "UI service is registered through a module");
        Check(contractProject.Contains("AdditionalProperties=\"SkipModDeploy=true\"", StringComparison.Ordinal),
            "offline contract project reference cannot deploy the API into the live game");
        Check(uiContracts.Contains("interface IUiService") && uiContracts.Contains("interface IUiScope"), "UI public contracts exist");
        Check(uiContracts.Contains("CreateMainMenuButtonAsync") && uiContracts.Contains("BelowNativeButtonText"), "advanced main-menu placement contract exists");
        Check(keybindingsApi.Contains("RegisterAction") && keybindingsApi.Contains("ModifierKeys") && keybindingsApi.Contains("ActionsChanged")
            && uiApi.Contains("CreateMainMenuButtonAsync") && uiApi.Contains("UiFailureCode") && uiApi.Contains("UiCapabilitySnapshot"),
            "public API documentation covers the complete keybinding and UI contracts");
        Check(uiContracts.Contains("TemplateNotFound"), "UI structured failures include template failure");
        Check(uiBackend.Contains("Resources.FindObjectsOfTypeAll<MenuPanel>()")
            && uiBackend.Contains("panel.buttonPrefab")
            && uiBackend.Contains("panel.GetComponentsInChildren<Tab>(true)")
            && uiBackend.Contains("Resources.FindObjectsOfTypeAll<Tab>()")
            && uiBackend.Contains("candidate.hideFlags != HideFlags.HideAndDontSave")
            && uiBackend.Contains("UiFailureCode.TemplateNotFound"),
            "Menu Button uses a probed native Tab template and fails closed");
        Check(uiBackend.Contains("GetComponent<RectTransform>()")
            && !uiBackend.Contains("(RectTransform)root.transform"),
            "UI roots use real RectTransform components without unsafe Transform casts");
        Check(uiBackend.Contains("GetComponentInParent<Canvas>()")
            && uiBackend.Contains("GetComponent<GraphicRaycaster>()")
            && uiBackend.Contains("Menu button parent must be under an active Canvas with GraphicRaycaster"),
            "Menu Button creation requires an interactive Canvas parent");
        Check(uiContracts.Contains("interface IUiButtonHandle") && uiContracts.Contains("new bool IsDisposed")
            && !uiContracts.Contains("UiButtonDefinition") && !uiContracts.Contains("CreateButtonAsync")
            && !uiBackend.Contains("FindButtonTemplate") && !uiBackend.Contains("UiCapability.Button"),
            "ordinary Button capability and factory are removed while Menu Button handle ABI remains compatible");
        Check(uiBackend.Contains("Native pooled objects are game-owned"),
            "scene cleanup tolerates Unity-owned controls already destroyed by unload");
        int uiServiceStart = uiBackend.IndexOf("internal sealed class UiService", StringComparison.Ordinal);
        int uiBackendStart = uiBackend.IndexOf("internal sealed class UnityUiBackend", StringComparison.Ordinal);
        int uiServiceDisposeStart = uiBackend.IndexOf("public void Dispose()", uiServiceStart, StringComparison.Ordinal);
        string uiServiceDispose = uiBackend.Substring(uiServiceDisposeStart, uiBackendStart - uiServiceDisposeStart);
        Check(!uiServiceDispose.Contains("Update();", StringComparison.Ordinal)
            && uiServiceDispose.IndexOf("disposed = true;", StringComparison.Ordinal)
                < uiServiceDispose.IndexOf("scope.Dispose();", StringComparison.Ordinal),
            "UI shutdown closes service before releasing native UI and never runs a final backend update");
        Check(uiBackend.Contains("menuRect.anchorMin = new Vector2(0.5f, 0.5f)")
            && uiBackend.Contains("menuRect.sizeDelta = definition.Size ?? new Vector2(180f, 36f)"),
            "cloned Menu Button layout is reset from native stretch settings");
        Check(!ui.Contains("[SMA-UI-") && !observers.Contains("[SMA-UI-") && !api.Contains("[SMA-UI-"), "temporary UI diagnostics removed");
        Check(conflictIndex.Contains("Resources.FindObjectsOfTypeAll<InputActionAsset>()")
            && !conflictIndex.Contains(".Enable()") && !conflictIndex.Contains(".Disable()"), "native InputActionAsset import is read-only");
        Check(ui.Contains("FormatConflictSummary") && ui.Contains("Conflict:\\n"), "management UI renders grouped conflict source and binding summary");
        Check(ui.Contains("uiScroll.scrollSensitivity = 0.2f"), "mouse wheel sensitivity is reduced to 0.2");
        Check(readme.Contains("当前发行版本为 `0.2.0-fix1`，公共 API 版本为 `1.1`"), "README release and API versions are current");
        Check(releaseNotes.StartsWith("# Sprocket Mod API v0.2.0-fix1", StringComparison.Ordinal), "release notes version is current");

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
        Check(!input.Contains("FindObjectOfType<SettingsMenu>"), "input routing does not search for SettingsMenu every frame");
        Check(input.Contains("EventSystem can retain an IL2CPP wrapper")
            && input.Contains("catch (Exception)"),
            "destroyed EventSystem selections are isolated during text-input probing");
        Check(module.Contains("NotifySceneUnloaded(sceneName)") && input.Contains("route.BeginTransition()"),
            "scene unload participates in the input transition gate");
        Check(nativeLease.Contains("NativeSettingsInputLease.Acquire")
            && nativeLease.Contains("snapshot.Restore()")
            && nativeLease.Contains("sendNavigationEvents = false"),
            "native settings input lease has capture, navigation isolation, and restoration paths");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Contract failed: {name}");
    }

    private interface ITestService { }

    private sealed class TestService : ITestService, IDisposable
    {
        internal bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}

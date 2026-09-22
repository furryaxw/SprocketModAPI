using System;
using System.IO;
using System.Linq;
using SprocketModAPI;

internal static class Program
{
    private static int Main()
    {
        CheckPublicBehavior();
        CheckServiceRegistryBehavior();
        RuntimeModuleHostTests.Run();
        KeybindingStoreTests.Run();
        BindingConflictIndexTests.Run();
        BindingNormalizationTests.Run();
        InputRoutingTests.Run();
        UiServiceTests.Run();
        ModMetadataTests.Run();
        MetadataSourceTests.Run();
        ModConfigTests.Run();
        ModMenuTests.Run();
        ModMenuStyleTests.Run();
        ModMenuLayoutTests.Run();
        ModMenuEntryVisibilityTests.Run();
        ModIdentityTests.Run();
        ChordDisplayTests.Run();
        ApiLogTests.Run();
        Console.WriteLine("Behavior contracts passed.");
        CheckSourceLayout();
        Console.WriteLine("Source-layout contracts passed.");
        TextIntegrityTests.Run();
        Console.WriteLine("Text-integrity contracts passed.");
        return 0;
    }

    private static void CheckPublicBehavior()
    {
        Check(SprocketApi.ApiVersion == new Version(2, 0), "API version");
        Version? releaseVersion = typeof(SprocketApi).Assembly.GetName().Version;
        Check(releaseVersion == new Version(0, 3, 0, 0), $"release assembly version ({releaseVersion})");
        Check(!SprocketApi.IsCompatible(new Version(1, 0)), "1.x is not accepted");
        Check(SprocketApi.IsCompatible(new Version(2, 0)), "current version compatible");
        Check(!SprocketApi.IsCompatible(new Version(1, 2)), "1.2 is not accepted");
        Check(!SprocketApi.IsCompatible(new Version(3, 0)), "different major rejected");
        Check(!SprocketApi.IsCompatible(new Version(2, 1)), "newer minor rejected");

        var modifierOnly = new KeyChord("<Keyboard>/leftCtrl");
        Check(!modifierOnly.IsEmpty, "modifier-only binding");
        Check(modifierOnly == new KeyChord("<keyboard>/leftctrl"), "control path normalization");
        var combo = new KeyChord("<Keyboard>/z", ModifierKeys.LeftCtrl);
        Check(combo.Modifiers == ModifierKeys.LeftCtrl, "combination modifiers");

        bool rejected = false;
        try { _ = new KeyChord("Z"); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "display strings rejected as persistence paths");
        Check(typeof(IInputActionHandle).GetMethod("SetBinding") != null, "two-slot mutation contract");

        Check(default(KeyChord).IsEmpty && default(KeyChord).DisplayName == "Unbound",
            "an unbound chord is a supported state with a readable label");
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
        string modMetadataDoc = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "mod-metadata.md"));
        string modMetaReader = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "ModMeta", "ModMetadataReader.cs"));
        string modMetaSource = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "ModMeta", "LoadedModSource.cs"));
        string modMetaModule = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "ModMeta", "ModMetaModule.cs"));
        string modConfigModule = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "ModConfig", "ModConfigModule.cs"));
        string modConfigService = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "ModConfig", "ModConfigService.cs"));
        string modConfigStore = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "ModConfig", "ModConfigStore.cs"));
        string modConfigDoc = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "mod-config-api.md"));
        string modMenuModule = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "ModMenu", "ModMenuModule.cs"));
        string modMenuContracts = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "ModMenu", "Contracts.cs"));
        string assemblyInfo = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Core", "AssemblyInfo.cs"));
        string modMenuWindow = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "ModMenu", "ModMenuWindow.cs"))
            + File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "ModMenu", "ModMenuWindow.Pages.cs"));
        string modMenuEntry = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "ModMenu", "ModMenuSettingsEntry.cs"));
        string modMenuStyle = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "ModMenu", "ModMenuStyle.cs"));
        string modMenuLayout = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "ModMenu", "ModMenuLayout.cs"));
        string runtimeModule = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Core", "RuntimeModule.cs"));
        string apiSelfSettings = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Core", "ApiSelfSettings.cs"));
        string uiDebugLog = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "Ui", "UiDebugSettings.cs"))
            + File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Modules", "Keybindings", "KeybindingDebugSettings.cs"));
        string uiAcceptance = File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI.UiAcceptance", "UiAcceptanceMod.cs"));
        string readme = File.ReadAllText(Path.Combine(repositoryRoot, "README.md"));
        string releaseNotes = File.ReadAllText(Path.Combine(repositoryRoot, "RELEASE_NOTES.md"));

        Check(api.Contains("IRuntimeModule[]") && api.Contains("RuntimeModuleHost.InitializeAll(modules, context)"), "Core hosts modules through common lifecycle");
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
        Check(uiContracts.Contains("interface IUiButtonHandle") && uiContracts.Contains("new bool IsDisposed"),
            "the UI contracts declare the Menu Button handle surface");
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
        Check(uiBackend.Contains("debug.Lifecycle($\"native-registration")
            && uiBackend.Contains("error($\"[SMA-UI] create menu button failed"),
            "successful native registration activity goes to diagnostics while real failures stay on the error sink");
        Check(conflictIndex.Contains("Resources.FindObjectsOfTypeAll<InputActionAsset>()")
            && !conflictIndex.Contains(".Enable()") && !conflictIndex.Contains(".Disable()"), "native InputActionAsset import is read-only");
        Check(ui.Contains("FormatConflictSummary") && ui.Contains("Conflict:\\n"), "management UI renders grouped conflict source and binding summary");
        Check(ui.Contains("uiScroll.scrollSensitivity = 0.2f"), "mouse wheel sensitivity is reduced to 0.2");
        Check(readme.Contains("当前发行版本为 `0.3.0`，公共 API 版本为 `2.0`"), "README release and API versions are current");
        Check(releaseNotes.StartsWith("# Sprocket Mod API v0.3.0", StringComparison.Ordinal), "release notes version is current");

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
        Check(ui.Contains("private const float BindingColumnWidth = (ContentWidth - 2f - ScrollbarWidth - (RowHorizontalPadding * 2f)) / 5f"), "Primary and Secondary share a fixed column width derived from the content width");
        Check(ui.Contains("private const float ResetColumnWidth = BindingColumnWidth / 4f"), "Reset column is one quarter of the binding column");
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
            && ui.Contains("if (currentMod != null)"), "visible separators divide mod groups and leave no blank space above the first Mod");
        Check(ui.Contains("private const int PrimaryContentPadding = 80")
            && ui.Contains("primaryLabel.margin = new Vector4(PrimaryContentPadding, 0f, 0f, 0f)"), "Primary content moves 40px toward Secondary");
        Check(ui.Contains("private static readonly Color BackdropColor = Rgba(17, 17, 17, 0.82f)"), "modal backdrop is semi-transparent");
        Check(ui.Contains("uiEntryRect.sizeDelta = new Vector2(EntryWidth, EntryHeight)"), "entry RectTransform starts from the native button size");
        Check(ui.Contains("return CreateText(parent, name, value, 12f, alignment, BoundTextColor, false)"), "list headers use the bound header color");
        Check(ui.Contains("CreateResetIcon(resetButton.transform)"), "Reset uses a font-independent icon");
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
        Check(api.Contains("new ModMetaModule()"), "metadata module is hosted through the common module list");
        Check(modMetaSource.Contains("MelonBase.RegisteredMelons") && modMetaSource.Contains("MelonAssembly")
            && modMetaSource.Contains("AssemblyMetadataAttribute")
            && !modMetaSource.Contains("Assembly.Load"),
            "metadata source reads registered melons and assembly metadata without loading assemblies");
        Check(modMetaSource.Contains("MelonAdditionalDependenciesAttribute") && modMetaSource.Contains("MelonIncompatibleAssembliesAttribute")
            && modMetaSource.Contains("CollectAssemblyNames"),
            "metadata source reads required dependencies and incompatible assemblies from the assembly");
        Check(modMenuWindow.Contains("AddDetailRow(\"Requires\"") && modMenuWindow.Contains("AddDetailRow(\"Incompatible\""),
            "mod menu details show required dependencies and incompatible assemblies");
        Check(modMetaModule.Contains("Register<IModMetadataService>") && modMetaModule.Contains("MelonBase.RegisteredMelons.Count"),
            "metadata service is registered through a module and re-snapshots on registration changes");
        Check(modMetaReader.Contains("Sprocket.Mod.") && modMetaReader.Contains("file:"),
            "metadata reader implements the declared key prefix and identity fallback");
        Check(modMetadataDoc.Contains("Sprocket.Mod.Id") && modMetadataDoc.Contains(".dll.disable")
            && modMetadataDoc.Contains("永不执行 DLL 代码"),
            "cross-repository metadata contract documents ids, disable convention and static-read rule");
        Check(api.Contains("new ModConfigModule()"), "config module is hosted through the common module list");
        Check(modConfigModule.Contains("Register<IModConfigService>")
            && modConfigModule.Contains("Path.Combine(MelonEnvironment.UserDataDirectory, \"SprocketModAPI\", \"modconfig\")"),
            "config service is registered through a module and persists under UserData/SprocketModAPI/modconfig");
        Check(modConfigModule.Contains("new ApiSelfSettings(") && apiSelfSettings.Contains("ModIdentity.ResolveModId")
            && apiSelfSettings.Contains("IModConfigRegistration"),
            "the API registers its own settings page under its declared id");
        Check(api.IndexOf("new ModConfigModule()") < api.IndexOf("new KeybindingsModule()"),
            "the config module initializes first so the API's own settings page exists before keybindings/UI read it");
        Check(uiDebugLog.Contains("ApiSelfSettings"),
            "UI/keybinding diagnostics read the API's own config page");
        Check(modConfigService.Contains("Validate(definition)") && modConfigService.Contains("Duplicate mod config entry key")
            && modConfigService.Contains("Math.Clamp"),
            "config service validates definitions up front and clamps stored numbers");
        Check(modConfigStore.Contains("WriteAtomic") && modConfigStore.Contains(".corrupt-")
            && modConfigStore.Contains("ConfigVersion"),
            "config store keeps atomic writes, corrupt backups and a migratable config version");
        Check(modConfigDoc.Contains("IModConfigRegistration") && modConfigDoc.Contains("modconfig/<modId>.json")
            && modConfigDoc.Contains("ModConfigEntryDefinition.Slider"),
            "config API documentation covers registration, persistence path and entry kinds");
        Check(api.Contains("new ModMenuModule()"), "mod menu module is hosted through the common module list");
        Check(modMenuModule.Contains("Register<IModMenuService>") && modMenuModule.Contains("AcquireInputBlock")
            && modMenuModule.Contains("new ModMenuSettingsEntry("),
            "mod menu registers its service, blocks API input while open and installs the settings-page entry");
        Check(!modMenuContracts.Contains("public interface IModMenuService")
            && modMenuContracts.Contains("internal interface IModMenuService")
            && assemblyInfo.Contains("SprocketModAPI.UiAcceptance"),
            "the mod menu service is internal and only the acceptance assembly is a friend");
        Check(!modMenuModule.Contains("DefaultPrimary"),
            "the mod menu action ships unbound: the settings-page entry is the only default way in");
        Check(input.Contains("KeyChord.IsEmpty"),
            "keybinding validation accepts a fully empty default (unbound-by-default actions)");
        Check(keybindingsApi.Contains("默认键位可以全空"), "keybinding documentation records unbound defaults");
        Check(modMenuWindow.Contains("ModFileToggle.Disable") && modMenuWindow.Contains("ModFileToggle.Enable")
            && modMenuWindow.Contains("RESTART REQUIRED"),
            "mod menu toggles .dll.disable files and reports the required restart");
        Check(modMenuWindow.Contains("ModMenuListModel.Filter") && modMenuWindow.Contains("IsSelfAssembly")
            && !modMenuWindow.Contains("KeybindingUiController"),
            "mod menu filters rows through the list model and never reaches into another module's internals");
        Check(modMenuWindow.Contains("ModAssemblyIndex.Collect") && modMenuWindow.Contains("escapeKey.wasPressedThisFrame")
            && modMenuWindow.Contains("missing-deps="),
            "mod menu indexes on-disk assemblies for dependency checks, closes on escape and logs the missing-dependency count");
        Check(modMenuWindow.Contains("AddDetailRow(\"Missing\"") && modMenuWindow.Contains("HasIncompatiblePresent"),
            "mod menu details surface missing dependencies and present incompatibilities");
        Check(runtimeModule.Contains("internal static int InitializeAll") && runtimeModule.Contains("Module initialization failed")
            && runtimeModule.Contains("internal static void ShutdownAll"),
            "module host isolates per-module initialize and shutdown failures");
        Check(api.Contains("failures != 0") && api.Contains("module(s) unavailable"),
            "the API reports module failures without aborting the remaining services");
        Check(modMenuWindow.Contains("[SMA-MENU] open mods=") && modMenuWindow.Contains("[SMA-MENU] config-page mod=")
            && modMenuWindow.Contains("[SMA-MENU] disabled ") && modMenuWindow.Contains("[SMA-MENU] close"),
            "mod menu logs open, config-page, toggle and close diagnostics for in-game acceptance");
        Check(modMenuEntry.Contains("[SMA-MENU] settings entry attached") && modMenuEntry.Contains("Content/Action buttons")
            && modMenuEntry.Contains("GeneralPageLabel") && modMenuEntry.Contains("TabState.Selected")
            && modMenuEntry.Contains("EntryBottom") && modMenuEntry.Contains("ModMenuStyle.EntryAppearance")
            && modMenuEntry.Contains("FindNativeButtonRect"),
            "the settings-page entry is aligned to a native button, gated on the General tab and styled through the shared mod menu style");
        Check(modMenuStyle.Contains("Rgb(58, 58, 58)") && modMenuStyle.Contains("Rgb(72, 72, 72)")
            && modMenuStyle.Contains("Rgb(44, 44, 44)") && modMenuStyle.Contains("Rgb(90, 90, 90)")
            && modMenuStyle.Contains("Rgb(235, 235, 235)"),
            "the shared style owns the native grey button palette in exactly one place");
        Check(modMenuEntry.Contains("image.color = Color(ModMenuStyle.White)")
            && !modMenuEntry.Contains("image.color = Color(appearance.Surface)")
            && !modMenuEntry.Contains("image.color = SurfaceColor"),
            "the entry keeps a white background so the grey state colours are not squared by UGUI");
        Check(modMenuEntry.Contains("FindNativeSelectable(observedActionButtons)") && modMenuEntry.Contains("native.colors")
            && modMenuEntry.Contains("ModMenuStyle.Resolve(true, sample)")
            && modMenuEntry.Contains("settings entry style source="),
            "the entry samples the native settings button state colours and reports the style source");
        Check(!modMenuEntry.Contains("KeybindingUiController") && !modMenuEntry.Contains("Modules.Keybindings"),
            "the settings entry copies the observed layout without reaching into the keybindings module");
        Check(modMenuLayout.Contains("internal const float WindowWidth") && modMenuLayout.Contains("internal const float ListWidth")
            && modMenuLayout.Contains("internal static float DetailWidth") && modMenuLayout.Contains("FitsInCanvas"),
            "the window geometry lives in one place with derived design invariants");
        Check(modMenuWindow.Contains("ModMenuLayout.WindowWidth"),
            "the window consumes the shared geometry");
        Check(modMenuEntry.Contains("ModMenuLayout.CanvasWidth"),
            "the entry reads the canvas size from the shared layout");
        Check(modMenuEntry.Contains("ModMenuEntryVisibility.ShouldApply")
            && modMenuEntry.Contains("ModMenuEntryVisibility.ShouldShow")
            && !modMenuEntry.Contains("settingsMenuActive && generalPageActive"),
            "the entry delegates its visibility gate to the pure logic instead of inlining it");
        Check(uiAcceptance.Contains("RunMenuSelfCheck") && uiAcceptance.Contains("modMenu.Open()")
            && uiAcceptance.Contains("menu-self-check") && uiAcceptance.Contains("metadata mods="),
            "the UI acceptance mod automatically opens the mod menu so the log carries menu evidence");
        foreach ((string directory, string tag) in new[]
                 {
                     ("Modules/Keybindings", "SMA-KEY"),
                     ("Modules/Ui", "SMA-UI"),
                     ("Modules/ModMenu", "SMA-MENU"),
                     ("Modules/ModConfig", "SMA-CONFIG"),
                     ("Modules/ModMeta", "SMA-META")
                 })
        {
            string root = Path.Combine(repositoryRoot, "SprocketModAPI", directory);
            string offenders = string.Join(", ", Directory
                .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).Contains("\"[SMA] ", StringComparison.Ordinal)
                               || File.ReadAllText(path).Contains("\"[SMA]\"", StringComparison.Ordinal))
                .Select(Path.GetFileName));
            Check(offenders.Length == 0, $"{directory} must tag its logs with [{tag}] (offenders: {offenders})");
        }

        Check(File.ReadAllText(Path.Combine(repositoryRoot, "SprocketModAPI", "Core", "ApiLog.cs"))
                .Contains("alreadyWarned")
            && apiSelfSettings.Contains("ApiSelfSettings"),
            "ApiLog owns the de-duplication rules and the diagnostics switch lives in the API settings");
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

using System;
using System.Reflection;
using MelonLoader;
using SprocketModAPI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[assembly: MelonInfo(typeof(SprocketModAPI.UiAcceptance.UiAcceptanceMod), "Sprocket Mod API UI Acceptance", "0.2.0", "furryAxw")]
[assembly: MelonGame("HD", "Sprocket")]
[assembly: MelonAdditionalDependencies("SprocketModAPI")]
// 开发/验收专用模组：声明一个稳定 Id（不进 Registry），键位与配置命名空间都由它继承。
[assembly: AssemblyMetadata("Sprocket.Mod.Id", "furryaxw.sprocketmodapi-ui-acceptance")]
[assembly: AssemblyMetadata("Sprocket.Mod.DisplayName", "Sprocket Mod API UI Acceptance")]
[assembly: AssemblyMetadata("Sprocket.Mod.Description", "Acceptance harness for SprocketModAPI: native menu button, metadata and a declarative config page.")]
[assembly: AssemblyMetadata("Sprocket.Mod.Authors", "furryAxw")]
[assembly: AssemblyMetadata("Sprocket.Mod.Repository", "furryaxw/SprocketModAPI")]
[assembly: AssemblyMetadata("Sprocket.Mod.Category", "developer-tools")]
[assembly: AssemblyMetadata("Sprocket.Mod.License", "LGPL-3.0-or-later")]

namespace SprocketModAPI.UiAcceptance
{
    public sealed class UiAcceptanceMod : MelonMod
    {
        private IUiScope? scope;
        private IUiMenuButtonHandle? menuButton;
        private IUiService? ui;
        private IModConfigService? config;
        private IModConfigRegistration? configPage;
        private IModMenuService? modMenu;
        private GameObject? testRoot;
        private bool menuButtonAttempted;
        private bool menuSelfCheckStarted;
        private int menuCloseAtFrame = -1;
        private string currentScene = "";

        public override void OnInitializeMelon()
        {
            RegisterConfigPage();

            if (!SprocketApi.TryGetService<IUiService>(out IUiService? resolvedUi))
            {
                LoggerInstance.Error("[SMA-UI-ACCEPT] IUiService unavailable.");
                return;
            }

            SprocketApi.TryGetService(out modMenu);
            ui = resolvedUi!;
            ui.StatusChanged += OnUiStatusChanged;
            LoggerInstance.Msg($"[SMA-UI-ACCEPT] capabilities={ui.Capabilities.Available}");
            LoggerInstance.Msg("[SMA-UI-ACCEPT] automatic test: creates one native Menu Button in MainMenu, then waits for a click; scene unload cleanup is observed in API logs.");
            // CreateScope 从调用方程序集推断 ModId（本模组声明了 Sprocket.Mod.Id）。
            scope = ui.CreateScope(new UiOwnerDefinition { DisplayName = "UI Acceptance" });
        }

        // 自动的菜单冒烟测试：主菜单就绪后主动打开一次 Mod 菜单（驱动真实的窗口代码路径），
        // 让日志里出现 `[SMA-MENU] open ...` 证据；几秒后自动关闭，把输入控制权还回去。
        // 这只是"没抛异常、能建出来"，排版与手感仍需肉眼确认。
        private void RunMenuSelfCheck()
        {
            if (menuSelfCheckStarted)
                return;
            menuSelfCheckStarted = true;

            if (SprocketApi.TryGetService<IModMetadataService>(out IModMetadataService? metadata) && metadata != null)
            {
                int configPages = 0;
                try
                {
                    if (SprocketApi.TryGetService<IModConfigService>(out IModConfigService? configService) && configService != null)
                        configPages = configService.Snapshots.Count;
                }
                catch (Exception exception)
                {
                    LoggerInstance.Warning($"[SMA-UI-ACCEPT] config snapshot unavailable: {exception.Message}");
                }

                LoggerInstance.Msg($"[SMA-UI-ACCEPT] metadata mods={metadata.Entries.Count} config-pages={configPages}");
            }

            if (modMenu == null)
            {
                LoggerInstance.Warning("[SMA-UI-ACCEPT] IModMenuService unavailable; menu self-check skipped.");
                return;
            }

            modMenu.Open();
            LoggerInstance.Msg($"[SMA-UI-ACCEPT] menu-self-check opened={modMenu.IsOpen}");
            menuCloseAtFrame = Time.frameCount + 300;
        }

        public override void OnUpdate()
        {
            if (menuCloseAtFrame <= 0 || Time.frameCount < menuCloseAtFrame)
                return;

            menuCloseAtFrame = -1;
            modMenu?.Close();
            LoggerInstance.Msg($"[SMA-UI-ACCEPT] menu-self-check closed visible={modMenu?.IsOpen}");
        }

        // 声明式配置页的参考实现：四种控件各一个，其中 label / anchor / show 真的驱动测试按钮，
        // 方便在游戏内验证「改配置 → 立即生效 → 重启后仍保留」。
        private void RegisterConfigPage()
        {
            if (!SprocketApi.TryGetService<IModConfigService>(out IModConfigService? resolvedConfig))
            {
                LoggerInstance.Warning("[SMA-UI-ACCEPT] IModConfigService unavailable; config page not registered.");
                return;
            }

            config = resolvedConfig;
            try
            {
                configPage = config!.Register(new ModConfigDefinition
                {
                    DisplayName = "UI Acceptance",
                    Sections = new[]
                    {
                        new ModConfigSectionDefinition { Id = "button", Title = "Test button" }
                    },
                    Entries = new[]
                    {
                        ModConfigEntryDefinition.Toggle("show-test-button", "Show test button", true, sectionId: "button"),
                        ModConfigEntryDefinition.Choice("test-anchor", "Test anchor", "center",
                            new[] { "center", "top", "bottom" }, sectionId: "button"),
                        ModConfigEntryDefinition.Slider("test-scale", "Test scale", 1.0, 0.5, 2.0, 0.1, sectionId: "button"),
                        ModConfigEntryDefinition.Text("test-label", "Test label", "MENU BUTTON TEST", 32, sectionId: "button")
                    }
                });
                config.Changed += OnConfigChanged;
                LoggerInstance.Msg($"[SMA-UI-ACCEPT] config-page-registered entries={configPage.Snapshot.Entries.Count}");
            }
            catch (Exception exception)
            {
                LoggerInstance.Error($"[SMA-UI-ACCEPT] config page registration failed: {exception.Message}");
                configPage = null;
            }
        }

        private void OnConfigChanged(ModConfigChangedEventArgs args)
        {
            if (!string.Equals(args.ModId, "sprocketmodapi-ui-acceptance", StringComparison.Ordinal))
                return;
            LoggerInstance.Msg($"[SMA-UI-ACCEPT] config-changed key={args.Key}");
            ApplyConfigToButton();
        }

        private void ApplyConfigToButton()
        {
            if (configPage == null || menuButton == null || menuButton.IsDisposed)
                return;

            try
            {
                menuButton.Text = configPage.GetText("test-label");
                if (testRoot != null)
                {
                    RectTransform? rect = testRoot.GetComponent<RectTransform>();
                    if (rect != null)
                    {
                        float scale = (float)configPage.GetNumber("test-scale");
                        rect.localScale = new Vector3(scale, scale, 1f);
                        string anchor = configPage.GetText("test-anchor");
                        rect.anchoredPosition = new Vector2(0f, anchor == "top" ? 120f : anchor == "bottom" ? -120f : 0f);
                    }
                }
            }
            catch (Exception exception)
            {
                LoggerInstance.Warning($"[SMA-UI-ACCEPT] applying config to the test button failed: {exception.Message}");
            }
        }

        private void TryCreateControls(UiCapabilitySnapshot status)
        {
            if (scope == null)
                return;
            if (string.Equals(currentScene, "MainMenu", StringComparison.Ordinal) && status.IsMainMenuReady)
            {
                RunMenuSelfCheck();
                if (status.Supports(UiCapability.MenuButton) && !menuButtonAttempted)
                {
                    menuButtonAttempted = true;
                    CreateMenuButton();
                }
            }
        }

        private Transform? CreateAcceptanceParent()
        {
            Transform? parent = FindAcceptanceParent();
            if (parent == null)
            {
                LoggerInstance.Warning("[SMA-UI-ACCEPT] no interactive Canvas found; expected active Canvas with GraphicRaycaster.");
                return null;
            }
            if (testRoot != null)
            {
                RectTransform? existingRoot = testRoot.GetComponent<RectTransform>();
                if (existingRoot != null)
                    return existingRoot;
            }
            testRoot = new GameObject("SMA UI Acceptance Test Root");
            Canvas testCanvas = testRoot.AddComponent<Canvas>();
            testCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            testCanvas.overrideSorting = true;
            testCanvas.sortingOrder = 32767;
            GraphicRaycaster testRaycaster = testRoot.AddComponent<GraphicRaycaster>();
            testRoot.transform.SetParent(parent, false);
            RectTransform testRect = testRoot.GetComponent<RectTransform>();
            testRect.SetAsLastSibling();
            testRect.anchorMin = new Vector2(0.5f, 0.5f);
            testRect.anchorMax = new Vector2(0.5f, 0.5f);
            testRect.pivot = new Vector2(0.5f, 0.5f);
            testRect.sizeDelta = new Vector2(240f, 120f);
            testRect.anchoredPosition = new Vector2(0f, 0f);
            parent = testRect;
            EventSystem? eventSystem = EventSystem.current;
            LoggerInstance.Msg($"[SMA-UI-ACCEPT] input eventSystem={(eventSystem == null ? "none" : eventSystem.name)} module={(eventSystem?.currentInputModule == null ? "none" : eventSystem.currentInputModule.GetType().Name)} canvas={testCanvas.name} raycaster={testRaycaster.isActiveAndEnabled} sorting={testCanvas.sortingOrder} rootActive={testRoot.activeInHierarchy}");
            return parent;
        }

        private void CreateMenuButton()
        {
            Transform? parent = CreateAcceptanceParent();
            if (parent == null || scope == null)
                return;
            UiCreateResult<IUiMenuButtonHandle> menuResult = scope.CreateMenuButtonAsync(new UiMenuButtonDefinition
            {
                Parent = parent,
                Text = "MENU BUTTON TEST",
                Selected = true,
                AnchoredPosition = new Vector2(0f, -30f),
                OnClick = () => LoggerInstance.Msg("[SMA-UI-ACCEPT] menu-button-clicked")
            }).GetAwaiter().GetResult();
            if (menuResult.Succeeded)
            {
                menuButton = menuResult.Value;
                ApplyConfigToButton();
                LoggerInstance.Msg("[SMA-UI-ACCEPT] menu-button-created");
            }
            else
                LoggerInstance.Error($"[SMA-UI-ACCEPT] menu-button-failed code={menuResult.Failure} message={menuResult.Message}");
        }

        public override void OnDeinitializeMelon()
        {
            if (ui != null)
                ui.StatusChanged -= OnUiStatusChanged;
            ui = null;
            if (config != null)
                config.Changed -= OnConfigChanged;
            config = null;
            configPage?.Dispose();
            configPage = null;
            menuButton?.Dispose();
            scope?.Dispose();
            menuButton = null;
            scope = null;
            if (testRoot != null)
                UnityEngine.Object.Destroy(testRoot);
            testRoot = null;
            LoggerInstance.Msg("[SMA-UI-ACCEPT] disposed");
        }

        private void OnUiStatusChanged(object? sender, UiStatusChangedEventArgs args)
        {
            UiCapabilitySnapshot current = args.Current;
            LoggerInstance.Msg($"[SMA-UI-ACCEPT] status previousScene={args.Previous.SceneName} currentScene={current.SceneName} capabilities={current.Available} ready={current.IsMainMenuReady} generation={current.MenuGeneration}");
            TryCreateControls(current);
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            currentScene = sceneName;
            LoggerInstance.Msg($"[SMA-UI-ACCEPT] scene-loaded name={sceneName}");
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            if (testRoot != null)
                UnityEngine.Object.Destroy(testRoot);
            testRoot = null;
            LoggerInstance.Msg($"[SMA-UI-ACCEPT] scene-unloaded name={sceneName}");
        }

        private static Transform? FindAcceptanceParent()
        {
            Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            foreach (Canvas canvas in canvases)
            {
                if (canvas == null || !canvas.isActiveAndEnabled)
                    continue;
                GraphicRaycaster? raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster != null && raycaster.isActiveAndEnabled)
                {
                    MelonLogger.Msg($"[SMA-UI-ACCEPT] parent-canvas name={canvas.name} mode={canvas.renderMode} raycaster={raycaster.enabled}");
                    return canvas.transform;
                }
            }
            return null;
        }
    }
}

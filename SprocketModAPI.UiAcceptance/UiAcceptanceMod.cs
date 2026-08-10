using System;
using MelonLoader;
using SprocketModAPI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[assembly: MelonInfo(typeof(SprocketModAPI.UiAcceptance.UiAcceptanceMod), "Sprocket Mod API UI Acceptance", "0.2.0", "furryAxw")]
[assembly: MelonGame("HD", "Sprocket")]
[assembly: MelonAdditionalDependencies("SprocketModAPI")]

namespace SprocketModAPI.UiAcceptance
{
    public sealed class UiAcceptanceMod : MelonMod
    {
        private IUiScope? scope;
        private IUiMenuButtonHandle? menuButton;
        private IUiService? ui;
        private GameObject? testRoot;
        private bool menuButtonAttempted;
        private string currentScene = "";

        public override void OnInitializeMelon()
        {
            if (!SprocketApi.TryGetService<IUiService>(out IUiService? resolvedUi))
            {
                LoggerInstance.Error("[SMA-UI-ACCEPT] IUiService unavailable.");
                return;
            }

            ui = resolvedUi!;
            ui.StatusChanged += OnUiStatusChanged;
            LoggerInstance.Msg($"[SMA-UI-ACCEPT] capabilities={ui.Capabilities.Available}");
            LoggerInstance.Msg("[SMA-UI-ACCEPT] automatic test: creates one native Menu Button in MainMenu, then waits for a click; scene unload cleanup is observed in API logs.");
            scope = ui.CreateScope(new UiOwnerDefinition { ModId = "sprocketmodapi-ui-acceptance", DisplayName = "UI Acceptance" });
        }

        private void TryCreateControls(UiCapabilitySnapshot status)
        {
            if (scope == null)
                return;
            if (string.Equals(currentScene, "MainMenu", StringComparison.Ordinal)
                && status.IsMainMenuReady && status.Supports(UiCapability.MenuButton) && !menuButtonAttempted)
            {
                menuButtonAttempted = true;
                CreateMenuButton();
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

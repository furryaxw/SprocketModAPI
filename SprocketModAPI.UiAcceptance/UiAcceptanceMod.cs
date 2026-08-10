using System;
using MelonLoader;
using SprocketModAPI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[assembly: MelonInfo(typeof(SprocketModAPI.UiAcceptance.UiAcceptanceMod), "Sprocket Mod API UI Acceptance", "0.1.0", "furryAxw")]
[assembly: MelonGame("HD", "Sprocket")]
[assembly: MelonAdditionalDependencies("SprocketModAPI")]

namespace SprocketModAPI.UiAcceptance
{
    public sealed class UiAcceptanceMod : MelonMod
    {
        private IUiScope? scope;
        private IUiButtonHandle? button;
        private IUiMenuButtonHandle? menuButton;
        private GameObject? testRoot;
        private bool attempted;
        private string currentScene = "";

        public override void OnInitializeMelon()
        {
            if (!SprocketApi.TryGetService<IUiService>(out IUiService? ui))
            {
                LoggerInstance.Error("[SMA-UI-ACCEPT] IUiService unavailable.");
                return;
            }

            LoggerInstance.Msg($"[SMA-UI-ACCEPT] capabilities={ui!.Capabilities.Available}");
            LoggerInstance.Msg("[SMA-UI-ACCEPT] automatic test: creates two controls once in MainMenu, then waits for clicks; scene unload cleanup is observed in API logs.");
            scope = ui.CreateScope(new UiOwnerDefinition { ModId = "sprocketmodapi-ui-acceptance", DisplayName = "UI Acceptance" });
        }

        public override void OnUpdate()
        {
            if (attempted || scope == null || !string.Equals(currentScene, "MainMenu", StringComparison.Ordinal))
                return;
            attempted = true;
            Transform? parent = FindAcceptanceParent();
            if (parent == null)
            {
            LoggerInstance.Warning("[SMA-UI-ACCEPT] no interactive Canvas found; expected active Canvas with GraphicRaycaster.");
                return;
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

            UiCreateResult<IUiButtonHandle> buttonResult = scope.CreateButtonAsync(new UiButtonDefinition
            {
                Parent = parent,
                Text = "UI BUTTON TEST",
                AnchoredPosition = new Vector2(0f, 30f),
                OnClick = () => LoggerInstance.Msg("[SMA-UI-ACCEPT] button-clicked")
            }).GetAwaiter().GetResult();
            if (buttonResult.Succeeded)
            {
                button = buttonResult.Value;
                LoggerInstance.Msg("[SMA-UI-ACCEPT] button-created");
            }
            else
                LoggerInstance.Error($"[SMA-UI-ACCEPT] button-failed code={buttonResult.Failure} message={buttonResult.Message}");

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
            button?.Dispose();
            menuButton?.Dispose();
            scope?.Dispose();
            button = null;
            menuButton = null;
            scope = null;
            if (testRoot != null)
                UnityEngine.Object.Destroy(testRoot);
            testRoot = null;
            LoggerInstance.Msg("[SMA-UI-ACCEPT] disposed");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            attempted = !string.Equals(sceneName, "MainMenu", StringComparison.Ordinal)
                || (menuButton != null && !menuButton.IsDisposed);
            currentScene = sceneName;
            button = null;
            LoggerInstance.Msg($"[SMA-UI-ACCEPT] scene-loaded name={sceneName}");
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            if (testRoot != null)
                UnityEngine.Object.Destroy(testRoot);
            testRoot = null;
            button = null;
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

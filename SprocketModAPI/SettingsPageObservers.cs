using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SprocketModAPI
{
    internal sealed partial class InputService
    {
        private GameObject? observedSettingsContent;
        private GameObject? observedKeymapping;
        private RectTransform? observedActionButtons;
        private bool entryAlignmentReady;

        internal void InitializeSettingsPageListeners()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && string.Equals(scene.name, "SettingsMenu", StringComparison.Ordinal))
                    AttachSettingsContentListener(scene);
            }
        }

        internal void NotifySceneLoadedForUi(string sceneName)
        {
            if (!string.Equals(sceneName, "SettingsMenu", StringComparison.Ordinal))
                return;

            AttachSettingsContentListener(SceneManager.GetSceneByName("SettingsMenu"));
        }

        private void AttachSettingsContentListener(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                warn("[SMA] SettingsMenu scene was not ready; the mod keybinding entry is unavailable.");
                return;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            GameObject? settingsRoot = roots.FirstOrDefault(root => root != null && string.Equals(root.name, "Settings Menu", StringComparison.Ordinal));
            if (settingsRoot == null)
            {
                warn("[SMA] Settings Menu root was not found; the mod keybinding entry is unavailable.");
                return;
            }

            AttachActionButtonsAlignmentListener(settingsRoot);

            Transform contentTransform = settingsRoot.transform.Find("Content/Content");
            if (contentTransform == null || contentTransform.gameObject == null)
            {
                warn("[SMA] Settings Menu/Content/Content was not found; the mod keybinding entry is unavailable.");
                return;
            }

            GameObject content = contentTransform.gameObject;
            if (observedSettingsContent != null && observedSettingsContent.GetInstanceID() == content.GetInstanceID())
                return;

            observedSettingsContent = content;
            if (content.GetComponent<ContentHierarchyWatcher>() == null)
                content.AddComponent<ContentHierarchyWatcher>();

            NotifySettingsContentChanged(contentTransform);
        }

        private void AttachActionButtonsAlignmentListener(GameObject settingsRoot)
        {
            Transform actionButtonsTransform = settingsRoot.transform.Find("Content/Action buttons");
            if (actionButtonsTransform == null)
            {
                entryAlignmentReady = false;
                warn("[SMA] Settings Menu/Content/Action buttons was not found; the mod keybinding entry is unavailable.");
                return;
            }

            RectTransform actionButtons = actionButtonsTransform.GetComponent<RectTransform>();
            if (actionButtons == null)
            {
                entryAlignmentReady = false;
                warn("[SMA] Settings Menu action buttons has no RectTransform; the mod keybinding entry is unavailable.");
                return;
            }

            observedActionButtons = actionButtons;
            if (actionButtonsTransform.GetComponent<ActionButtonsAlignmentWatcher>() == null)
                actionButtonsTransform.gameObject.AddComponent<ActionButtonsAlignmentWatcher>();

            ApplyObservedActionButtonsAlignment();
        }

        internal void NotifyActionButtonsRectChanged(RectTransform actionButtons)
        {
            if (actionButtons == null)
                return;

            observedActionButtons = actionButtons;
            ApplyObservedActionButtonsAlignment();
        }

        internal void NotifyActionButtonsDestroyed()
        {
            observedActionButtons = null;
            entryAlignmentReady = false;
            if (uiEntryObject != null)
                uiEntryObject.SetActive(false);
        }

        private void ApplyObservedActionButtonsAlignment()
        {
            if (observedActionButtons == null || uiCanvas == null || uiRoot == null || uiEntryRect == null)
                return;

            try
            {
                Canvas sourceCanvas = observedActionButtons.GetComponentInParent<Canvas>();
                Camera? sourceCamera = sourceCanvas != null && sourceCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? sourceCanvas.worldCamera
                    : null;

                Rect sourceRect = observedActionButtons.rect;
                Vector3 leftWorld = observedActionButtons.TransformPoint(new Vector3(sourceRect.xMin, sourceRect.yMin, 0f));
                Vector2 leftScreen = RectTransformUtility.WorldToScreenPoint(sourceCamera, leftWorld);
                RectTransform rootRect = uiRoot.GetComponent<RectTransform>();
                if (rootRect == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, leftScreen, null, out Vector2 localPoint))
                {
                    entryAlignmentReady = false;
                    warn("[SMA] Failed to align the mod keybinding entry to the native settings UI.");
                    return;
                }

                Vector2 entryPosition = uiEntryRect.anchoredPosition;
                entryPosition.x = localPoint.x - rootRect.rect.xMin;
                entryPosition.y = 84f;
                uiEntryRect.anchoredPosition = entryPosition;
                entryAlignmentReady = true;
            }
            catch (Exception exception)
            {
                entryAlignmentReady = false;
                warn($"[SMA] Failed to align the mod keybinding entry: {exception.Message}");
            }
        }

        internal void NotifySettingsContentChanged(Transform content)
        {
            if (content == null)
                return;

            Transform keymappingTransform = content.Find("Keymapping");
            if (keymappingTransform == null || keymappingTransform.gameObject == null)
            {
                observedKeymapping = null;
                SetKeymappingPageActive(false);
                return;
            }

            GameObject keymapping = keymappingTransform.gameObject;
            observedKeymapping = keymapping;
            if (keymapping.GetComponent<KeymappingActivationWatcher>() == null)
                keymapping.AddComponent<KeymappingActivationWatcher>();

            SetKeymappingPageActive(keymapping.activeSelf);
        }

        internal void NotifyKeymappingActivationChanged(GameObject keymapping, bool enabledEvent)
        {
            if (keymapping == null)
                return;

            observedKeymapping = keymapping;
            SetKeymappingPageActive(enabledEvent);
        }

        internal void NotifySettingsContentDestroyed()
        {
            observedSettingsContent = null;
            observedKeymapping = null;
            SetKeymappingPageActive(false);
        }

        internal void NotifyKeymappingDestroyed()
        {
            observedKeymapping = null;
            SetKeymappingPageActive(false);
        }

        private void SetKeymappingPageActive(bool active)
        {
            if (keymappingPageActive == active)
                return;

            keymappingPageActive = active;
            if (!active)
            {
                uiVisible = false;
                uiSearchInput?.DeactivateInputField();
                CancelCapture();
                if (uiRoot != null)
                    uiRoot.SetActive(false);
            }
        }
    }

    public sealed class ContentHierarchyWatcher : MonoBehaviour
    {
        private void OnEnable() => ApiMod.Service?.NotifySettingsContentChanged(transform);
        private void OnTransformChildrenChanged() => ApiMod.Service?.NotifySettingsContentChanged(transform);
        private void OnDestroy() => ApiMod.Service?.NotifySettingsContentDestroyed();
    }

    public sealed class KeymappingActivationWatcher : MonoBehaviour
    {
        private void OnEnable() => ApiMod.Service?.NotifyKeymappingActivationChanged(gameObject, true);
        private void OnDisable() => ApiMod.Service?.NotifyKeymappingActivationChanged(gameObject, false);
        private void OnDestroy() => ApiMod.Service?.NotifyKeymappingDestroyed();
    }

    public sealed class ActionButtonsAlignmentWatcher : MonoBehaviour
    {
        private void OnEnable() => Notify();
        private void OnRectTransformDimensionsChange() => Notify();
        private void OnTransformParentChanged() => Notify();
        private void OnDestroy() => ApiMod.Service?.NotifyActionButtonsDestroyed();

        private void Notify()
        {
            RectTransform rect = GetComponent<RectTransform>();
            if (rect != null)
                ApiMod.Service?.NotifyActionButtonsRectChanged(rect);
        }
    }
}

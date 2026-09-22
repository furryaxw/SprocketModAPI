using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SprocketModAPI
{
    internal sealed partial class KeybindingUiController
    {
        private GameObject? observedSettingsContent;
        private GameObject? observedSettingsRoot;
        private GameObject? observedKeymapping;
        private RectTransform? observedActionButtons;
        private readonly HashSet<int> activePauseMenus = new();
        private bool entryAlignmentReady;

        // 入口与原生按钮顶边之间的间隙（屏幕像素）。
        private const float EntryGapAboveRow = 50f;

        internal void InitializeSettingsPageListeners()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;
                AttachPauseMenuListeners(scene);
                if (string.Equals(scene.name, "SettingsMenu", StringComparison.Ordinal))
                    AttachSettingsContentListener(scene);
            }
        }

        internal void NotifySceneLoaded(string sceneName)
        {
            Scene loadedScene = SceneManager.GetSceneByName(sceneName);
            AttachPauseMenuListeners(loadedScene);
            if (!string.Equals(sceneName, "SettingsMenu", StringComparison.Ordinal))
                return;

            AttachSettingsContentListener(loadedScene);
        }

        private void AttachPauseMenuListeners(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate == null || candidate.gameObject == null || !IsPauseMenuObject(candidate.name))
                        continue;
                    if (candidate.GetComponent<PauseMenuActivationWatcher>() == null)
                        candidate.gameObject.AddComponent<PauseMenuActivationWatcher>();
                    if (candidate.gameObject.activeInHierarchy)
                        NotifyPauseMenuActivationChanged(candidate.gameObject, true);
                }
            }
        }

        private static bool IsPauseMenuObject(string name)
            => name.Contains("PauseMenu", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Pause Menu", StringComparison.OrdinalIgnoreCase)
                || name.Contains("EscapeMenu", StringComparison.OrdinalIgnoreCase);

        internal void NotifyPauseMenuActivationChanged(GameObject pauseMenu, bool active)
        {
            if (pauseMenu == null)
                return;
            int instanceId = pauseMenu.GetInstanceID();
            if (active)
                activePauseMenus.Add(instanceId);
            else
                activePauseMenus.Remove(instanceId);
            input.NotifyPauseMenuActive(activePauseMenus.Count != 0);
        }

        internal void NotifySettingsMenuActivationChanged(bool active)
        {
            input.NotifySettingsPageActive(active);
        }

        internal void NotifySceneUnloaded(string sceneName)
        {
            if (!string.Equals(sceneName, "SettingsMenu", StringComparison.Ordinal))
                return;

            ReleaseNativeInputLease();
            observedSettingsContent = null;
            observedSettingsRoot = null;
            observedKeymapping = null;
            observedActionButtons = null;
            entryAlignmentReady = false;
            SetKeymappingPageActive(false);
        }

        private void AttachSettingsContentListener(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                log.Warn("[SMA-KEY] SettingsMenu scene was not ready; the mod keybinding entry is unavailable.");
                return;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            GameObject? settingsRoot = roots.FirstOrDefault(root => root != null && string.Equals(root.name, "Settings Menu", StringComparison.Ordinal));
            if (settingsRoot == null)
            {
                log.Warn("[SMA-KEY] Settings Menu root was not found; the mod keybinding entry is unavailable.");
                return;
            }

            if (settingsRoot.GetComponent<SettingsMenuActivationWatcher>() == null)
                settingsRoot.AddComponent<SettingsMenuActivationWatcher>();
            observedSettingsRoot = settingsRoot;
            input.NotifySettingsPageActive(settingsRoot.activeInHierarchy);

            AttachActionButtonsAlignmentListener(settingsRoot);

            Transform contentTransform = settingsRoot.transform.Find("Content/Content");
            if (contentTransform == null || contentTransform.gameObject == null)
            {
                log.Warn("[SMA-KEY] Settings Menu/Content/Content was not found; the mod keybinding entry is unavailable.");
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
                log.Warn("[SMA-KEY] Settings Menu/Content/Action buttons was not found; the mod keybinding entry is unavailable.");
                return;
            }

            RectTransform actionButtons = actionButtonsTransform.GetComponent<RectTransform>();
            if (actionButtons == null)
            {
                entryAlignmentReady = false;
                log.Warn("[SMA-KEY] Settings Menu action buttons has no RectTransform; the mod keybinding entry is unavailable.");
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

        // 切页会把「Action buttons」整排换掉（旧的可能被销毁或停用），缓存会指向上一页那排，
        // 于是入口停在上一个页面的位置，直到事件把引用刷新。每帧确认引用是否仍指向活着的对象。
        private void EnsureActionButtons()
        {
            if (observedActionButtons != null && observedActionButtons.gameObject.activeInHierarchy)
                return;
            if (observedSettingsRoot == null)
                return;

            AttachActionButtonsAlignmentListener(observedSettingsRoot);
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
                RectTransform rootRect = uiRoot.GetComponent<RectTransform>();
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
                    log.Warn("[SMA-KEY] Failed to align the mod keybinding entry to the native settings UI.");
                    return;
                }

                float width = MathF.Abs(localTopRight.x - localBottomLeft.x);
                float height = MathF.Abs(localTopRight.y - localBottomLeft.y);
                if (width > 0f && height > 0f)
                    uiEntryRect.sizeDelta = new Vector2(width, height);

                Vector2 entryPosition = uiEntryRect.anchoredPosition;
                entryPosition.x = localTarget.x - rootRect.rect.xMin;
                entryPosition.y = localTarget.y - rootRect.rect.yMin;
                uiEntryRect.anchoredPosition = entryPosition;

                float canvasScale = sizeSource.rect.width > 0f && width > 0f ? width / sizeSource.rect.width : 1f;
                ApplyNativeLabelFit(canvasScale);
                ApplyNativeEntryAppearance();
                entryAlignmentReady = true;
            }
            catch (Exception exception)
            {
                entryAlignmentReady = false;
                log.Warn($"[SMA-KEY] Failed to align the mod keybinding entry: {exception.Message}");
            }
        }

        // 原生矩形上的一个点 → 屏幕坐标，再换算到本画布的本地坐标。
        private static Vector2 ToScreen(Camera? camera, RectTransform source, Vector2 localPoint)
            => RectTransformUtility.WorldToScreenPoint(camera, source.TransformPoint(new Vector3(localPoint.x, localPoint.y, 0f)));

        private static bool TryScreenToLocal(RectTransform rootRect, Vector2 screen, out Vector2 local)
            => RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, screen, null, out local);

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

        // 字号跟着按钮一起换算：两个字号的"像素大小"取决于各自画布的缩放，直接抄原生字号会大一大截。
        // 打开自动缩放，上限就是换算后的原生字号——标签比原生按钮的名字长时自动缩小，不会溢出。
        private void ApplyNativeLabelFit(float canvasScale)
        {
            if (uiEntryLabel == null)
                return;

            Selectable? native = observedActionButtons == null ? null : FindNativeSelectable(observedActionButtons);
            Il2CppTMPro.TextMeshProUGUI? nativeLabel = native == null
                ? null
                : native.GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
            float maxFontSize = nativeLabel != null && nativeLabel.fontSize > 0f
                ? nativeLabel.fontSize * MathF.Max(canvasScale, 0.1f)
                : 12f;

            uiEntryLabel.enableAutoSizing = true;
            uiEntryLabel.fontSizeMin = MathF.Min(9f, maxFontSize);
            uiEntryLabel.fontSizeMax = maxFontSize;
        }

        // 入口观感照抄原生按钮：底图留白、颜色只放 ColorBlock（两层都塞灰会被 UGUI 相乘成"灰的平方"）。
        private void ApplyNativeEntryAppearance()
        {
            if (observedActionButtons == null || uiEntryObject == null)
                return;

            try
            {
                Selectable? native = FindNativeSelectable(observedActionButtons);
                Image? image = uiEntryObject.GetComponent<Image>();
                Button? button = uiEntryObject.GetComponent<Button>();
                if (native == null || image == null || button == null)
                    return;

                // 原生按钮自身的状态色可用时照抄它（底图必须留白，否则两层颜色会相乘）；
                // 不可用就保持入口创建时的纯色，不去抄原生底图。
                ColorBlock nativeColors = native.colors;
                if (nativeColors.normalColor.a > 0.01f)
                {
                    image.color = Color.white;
                    ColorBlock colors = button.colors;
                    colors.normalColor = nativeColors.normalColor;
                    colors.highlightedColor = nativeColors.highlightedColor;
                    colors.pressedColor = nativeColors.pressedColor;
                    colors.selectedColor = nativeColors.normalColor;
                    colors.disabledColor = nativeColors.pressedColor;
                    colors.colorMultiplier = 1f;
                    colors.fadeDuration = nativeColors.fadeDuration;
                    button.colors = colors;
                }

                Il2CppTMPro.TextMeshProUGUI? nativeLabel = native.GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>(true);
                if (uiEntryLabel != null && nativeLabel != null)
                    uiEntryLabel.color = nativeLabel.color;
            }
            catch (Exception exception)
            {
                log.Warn($"[SMA-KEY] failed to copy the native entry appearance: {exception.Message}");
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
                ReleaseNativeInputLease();
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
        private void OnEnable() => KeybindingsModule.Controller?.NotifySettingsContentChanged(transform);
        private void OnTransformChildrenChanged() => KeybindingsModule.Controller?.NotifySettingsContentChanged(transform);
        private void OnDestroy() => KeybindingsModule.Controller?.NotifySettingsContentDestroyed();
    }

    public sealed class KeymappingActivationWatcher : MonoBehaviour
    {
        private void OnEnable() => KeybindingsModule.Controller?.NotifyKeymappingActivationChanged(gameObject, true);
        private void OnDisable() => KeybindingsModule.Controller?.NotifyKeymappingActivationChanged(gameObject, false);
        private void OnDestroy() => KeybindingsModule.Controller?.NotifyKeymappingDestroyed();
    }

    public sealed class ActionButtonsAlignmentWatcher : MonoBehaviour
    {
        private void OnEnable() => Notify();
        private void OnRectTransformDimensionsChange() => Notify();
        private void OnTransformParentChanged() => Notify();
        private void OnDestroy() => KeybindingsModule.Controller?.NotifyActionButtonsDestroyed();

        private void Notify()
        {
            RectTransform rect = GetComponent<RectTransform>();
            if (rect != null)
                KeybindingsModule.Controller?.NotifyActionButtonsRectChanged(rect);
        }
    }

    public sealed class PauseMenuActivationWatcher : MonoBehaviour
    {
        private void OnEnable() => KeybindingsModule.Controller?.NotifyPauseMenuActivationChanged(gameObject, true);
        private void OnDisable() => KeybindingsModule.Controller?.NotifyPauseMenuActivationChanged(gameObject, false);
        private void OnDestroy() => KeybindingsModule.Controller?.NotifyPauseMenuActivationChanged(gameObject, false);
    }

    public sealed class SettingsMenuActivationWatcher : MonoBehaviour
    {
        private void OnEnable() => KeybindingsModule.Controller?.NotifySettingsMenuActivationChanged(true);
        private void OnDisable() => KeybindingsModule.Controller?.NotifySettingsMenuActivationChanged(false);
        private void OnDestroy() => KeybindingsModule.Controller?.NotifySettingsMenuActivationChanged(false);
    }
}

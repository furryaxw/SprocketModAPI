using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SprocketModAPI
{
    internal sealed partial class KeybindingUiController
    {
        private NativeSettingsInputLease? nativeInputLease;
        private bool nativeLeaseUnavailableLogged;

        private void UpdateNativeInputLease()
        {
            bool shouldOwnInput = keymappingPageActive && (uiVisible || captureAction != null);
            if (shouldOwnInput == (nativeInputLease != null))
                return;

            if (!shouldOwnInput)
            {
                ReleaseNativeInputLease();
                return;
            }

            if (observedKeymapping == null)
            {
                if (!nativeLeaseUnavailableLogged)
                {
                    nativeLeaseUnavailableLogged = true;
                    warn("[SMA] Native keymapping input could not be isolated because the page root is unavailable.");
                }
                return;
            }

            try
            {
                nativeInputLease = NativeSettingsInputLease.Acquire(observedKeymapping);
                nativeLeaseUnavailableLogged = false;
            }
            catch (Exception exception)
            {
                warn($"[SMA] Native keymapping input isolation failed: {exception}");
                ReleaseNativeInputLease();
            }
        }

        private void ReleaseNativeInputLease()
        {
            NativeSettingsInputLease? lease = nativeInputLease;
            nativeInputLease = null;
            nativeLeaseUnavailableLogged = false;
            if (lease == null)
                return;

            try
            {
                lease.Dispose();
            }
            catch (Exception exception)
            {
                warn($"[SMA] Native keymapping input restoration failed: {exception}");
            }
        }
    }

    internal sealed class NativeSettingsInputLease : IDisposable
    {
        private readonly List<SelectableSnapshot> selectables;
        private readonly EventSystem? eventSystem;
        private readonly GameObject? selectedObject;
        private readonly bool sendNavigationEvents;
        private bool disposed;

        private NativeSettingsInputLease(List<SelectableSnapshot> selectables, EventSystem? eventSystem,
            GameObject? selectedObject, bool sendNavigationEvents)
        {
            this.selectables = selectables;
            this.eventSystem = eventSystem;
            this.selectedObject = selectedObject;
            this.sendNavigationEvents = sendNavigationEvents;
        }

        internal static NativeSettingsInputLease Acquire(GameObject keymappingRoot)
        {
            if (keymappingRoot == null)
                throw new ArgumentNullException(nameof(keymappingRoot));

            var snapshots = new List<SelectableSnapshot>();
            foreach (Selectable selectable in keymappingRoot.GetComponentsInChildren<Selectable>(true))
            {
                if (selectable == null)
                    continue;
                snapshots.Add(new SelectableSnapshot(selectable, selectable.interactable, selectable.navigation));
            }

            EventSystem? current = EventSystem.current;
            GameObject? selected = current?.currentSelectedGameObject;
            bool navigation = current?.sendNavigationEvents ?? false;
            var lease = new NativeSettingsInputLease(snapshots, current, selected, navigation);
            try
            {
                foreach (SelectableSnapshot snapshot in snapshots)
                    snapshot.Disable();
                if (current != null)
                {
                    current.sendNavigationEvents = false;
                    current.SetSelectedGameObject(null);
                }
                return lease;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            Exception? failure = null;
            foreach (SelectableSnapshot snapshot in selectables)
            {
                try { snapshot.Restore(); }
                catch (Exception exception) { failure ??= exception; }
            }

            try
            {
                if (eventSystem != null)
                {
                    eventSystem.sendNavigationEvents = sendNavigationEvents;
                    if (selectedObject != null && selectedObject.activeInHierarchy)
                        eventSystem.SetSelectedGameObject(selectedObject);
                }
            }
            catch (Exception exception) { failure ??= exception; }
            selectables.Clear();
            if (failure != null)
                throw failure;
        }

        private readonly struct SelectableSnapshot
        {
            private readonly Selectable selectable;
            private readonly bool interactable;
            private readonly Navigation navigation;

            internal SelectableSnapshot(Selectable selectable, bool interactable, Navigation navigation)
            {
                this.selectable = selectable;
                this.interactable = interactable;
                this.navigation = navigation;
            }

            internal void Disable()
            {
                if (selectable == null)
                    return;
                Navigation disabledNavigation = selectable.navigation;
                disabledNavigation.mode = Navigation.Mode.None;
                selectable.navigation = disabledNavigation;
                selectable.interactable = false;
            }

            internal void Restore()
            {
                if (selectable == null)
                    return;
                selectable.navigation = navigation;
                selectable.interactable = interactable;
            }
        }
    }
}

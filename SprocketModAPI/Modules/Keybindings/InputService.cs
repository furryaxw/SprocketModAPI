using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2CppSprocket.SettingConfiguration;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SprocketModAPI
{
    internal sealed class InputService : IInputService, IDisposable
    {
        private readonly Action<string> warn;
        private readonly Action<string> error;
        private readonly Dictionary<string, ActionState> actions = new();
        private readonly Dictionary<string, Dictionary<string, string?>> retained = new();
        private readonly List<InputBlock> blocks = new();
        private readonly KeybindingStore store;
        private readonly BindingConflictIndex conflictIndex = new();
        private readonly NativeInputBindingReader nativeBindingReader;
        private IReadOnlyList<NativeBindingSnapshot> nativeBindings = Array.Empty<NativeBindingSnapshot>();
        private InputContextMask context = InputContextMask.OtherMenu;
        private bool disposed;

        internal InputService(Action<string> warn, Action<string> error)
        {
            this.warn = warn;
            this.error = error;
            nativeBindingReader = new NativeInputBindingReader(warn);
            string filePath = Path.Combine(MelonEnvironment.UserDataDirectory, "SprocketModAPI", "keybindings.json");
            store = new KeybindingStore(filePath, warn);
            foreach (var item in store.Load())
                retained[item.Key] = item.Value;
        }

        public event Action<IReadOnlyList<ModActionDefinition>>? ActionsChanged;
        internal event Action? InternalActionsChanged;
        internal IReadOnlyCollection<ActionState> Actions => actions.Values;

        public IInputActionHandle RegisterAction(ModActionDefinition definition)
        {
            ThrowIfDisposed();
            Validate(definition);
            if (actions.ContainsKey(definition.StableId))
                throw new InvalidOperationException($"Duplicate action ID: {definition.StableId}");

            var state = new ActionState(definition, error, LoadBinding(definition.StableId), BindingsChanged, RemoveAction);
            actions.Add(definition.StableId, state);
            NotifyActionsChanged();
            return state;
        }

        public IDisposable AcquireInputBlock(object owner, string reason)
        {
            ThrowIfDisposed();
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("An input block reason is required.", nameof(reason));
            var block = new InputBlock(this);
            blocks.Add(block);
            return block;
        }

        internal void Update(bool externallyBlocked)
        {
            if (disposed)
                return;

            context = DetectContext();
            if (!Application.isFocused || externallyBlocked || blocks.Count != 0
                || context == InputContextMask.Settings || context == InputContextMask.TextInput)
            {
                foreach (ActionState action in actions.Values)
                    action.Suppress();
                return;
            }

            foreach (ActionState action in actions.Values)
                action.Update(context);
        }

        internal void NotifySceneChanged()
        {
            context = DetectContext();
            foreach (ActionState action in actions.Values)
                action.Suppress();
            RefreshNativeBindings();
        }

        internal void InitializeNativeBindings() => RefreshNativeBindings();

        internal IReadOnlyList<BindingConflictInfo> GetConflicts(ActionState action)
            => conflictIndex.GetForAction(action.Definition.StableId);

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            Save();
            foreach (ActionState action in actions.Values.ToArray())
                action.Detach();
            actions.Clear();
            blocks.Clear();
            ActionsChanged = null;
            InternalActionsChanged = null;
        }

        private void Remove(InputBlock block) => blocks.Remove(block);

        private void RemoveAction(ActionState state)
        {
            retained[state.Definition.StableId] = state.Serialize();
            if (!actions.Remove(state.Definition.StableId))
                return;
            Save();
            NotifyActionsChanged();
        }

        private void NotifyActionsChanged()
        {
            RebuildConflictIndex();
            ModActionDefinition[] snapshot = actions.Values.Select(action => action.Definition).ToArray();
            ActionsChanged?.Invoke(snapshot);
            InternalActionsChanged?.Invoke();
        }

        private void BindingsChanged()
        {
            Save();
            RebuildConflictIndex();
            InternalActionsChanged?.Invoke();
        }

        internal void RefreshNativeBindings()
        {
            nativeBindings = nativeBindingReader.ReadLoadedAssets();
            RebuildConflictIndex();
            InternalActionsChanged?.Invoke();
        }

        private void RebuildConflictIndex()
        {
            conflictIndex.Rebuild(actions.Values.Select(action => new ModBindingSnapshot(
                action.Definition.StableId, action.Definition.DisplayName, action.Primary, action.Secondary)), nativeBindings);
        }

        private static InputContextMask DetectContext()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            if (sceneName.Contains("Settings", StringComparison.OrdinalIgnoreCase))
                return InputContextMask.Settings;
            SettingsMenu? settings = UnityEngine.Object.FindObjectOfType<SettingsMenu>();
            if (settings != null && settings.gameObject != null && settings.gameObject.activeInHierarchy)
                return InputContextMask.Settings;
            if (sceneName.Contains("Designer", StringComparison.OrdinalIgnoreCase))
                return InputContextMask.Designer;
            if (sceneName.Contains("Menu", StringComparison.OrdinalIgnoreCase))
                return InputContextMask.MainMenu;
            return sceneName.Contains("VehicleControl", StringComparison.OrdinalIgnoreCase)
                ? InputContextMask.Gameplay
                : InputContextMask.OtherMenu;
        }

        private static void Validate(ModActionDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(definition.ModId) || string.IsNullOrWhiteSpace(definition.ActionId)
                || definition.ModId.Contains(':') || definition.ActionId.Contains(':'))
                throw new ArgumentException("modId and actionId must be non-empty and cannot contain ':'.");
            if (definition.DefaultPrimary.IsEmpty && definition.DefaultSecondary.IsEmpty)
                throw new ArgumentException("At least one default binding is required.");
        }

        private Dictionary<string, string?> LoadBinding(string id)
            => retained.TryGetValue(id, out Dictionary<string, string?>? value) ? value : new();

        private void Save()
        {
            var data = new Dictionary<string, Dictionary<string, string?>>(retained, StringComparer.Ordinal);
            foreach (var action in actions)
                data[action.Key] = action.Value.Serialize();
            store.Save(data);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(InputService));
        }

        private sealed class InputBlock : IDisposable
        {
            private InputService? service;
            internal InputBlock(InputService service) => this.service = service;
            public void Dispose()
            {
                InputService? owner = service;
                service = null;
                if (owner != null)
                    owner.Remove(this);
            }
        }
    }

    internal sealed class BindingSlots
    {
        private readonly KeyChord defaultPrimary;
        private readonly KeyChord defaultSecondary;

        internal BindingSlots(KeyChord defaultPrimary, KeyChord defaultSecondary, KeyChord primary, KeyChord secondary)
        {
            this.defaultPrimary = defaultPrimary;
            this.defaultSecondary = defaultSecondary;
            Primary = primary;
            Secondary = secondary;
            Normalize();
        }

        internal KeyChord Primary { get; private set; }
        internal KeyChord Secondary { get; private set; }

        internal void SetBinding(int slot, KeyChord? binding)
        {
            if (slot == 0) Primary = binding ?? default;
            else if (slot == 1) Secondary = binding ?? default;
            else throw new ArgumentOutOfRangeException(nameof(slot), "Only binding slots 0 and 1 are supported.");
            Normalize();
        }

        internal void RestoreDefaults()
        {
            Primary = defaultPrimary;
            Secondary = defaultSecondary;
            Normalize();
        }

        private void Normalize()
        {
            if (!Primary.IsEmpty && Primary == Secondary)
                Secondary = default;
        }
    }

    internal sealed class ActionState : IInputActionHandle
    {
        public readonly ModActionDefinition Definition;
        private readonly Action<string> error;
        private readonly Action changed;
        private readonly Action<ActionState> remove;
        private readonly BindingSlots bindings;
        private bool previous;
        private bool pressed;
        private bool released;
        private bool enabled = true;
        private bool disposed;
        private InputAction? primaryAction;
        private InputAction? secondaryAction;
        private float pressedAt;

        internal ActionState(ModActionDefinition definition, Action<string> error,
            Dictionary<string, string?> overrides, Action changed, Action<ActionState> remove)
        {
            Definition = definition;
            this.error = error;
            this.changed = changed;
            this.remove = remove;
            bindings = new BindingSlots(definition.DefaultPrimary, definition.DefaultSecondary,
                Resolve(overrides, "primary", definition.DefaultPrimary),
                Resolve(overrides, "secondary", definition.DefaultSecondary));
            Rebuild();
        }

        public event Action? Pressed;
        public event Action? Released;
        public event Action? Tapped;
        public bool WasPressedThisFrame => pressed;
        public bool WasReleasedThisFrame => released;
        public bool IsPressed => previous;
        public bool Enabled { get => enabled; set => enabled = value; }
        public KeyChord Primary => bindings.Primary;
        public KeyChord Secondary => bindings.Secondary;

        internal void Update(InputContextMask context)
        {
            pressed = released = false;
            bool allowed;
            try
            {
                allowed = enabled && (Definition.Contexts & context) != 0 && (Definition.Gate?.Invoke() ?? true);
            }
            catch (Exception exception)
            {
                error($"[SMA] {Definition.StableId} gate failed: {exception}");
                allowed = false;
            }

            bool now = allowed && (ChordPressed(bindings.Primary, primaryAction) || ChordPressed(bindings.Secondary, secondaryAction));
            if (!previous && now)
            {
                pressed = true;
                pressedAt = Time.unscaledTime;
                SafeInvoke(Pressed);
            }
            if (previous && !now)
            {
                released = true;
                SafeInvoke(Released);
                if (Time.unscaledTime - pressedAt <= InputSystem.settings.defaultTapTime)
                    SafeInvoke(Tapped);
            }
            previous = now;
        }

        internal void Suppress()
        {
            if (previous)
            {
                previous = false;
                released = true;
                SafeInvoke(Released);
            }
            pressed = false;
        }

        public void SetBinding(int slot, KeyChord? binding)
        {
            bindings.SetBinding(slot, binding);
            Rebuild();
            changed();
        }

        public void RestoreDefaults()
        {
            bindings.RestoreDefaults();
            Rebuild();
            changed();
        }

        public void Unregister() => Dispose();
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Detach();
            remove(this);
        }

        internal void Detach()
        {
            primaryAction?.Disable();
            primaryAction?.Dispose();
            secondaryAction?.Disable();
            secondaryAction?.Dispose();
            primaryAction = secondaryAction = null;
        }

        internal Dictionary<string, string?> Serialize()
        {
            var value = new Dictionary<string, string?>();
            if (bindings.Primary != Definition.DefaultPrimary) value["primary"] = KeyChordCodec.Format(bindings.Primary);
            if (bindings.Secondary != Definition.DefaultSecondary) value["secondary"] = KeyChordCodec.Format(bindings.Secondary);
            return value;
        }

        private void Rebuild()
        {
            Detach();
            primaryAction = Build(bindings.Primary, "primary");
            secondaryAction = Build(bindings.Secondary, "secondary");
        }

        private InputAction? Build(KeyChord chord, string slot)
        {
            if (chord.IsEmpty) return null;
            var value = new InputAction($"{Definition.StableId}:{slot}");
            value.AddBinding(chord.RawControlPath);
            value.Enable();
            return value;
        }

        private void SafeInvoke(Action? callback)
        {
            try { callback?.Invoke(); }
            catch (Exception exception) { error($"[SMA] {Definition.StableId} callback failed: {exception}"); }
        }

        private static KeyChord Resolve(Dictionary<string, string?> overrides, string key, KeyChord fallback)
        {
            if (!overrides.TryGetValue(key, out string? value)) return fallback;
            if (value == null) return default;
            return KeyChordCodec.TryParse(value, out KeyChord chord) ? chord : fallback;
        }

        private static bool ChordPressed(KeyChord chord, InputAction? action)
        {
            if (chord.IsEmpty || action == null || !action.IsPressed()) return false;
            ModifierKeys actual = CurrentModifiers();
            actual &= ~PrimaryModifier(chord.RawControlPath);
            return actual == chord.Modifiers;
        }

        private static ModifierKeys CurrentModifiers()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null) return ModifierKeys.None;
            ModifierKeys modifiers = ModifierKeys.None;
            if (keyboard.leftShiftKey.isPressed) modifiers |= ModifierKeys.LeftShift;
            if (keyboard.rightShiftKey.isPressed) modifiers |= ModifierKeys.RightShift;
            if (keyboard.leftCtrlKey.isPressed) modifiers |= ModifierKeys.LeftCtrl;
            if (keyboard.rightCtrlKey.isPressed) modifiers |= ModifierKeys.RightCtrl;
            if (keyboard.leftAltKey.isPressed) modifiers |= ModifierKeys.LeftAlt;
            if (keyboard.rightAltKey.isPressed) modifiers |= ModifierKeys.RightAlt;
            return modifiers;
        }

        private static ModifierKeys PrimaryModifier(string path) => path switch
        {
            "<keyboard>/leftshift" => ModifierKeys.LeftShift,
            "<keyboard>/rightshift" => ModifierKeys.RightShift,
            "<keyboard>/leftctrl" => ModifierKeys.LeftCtrl,
            "<keyboard>/rightctrl" => ModifierKeys.RightCtrl,
            "<keyboard>/leftalt" => ModifierKeys.LeftAlt,
            "<keyboard>/rightalt" => ModifierKeys.RightAlt,
            _ => ModifierKeys.None
        };
    }
}

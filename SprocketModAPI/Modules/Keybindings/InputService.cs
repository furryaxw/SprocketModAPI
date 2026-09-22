using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MelonLoader.Utils;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
        private readonly KeybindingDebugLog debug;
        private readonly BindingConflictIndex conflictIndex = new();
        private readonly NativeInputBindingReader nativeBindingReader;
        private readonly HashSet<string> loadedScenes = new(StringComparer.OrdinalIgnoreCase);
        private readonly InputRouteState route = new();
        private IReadOnlyList<NativeBindingSnapshot> nativeBindings = Array.Empty<NativeBindingSnapshot>();
        private InputContextMask context = InputContextMask.OtherMenu;
        private bool settingsPageActive;
        private bool pauseMenuActive;
        private bool disposed;
        private string? lastRouteState;

        internal InputService(Action<string> warn, Action<string> error)
        {
            this.warn = warn;
            this.error = error;
            debug = new KeybindingDebugLog(ApiSelfSettings.Current, warn);
            nativeBindingReader = new NativeInputBindingReader(warn);
            string filePath = Path.Combine(MelonEnvironment.UserDataDirectory, "SprocketModAPI", "keybindings.json");
            store = new KeybindingStore(filePath, warn);
            foreach (var item in store.Load())
                retained[item.Key] = item.Value;
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.IsValid() && scene.isLoaded && !string.IsNullOrWhiteSpace(scene.name))
                    loadedScenes.Add(scene.name);
            }
        }

        public event Action<IReadOnlyList<ModActionDefinition>>? ActionsChanged;
        internal event Action? InternalActionsChanged;
        internal IReadOnlyCollection<ActionState> Actions => actions.Values;

        [MethodImpl(MethodImplOptions.NoInlining)] // GetCallingAssembly 需要真实栈帧：不能被内联进模组方法
        public IInputActionHandle RegisterAction(ModActionDefinition definition)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(definition.ModId))
                definition.ModId = ModIdentity.ResolveModId(Assembly.GetCallingAssembly());
            Validate(definition);
            if (actions.ContainsKey(definition.StableId))
                throw new InvalidOperationException($"Duplicate action ID: {definition.StableId}");

            var state = new ActionState(definition, error, debug, LoadBinding(definition.StableId), BindingsChanged, RemoveAction);
            actions.Add(definition.StableId, state);
            NotifyActionsChanged();
            return state;
        }

        public IDisposable AcquireInputBlock(object owner, string reason)
            => AcquireInputBlock(owner, reason, null);

        // `exemptActionId`：这块 UI 自己那个开关键必须继续响应，否则只能开、不能关。
        // 只豁免一个明确的动作 ID，其它模组的动作照旧被挡住。
        internal IDisposable AcquireInputBlock(object owner, string reason, string? exemptActionId)
        {
            ThrowIfDisposed();
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("An input block reason is required.", nameof(reason));
            var block = new InputBlock(this, exemptActionId);
            blocks.Add(block);
            return block;
        }

        internal void Update(bool externallyBlocked)
        {
            if (disposed)
                return;

            context = DetectContext();
            bool routeReady = route.Observe(context);
            bool focused = Application.isFocused;
            int blockCount = blocks.Count;
            bool blockedByLease = externallyBlocked || blockCount != 0;
            string routeState = $"scene={SceneManager.GetActiveScene().name}, context={context}, stable={routeReady}, focused={focused}, externalBlock={externallyBlocked}, blocks={blockCount}";
            if (debug.Enabled && (debug.LogEveryFrame || routeState != lastRouteState))
                debug.Routing(routeState);
            lastRouteState = routeState;
            if (!routeReady || !focused
                || context == InputContextMask.Settings || context == InputContextMask.TextInput)
            {
                foreach (ActionState action in actions.Values)
                    action.Suppress(routeState);
                return;
            }

            if (blockedByLease)
            {
                // 拿到输入锁的一方可以豁免一个动作（见 AcquireInputBlock 的重载）：被豁免的动作
                // 继续走正常的按下判定，其余动作一律压住；`externallyBlocked`（别的模组的模态）
                // 不参与豁免。
                foreach (ActionState action in actions.Values)
                {
                    if (!externallyBlocked && IsBlockExempt(action))
                        action.Update(context);
                    else
                        action.Suppress(routeState);
                }

                return;
            }

            foreach (ActionState action in actions.Values)
                action.Update(context);
        }

        private bool IsBlockExempt(ActionState action)
        {
            foreach (InputBlock block in blocks)
            {
                if (block.ExemptActionId != null && string.Equals(block.ExemptActionId, action.Definition.StableId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        internal void NotifySceneLoaded(string sceneName)
        {
            if (!string.IsNullOrWhiteSpace(sceneName))
                loadedScenes.Add(sceneName);
            BeginTransition();
            RefreshNativeBindings();
        }

        internal void NotifySceneUnloaded(string sceneName)
        {
            if (!string.IsNullOrWhiteSpace(sceneName))
                loadedScenes.Remove(sceneName);
            if (IsPauseScene(sceneName))
                pauseMenuActive = false;
            if (IsSettingsScene(sceneName))
                settingsPageActive = false;
            BeginTransition();
        }

        internal void NotifySceneChanged() => BeginTransition();

        internal void NotifySettingsPageActive(bool active)
        {
            if (settingsPageActive == active)
                return;
            settingsPageActive = active;
            BeginTransition();
        }

        internal void NotifyPauseMenuActive(bool active)
        {
            if (pauseMenuActive == active)
                return;
            pauseMenuActive = active;
            BeginTransition();
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

        private void BeginTransition()
        {
            route.BeginTransition();
            foreach (ActionState action in actions.Values)
                action.Suppress("scene-transition");
        }

        private InputContextMask DetectContext()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            return SceneInputContextProbe.Resolve(sceneName, loadedScenes, pauseMenuActive, settingsPageActive,
                IsTextInputSelected());
        }

        private static bool IsSettingsScene(string sceneName)
            => sceneName.Contains("Settings", StringComparison.OrdinalIgnoreCase);

        private static bool IsPauseScene(string sceneName)
            => sceneName.Contains("Pause", StringComparison.OrdinalIgnoreCase)
                || sceneName.Contains("EscapeMenu", StringComparison.OrdinalIgnoreCase);

        private static bool IsTextInputSelected()
        {
            try
            {
                GameObject? selected = EventSystem.current?.currentSelectedGameObject;
                if (selected == null || !selected.activeInHierarchy)
                    return false;
                return selected.GetComponentInParent<TMP_InputField>() != null
                    || selected.GetComponentInParent<InputField>() != null;
            }
            catch (Exception)
            {
                // EventSystem can retain an IL2CPP wrapper for a selected object while its scene unloads.
                return false;
            }
        }

        private static void Validate(ModActionDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(definition.ModId) || string.IsNullOrWhiteSpace(definition.ActionId)
                || definition.ModId.Contains(':') || definition.ActionId.Contains(':'))
                throw new ArgumentException("modId and actionId must be non-empty and cannot contain ':'.");
            // 默认键位**允许全空**：动作注册后初始为未绑定（`KeyChord.IsEmpty`），玩家在键位窗口里自己绑。
            // 走这条路的多为「不该占用默认键」的动作，例如 Mod 菜单（入口在设置页，不需要抢键）。
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
            internal InputBlock(InputService service, string? exemptActionId)
            {
                this.service = service;
                ExemptActionId = exemptActionId;
            }

            // 该输入锁唯一允许继续触发的动作 ID；null 表示锁住全部动作。
            internal string? ExemptActionId { get; }

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
        private readonly KeybindingDebugLog debug;
        private readonly Action changed;
        private readonly Action<ActionState> remove;
        private readonly BindingSlots bindings;
        private readonly ActionDispatchState dispatch = new();
        private bool isPressed;
        private bool pressed;
        private bool released;
        private bool enabled = true;
        private bool disposed;
        private InputAction? primaryAction;
        private InputAction? secondaryAction;
        private float pressedAt;
        private int flagsFrame = -1;

        internal ActionState(ModActionDefinition definition, Action<string> error, KeybindingDebugLog debug,
            Dictionary<string, string?> overrides, Action changed, Action<ActionState> remove)
        {
            Definition = definition;
            this.error = error;
            this.debug = debug;
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
        public bool IsPressed => isPressed;
        public bool Enabled { get => enabled; set => enabled = value; }
        public KeyChord Primary => bindings.Primary;
        public KeyChord Secondary => bindings.Secondary;

        internal void Update(InputContextMask context)
        {
            ResetFrameFlags();
            bool physicalPressed = ReadPhysicalState(out bool primaryRaw, out bool secondaryRaw, out ModifierKeys modifiers);
            bool contextMatches = (Definition.Contexts & context) != 0;
            bool allowed = ActionGateEvaluator.IsAllowed(enabled, contextMatches,
                Definition.Gate, exception => error($"[SMA-KEY] {Definition.StableId} gate failed: {exception}"));
            if (primaryRaw || secondaryRaw || debug.LogEveryFrame)
                debug.Binding($"action={Definition.StableId}, primaryRaw={primaryRaw}, secondaryRaw={secondaryRaw}, physical={physicalPressed}, enabled={enabled}, context={context}, contextMatch={contextMatches}, allowed={allowed}, modifiers={modifiers}, primary={KeyChordCodec.Format(Primary) ?? "empty"}, secondary={KeyChordCodec.Format(Secondary) ?? "empty"}", true);
            ActionDispatchResult result = dispatch.Update(physicalPressed, allowed, true);
            Apply(result, true);
        }

        internal void Suppress(string reason)
        {
            ResetFrameFlags();
            pressed = false;
            bool physicalPressed = ReadPhysicalState(out bool primaryRaw, out bool secondaryRaw, out ModifierKeys modifiers);
            if (primaryRaw || secondaryRaw || physicalPressed)
                debug.Binding($"action={Definition.StableId}, suppressed=true, reason={reason}, primaryRaw={primaryRaw}, secondaryRaw={secondaryRaw}, physical={physicalPressed}, modifiers={modifiers}", true);
            ActionDispatchResult result = dispatch.Update(physicalPressed, false);
            Apply(result, false);
        }

        private void ResetFrameFlags()
        {
            int frame = Time.frameCount;
            if (flagsFrame == frame)
                return;
            flagsFrame = frame;
            pressed = released = false;
        }

        private void Apply(ActionDispatchResult result, bool allowTap)
        {
            if (result.PressedThisFrame)
            {
                pressed = true;
                pressedAt = Time.unscaledTime;
                SafeInvoke(Pressed);
            }
            if (result.ReleasedThisFrame)
            {
                released = true;
                SafeInvoke(Released);
                if (allowTap && Time.unscaledTime - pressedAt <= InputSystem.settings.defaultTapTime)
                    SafeInvoke(Tapped);
            }
            isPressed = result.IsPressed;
        }

        private bool ReadPhysicalState(out bool primaryRaw, out bool secondaryRaw, out ModifierKeys modifiers)
        {
            modifiers = CurrentModifiers();
            primaryRaw = IsRawPressed(primaryAction);
            secondaryRaw = IsRawPressed(secondaryAction);
            return (primaryRaw && ModifiersMatch(bindings.Primary, modifiers))
                || (secondaryRaw && ModifiersMatch(bindings.Secondary, modifiers));
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
            catch (Exception exception) { error($"[SMA-KEY] {Definition.StableId} callback failed: {exception}"); }
        }

        private static KeyChord Resolve(Dictionary<string, string?> overrides, string key, KeyChord fallback)
        {
            if (!overrides.TryGetValue(key, out string? value)) return fallback;
            if (value == null) return default;
            return KeyChordCodec.TryParse(value, out KeyChord chord) ? chord : fallback;
        }

        private static bool IsRawPressed(InputAction? action)
            => action != null && action.IsPressed();

        private static bool ModifiersMatch(KeyChord chord, ModifierKeys actual)
        {
            if (chord.IsEmpty)
                return false;
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

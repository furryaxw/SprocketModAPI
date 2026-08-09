using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Il2CppSprocket.SettingConfiguration;
using Il2CppInterop.Runtime.Injection;

[assembly: MelonInfo(typeof(SprocketModAPI.ApiMod), "Sprocket Mod API", "0.1.0", "furryAxw")]
[assembly: MelonGame("HD", "Sprocket")]

namespace SprocketModAPI
{
    [Flags]
    public enum InputContextMask { None = 0, Gameplay = 1, Designer = 2, MainMenu = 4, PauseMenu = 8, Settings = 16, TextInput = 32, OtherMenu = 64, All = 127 }
    [Flags]
    public enum ModifierKeys { None = 0, LeftShift = 1, RightShift = 2, LeftCtrl = 4, RightCtrl = 8, LeftAlt = 16, RightAlt = 32, AnyShift = LeftShift | RightShift, AnyCtrl = LeftCtrl | RightCtrl, AnyAlt = LeftAlt | RightAlt }

    public readonly struct KeyChord : IEquatable<KeyChord>
    {
        public KeyChord(string controlPath, ModifierKeys modifiers = ModifierKeys.None)
        {
            if (string.IsNullOrWhiteSpace(controlPath) || !controlPath.StartsWith("<", StringComparison.Ordinal) || !controlPath.Contains(">/", StringComparison.Ordinal))
                throw new ArgumentException("Invalid Unity Input System control path.", nameof(controlPath));
            RawControlPath = controlPath.Trim().ToLowerInvariant();
            ControlPath = RawControlPath;
            Modifiers = modifiers;
        }
        public string ControlPath { get; }
        internal string RawControlPath { get; }
        public ModifierKeys Modifiers { get; }
        public bool IsEmpty => string.IsNullOrEmpty(RawControlPath);
        public bool Equals(KeyChord other) => RawControlPath == other.RawControlPath && Modifiers == other.Modifiers;
        public override bool Equals(object? obj) => obj is KeyChord other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(RawControlPath, Modifiers);
        public static bool operator ==(KeyChord a, KeyChord b) => a.Equals(b);
        public static bool operator !=(KeyChord a, KeyChord b) => !a.Equals(b);
        public override string ToString() => IsEmpty ? "Unbound" : (Modifiers == ModifierKeys.None ? ControlPath : $"{Modifiers}+{ControlPath}");
    }

    public sealed class ModActionDefinition
    {
        public string ModId { get; init; } = "";
        public string ActionId { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string Category { get; init; } = "General";
        public string Description { get; init; } = "";
        public KeyChord DefaultPrimary { get; init; }
        public KeyChord DefaultSecondary { get; init; }
        public InputContextMask Contexts { get; init; } = InputContextMask.Gameplay;
        public Func<bool>? Gate { get; init; }
        public string StableId => $"{ModId}:{ActionId}";
    }

    public interface IInputActionHandle : IDisposable
    {
        event Action? Pressed; event Action? Released; event Action? Tapped;
        bool WasPressedThisFrame { get; } bool WasReleasedThisFrame { get; } bool IsPressed { get; }
        bool Enabled { get; set; } KeyChord Primary { get; } KeyChord Secondary { get; }
        void SetBinding(int slot, KeyChord? binding); void RestoreDefaults(); void Unregister();
    }
    public interface IInputService
    {
        IInputActionHandle RegisterAction(ModActionDefinition definition);
        IDisposable AcquireInputBlock(object owner, string reason);
        event Action<IReadOnlyList<ModActionDefinition>>? ActionsChanged;
    }
    public static class SprocketApi
    {
        public static Version ApiVersion { get; } = new(1, 0);
        public static bool IsCompatible(Version requested) => requested.Major == ApiVersion.Major && requested.Minor <= ApiVersion.Minor;
        public static bool TryGetService<T>(out T? service) where T : class { service = ApiMod.Service as T; return service != null; }
        public static T? TryGetService<T>() where T : class => ApiMod.Service as T;
    }

    public sealed class ApiMod : MelonMod
    {
        internal static InputService? Service { get; private set; }
        public override void OnInitializeMelon()
        {
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<ContentHierarchyWatcher>();
                ClassInjector.RegisterTypeInIl2Cpp<KeymappingActivationWatcher>();
                ClassInjector.RegisterTypeInIl2Cpp<ActionButtonsAlignmentWatcher>();
            }
            catch (Exception exception)
            {
                LoggerInstance.Error($"[SMA] Settings UI observer registration failed: {exception}");
            }

            Service = new InputService(LoggerInstance.Warning, LoggerInstance.Error);
            Service.InitializeSettingsPageListeners();
            LoggerInstance.Msg("Sprocket Mod API 0.1.0 initialized (API 1.0).");
        }
        public override void OnUpdate() { Service?.Update(); Service?.UpdateUi(); }
        public override void OnGUI() { }
        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            Service?.NotifySceneChanged();
            Service?.NotifySceneLoadedForUi(sceneName);
        }
        public override void OnDeinitializeMelon() { Service?.Dispose(); Service = null; }
    }

    internal sealed partial class InputService : IInputService, IDisposable
    {
        private readonly Action<string> warn; private readonly Action<string> error; private readonly Dictionary<string, ActionState> actions = new(); private readonly Dictionary<string, Dictionary<string, string?>> retained = new(); private readonly List<IDisposable> blocks = new(); private readonly string filePath; private InputContextMask context = InputContextMask.OtherMenu; private bool focused = true;
        public event Action<IReadOnlyList<ModActionDefinition>>? ActionsChanged;
        public InputService(Action<string> warn, Action<string> error) { this.warn = warn; this.error = error; filePath = Path.Combine(MelonEnvironment.UserDataDirectory, "SprocketModAPI", "keybindings.json"); LoadAll(); }
        public IInputActionHandle RegisterAction(ModActionDefinition definition) { Validate(definition); if (actions.ContainsKey(definition.StableId)) throw new InvalidOperationException($"Duplicate action ID: {definition.StableId}"); var state = new ActionState(definition, error, LoadBinding(definition.StableId), Save, RemoveAction); actions.Add(definition.StableId, state); uiDirty = true; ActionsChanged?.Invoke(actions.Values.Select(a => a.Definition).ToArray()); return state; }
        public IDisposable AcquireInputBlock(object owner, string reason) { var block = new Block(this, owner, reason); blocks.Add(block); return block; }
        public void Update() { focused = Application.isFocused; context = DetectContext(); UpdateCapture(); if (!focused || blocks.Count != 0 || uiVisible || context == InputContextMask.Settings || context == InputContextMask.TextInput) { foreach (var a in actions.Values) a.Suppress(); return; } foreach (var a in actions.Values) a.Update(context); }
        public void NotifySceneChanged() { context = DetectContext(); foreach (var a in actions.Values) a.Suppress(); }
        private void Remove(Block block) => blocks.Remove(block);
        public void Dispose() { Save(); foreach (var a in actions.Values.ToArray()) a.Detach(); actions.Clear(); }
        private void RemoveAction(ActionState state) { retained[state.Definition.StableId] = state.Serialize(); if (actions.Remove(state.Definition.StableId)) { uiDirty = true; Save(); ActionsChanged?.Invoke(actions.Values.Select(a => a.Definition).ToArray()); } }
        private InputContextMask DetectContext() { string n = SceneManager.GetActiveScene().name; if (n.Contains("Settings", StringComparison.OrdinalIgnoreCase)) return InputContextMask.Settings; SettingsMenu? settings = UnityEngine.Object.FindObjectOfType<SettingsMenu>(); if (settings != null && settings.gameObject != null && settings.gameObject.activeInHierarchy) return InputContextMask.Settings; if (n.Contains("Designer", StringComparison.OrdinalIgnoreCase)) return InputContextMask.Designer; if (n.Contains("Menu", StringComparison.OrdinalIgnoreCase)) return InputContextMask.MainMenu; return n.Contains("VehicleControl", StringComparison.OrdinalIgnoreCase) ? InputContextMask.Gameplay : InputContextMask.OtherMenu; }
        private static void Validate(ModActionDefinition d) { if (string.IsNullOrWhiteSpace(d.ModId) || string.IsNullOrWhiteSpace(d.ActionId) || d.ModId.Contains(':') || d.ActionId.Contains(':')) throw new ArgumentException("modId and actionId must be non-empty and cannot contain ':'."); if (d.DefaultPrimary.IsEmpty && d.DefaultSecondary.IsEmpty) throw new ArgumentException("At least one default binding is required."); }
        private void LoadAll() { try { if (!File.Exists(filePath)) return; ConfigFile? root = JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(filePath)); if (root?.Actions != null) foreach (var item in root.Actions) retained[item.Key] = item.Value; } catch (Exception e) { warn($"[SMA] ignored corrupt keybindings: {e.Message}"); } }
        private Dictionary<string, string?> LoadBinding(string id) => retained.TryGetValue(id, out var value) ? value : new();
        private void Save() { try { Directory.CreateDirectory(Path.GetDirectoryName(filePath)!); string tmp = filePath + ".tmp"; var data = new Dictionary<string, Dictionary<string, string?>>(retained); foreach (var x in actions) data[x.Key] = x.Value.Serialize(); var root = new ConfigFile { Actions = data }; File.WriteAllText(tmp, JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true })); if (File.Exists(filePath)) File.Replace(tmp, filePath, null); else File.Move(tmp, filePath); } catch (Exception e) { warn($"[SMA] keybinding save failed: {e.Message}"); } }
        private sealed class ConfigFile { public int SchemaVersion { get; set; } = 1; public string ApiVersion { get; set; } = "1.0"; public Dictionary<string, Dictionary<string, string?>> Actions { get; set; } = new(); }
        private sealed class Block : IDisposable { private readonly InputService service; private bool disposed; public Block(InputService s, object owner, string reason) { service = s; } public void Dispose() { if (!disposed) { disposed = true; service.Remove(this); } } }
    }

    internal sealed class ActionState : IInputActionHandle
    {
        public readonly ModActionDefinition Definition; private readonly Action<string> error; private readonly Action changed; private readonly Action<ActionState> remove; private KeyChord primary, secondary; private bool previous, pressed, released, enabled = true, disposed; private InputAction? primaryAction, secondaryAction; private float pressedAt;
        public ActionState(ModActionDefinition d, Action<string> e, Dictionary<string, string?> o, Action changed, Action<ActionState> remove) { Definition = d; error = e; this.changed = changed; this.remove = remove; primary = Resolve(o, "primary", d.DefaultPrimary); secondary = Resolve(o, "secondary", d.DefaultSecondary); Rebuild(); }
        public event Action? Pressed; public event Action? Released; public event Action? Tapped; public bool WasPressedThisFrame => pressed; public bool WasReleasedThisFrame => released; public bool IsPressed => previous; public bool Enabled { get => enabled; set => enabled = value; } public KeyChord Primary => primary; public KeyChord Secondary => secondary;
        public void Update(InputContextMask context) { pressed = released = false; bool allowed = enabled && (Definition.Contexts & context) != 0 && (Definition.Gate?.Invoke() ?? true); bool now = allowed && (ChordPressed(primary, primaryAction) || ChordPressed(secondary, secondaryAction)); if (!previous && now) { pressed = true; pressedAt = Time.unscaledTime; SafeInvoke(Pressed); } if (previous && !now) { released = true; SafeInvoke(Released); if (Time.unscaledTime - pressedAt <= InputSystem.settings.defaultTapTime) SafeInvoke(Tapped); } previous = now; }
        public void Suppress() { if (previous) { previous = false; released = true; SafeInvoke(Released); } pressed = false; }
        public void SetBinding(int slot, KeyChord? binding) { if (slot == 0) primary = binding ?? default; else if (slot == 1) secondary = binding ?? default; else throw new ArgumentOutOfRangeException(nameof(slot), "Only binding slots 0 and 1 are supported."); Rebuild(); changed(); }
        public void RestoreDefaults() { primary = Definition.DefaultPrimary; secondary = Definition.DefaultSecondary; Rebuild(); changed(); }
        public void Unregister() => Dispose(); public void Dispose() { if (disposed) return; disposed = true; Detach(); remove(this); }
        internal void Detach() { primaryAction?.Disable(); primaryAction?.Dispose(); secondaryAction?.Disable(); secondaryAction?.Dispose(); primaryAction = secondaryAction = null; }
        public Dictionary<string, string?> Serialize() { var value = new Dictionary<string, string?>(); if (primary != Definition.DefaultPrimary) value["primary"] = Format(primary); if (secondary != Definition.DefaultSecondary) value["secondary"] = Format(secondary); return value; }
        private void Rebuild() { Detach(); primaryAction = Build(primary, "primary"); secondaryAction = Build(secondary, "secondary"); }
        private InputAction? Build(KeyChord chord, string slot) { if (chord.IsEmpty) return null; var value = new InputAction($"{Definition.StableId}:{slot}"); value.AddBinding(chord.RawControlPath); value.Enable(); return value; }
        private void SafeInvoke(Action? callback) { try { callback?.Invoke(); } catch (Exception e) { error($"[SMA] {Definition.StableId} callback failed: {e}"); } }
        private static string? Format(KeyChord chord) => chord.IsEmpty ? null : $"{chord.RawControlPath}|{(int)chord.Modifiers}";
        private static KeyChord Resolve(Dictionary<string, string?> o, string key, KeyChord fallback) { if (!o.TryGetValue(key, out var value)) return fallback; if (string.IsNullOrWhiteSpace(value)) return default; int marker = value.LastIndexOf('|'); if (marker < 0) return new KeyChord(value); string path = value.Substring(0, marker); return int.TryParse(value.Substring(marker + 1), out int modifiers) ? new KeyChord(path, (ModifierKeys)modifiers) : new KeyChord(path); }
        private static bool ChordPressed(KeyChord chord, InputAction? action) { if (chord.IsEmpty || action == null || !action.IsPressed()) return false; ModifierKeys actual = CurrentModifiers(); actual &= ~PrimaryModifier(chord.RawControlPath); return actual == chord.Modifiers; }
        private static ModifierKeys CurrentModifiers() { Keyboard? k = Keyboard.current; if (k == null) return ModifierKeys.None; ModifierKeys m = ModifierKeys.None; if (k.leftShiftKey.isPressed) m |= ModifierKeys.LeftShift; if (k.rightShiftKey.isPressed) m |= ModifierKeys.RightShift; if (k.leftCtrlKey.isPressed) m |= ModifierKeys.LeftCtrl; if (k.rightCtrlKey.isPressed) m |= ModifierKeys.RightCtrl; if (k.leftAltKey.isPressed) m |= ModifierKeys.LeftAlt; if (k.rightAltKey.isPressed) m |= ModifierKeys.RightAlt; return m; }
        private static ModifierKeys PrimaryModifier(string path) => path switch { "<keyboard>/leftshift" => ModifierKeys.LeftShift, "<keyboard>/rightshift" => ModifierKeys.RightShift, "<keyboard>/leftctrl" => ModifierKeys.LeftCtrl, "<keyboard>/rightctrl" => ModifierKeys.RightCtrl, "<keyboard>/leftalt" => ModifierKeys.LeftAlt, "<keyboard>/rightalt" => ModifierKeys.RightAlt, _ => ModifierKeys.None };
    }
}

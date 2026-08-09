using System;
using System.Collections.Generic;

namespace SprocketModAPI
{
    [Flags]
    public enum InputContextMask
    {
        None = 0, Gameplay = 1, Designer = 2, MainMenu = 4, PauseMenu = 8,
        Settings = 16, TextInput = 32, OtherMenu = 64, All = 127
    }

    [Flags]
    public enum ModifierKeys
    {
        None = 0, LeftShift = 1, RightShift = 2, LeftCtrl = 4, RightCtrl = 8,
        LeftAlt = 16, RightAlt = 32, AnyShift = LeftShift | RightShift,
        AnyCtrl = LeftCtrl | RightCtrl, AnyAlt = LeftAlt | RightAlt
    }

    public readonly struct KeyChord : IEquatable<KeyChord>
    {
        public KeyChord(string controlPath, ModifierKeys modifiers = ModifierKeys.None)
        {
            if (string.IsNullOrWhiteSpace(controlPath)
                || !controlPath.StartsWith("<", StringComparison.Ordinal)
                || !controlPath.Contains(">/", StringComparison.Ordinal))
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
        public static bool operator ==(KeyChord left, KeyChord right) => left.Equals(right);
        public static bool operator !=(KeyChord left, KeyChord right) => !left.Equals(right);
        public override string ToString() => IsEmpty ? "Unbound" : Modifiers == ModifierKeys.None ? ControlPath : $"{Modifiers}+{ControlPath}";
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
        event Action? Pressed;
        event Action? Released;
        event Action? Tapped;
        bool WasPressedThisFrame { get; }
        bool WasReleasedThisFrame { get; }
        bool IsPressed { get; }
        bool Enabled { get; set; }
        KeyChord Primary { get; }
        KeyChord Secondary { get; }
        void SetBinding(int slot, KeyChord? binding);
        void RestoreDefaults();
        void Unregister();
    }

    public interface IInputService
    {
        IInputActionHandle RegisterAction(ModActionDefinition definition);
        IDisposable AcquireInputBlock(object owner, string reason);
        event Action<IReadOnlyList<ModActionDefinition>>? ActionsChanged;
    }
}

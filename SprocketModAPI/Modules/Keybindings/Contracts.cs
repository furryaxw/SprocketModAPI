using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

        // 给界面/提示文本用的可读键名，例如 `Left Ctrl + Z`、`F10`、`Space`；未绑定时是 `Unbound`。
        //
        // 和 `ToString` 的区别：`ToString` 给日志与持久化看（保留原始 control path），
        // 这个给玩家看。模组的提示文本**不要**手写键名，直接用 `动作.Primary.DisplayName`，
        // 这样玩家重绑之后提示自动跟着变。
        public string DisplayName
        {
            get
            {
                if (IsEmpty)
                    return "Unbound";

                string main = FriendlyKey(ControlPath);
                string modifiers = FriendlyModifiers(Modifiers);
                if (modifiers.Length == 0)
                    return main;
                return string.Equals(modifiers, main, StringComparison.Ordinal) ? main : $"{modifiers} + {main}";
            }
        }

        private static string FriendlyModifiers(ModifierKeys modifiers)
        {
            if (modifiers == ModifierKeys.None)
                return "";

            var parts = new List<string>();
            void Add(ModifierKeys flag, string name)
            {
                if (modifiers.HasFlag(flag))
                    parts.Add(name);
            }

            Add(ModifierKeys.LeftCtrl, "Left Ctrl");
            Add(ModifierKeys.RightCtrl, "Right Ctrl");
            Add(ModifierKeys.LeftShift, "Left Shift");
            Add(ModifierKeys.RightShift, "Right Shift");
            Add(ModifierKeys.LeftAlt, "Left Alt");
            Add(ModifierKeys.RightAlt, "Right Alt");
            return string.Join(" + ", parts);
        }

        private static string FriendlyKey(string controlPath)
        {
            int separator = controlPath.IndexOf(">/", StringComparison.Ordinal);
            string token = separator >= 0 ? controlPath[(separator + 2)..] : controlPath;
            if (token.Length == 0)
                return "Unbound";
            if (token.Length == 1)
                return token.ToUpperInvariant();

            switch (token)
            {
                case "leftctrl": return "Left Ctrl";
                case "rightctrl": return "Right Ctrl";
                case "leftshift": return "Left Shift";
                case "rightshift": return "Right Shift";
                case "leftalt": return "Left Alt";
                case "rightalt": return "Right Alt";
                case "space": return "Space";
                case "enter":
                case "return": return "Enter";
                case "escape": return "Esc";
                case "tab": return "Tab";
                case "backspace": return "Backspace";
                case "uparrow": return "Up";
                case "downarrow": return "Down";
                case "leftarrow": return "Left";
                case "rightarrow": return "Right";
            }

            if (token.Length is 2 or 3 && token[0] == 'f' && int.TryParse(token[1..], out int functionKey))
                return $"F{functionKey}";

            return char.ToUpperInvariant(token[0]) + token[1..];
        }
    }

    public sealed class ModActionDefinition
    {
        // 键位命名空间。**可以留空**：留空时由 `IInputService` 从调用方程序集的 `Sprocket.Mod.Id` 推断。
        public string ModId { get; set; } = "";
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

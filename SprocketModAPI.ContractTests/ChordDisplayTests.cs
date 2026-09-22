using System;

namespace SprocketModAPI
{
    // `KeyChord.DisplayName`：给玩家看的可读键名（模组提示文本不该手写键名，重绑后要自动跟着变）。
    // 纯字符串格式化，不碰 Unity。
    internal static class ChordDisplayTests
    {
        internal static void Run()
        {
            CheckSingleKeys();
            CheckModifiers();
            CheckModifierOnlyAndDuplicates();
            CheckEmptyAndRawFormStaysUntouched();
        }

        private static void CheckSingleKeys()
        {
            Check(new KeyChord("<Keyboard>/z").DisplayName == "Z", "a letter is upper-cased");
            Check(new KeyChord("<Keyboard>/1").DisplayName == "1", "a digit stays as-is");
            Check(new KeyChord("<Keyboard>/space").DisplayName == "Space", "space has a friendly name");
            Check(new KeyChord("<Keyboard>/escape").DisplayName == "Esc", "escape is shortened");
            Check(new KeyChord("<Keyboard>/f10").DisplayName == "F10", "function keys keep their number");
            Check(new KeyChord("<Keyboard>/upArrow").DisplayName == "Up", "arrows are shortened");
            Check(new KeyChord("<Mouse>/leftButton").DisplayName == "Leftbutton",
                "unknown tokens are capitalized verbatim (control paths are lower-cased on construction)");
        }

        private static void CheckModifiers()
        {
            Check(new KeyChord("<Keyboard>/z", ModifierKeys.LeftCtrl).DisplayName == "Left Ctrl + Z",
                "modifier and key are joined readably");
            Check(new KeyChord("<Keyboard>/z", ModifierKeys.LeftCtrl | ModifierKeys.LeftShift).DisplayName
                == "Left Ctrl + Left Shift + Z", "multiple modifiers keep a stable order");
        }

        private static void CheckModifierOnlyAndDuplicates()
        {
            Check(new KeyChord("<Keyboard>/leftCtrl").DisplayName == "Left Ctrl",
                "a modifier-only chord shows the modifier name");
            Check(new KeyChord("<Keyboard>/leftCtrl", ModifierKeys.LeftCtrl).DisplayName == "Left Ctrl",
                "the same modifier is not printed twice");
        }

        private static void CheckEmptyAndRawFormStaysUntouched()
        {
            Check(default(KeyChord).DisplayName == "Unbound", "an unbound chord says Unbound");
            var chord = new KeyChord("<Keyboard>/z", ModifierKeys.LeftCtrl);
            Check(chord.ToString() == $"{ModifierKeys.LeftCtrl}+<keyboard>/z",
                "ToString keeps the raw control path for logs and persistence");
        }

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException($"Contract failed: {name}");
        }
    }
}

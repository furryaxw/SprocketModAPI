using System;
using System.Linq;
using SprocketModAPI;

internal static class BindingConflictIndexTests
{
    internal static void Run()
    {
        var index = new BindingConflictIndex();
        var plainK = new KeyChord("<Keyboard>/k");
        var ctrlK = new KeyChord("<Keyboard>/k", ModifierKeys.LeftCtrl);

        index.Rebuild(
            new[]
            {
                new ModBindingSnapshot("alpha:toggle", "Alpha Toggle", plainK, ctrlK),
                new ModBindingSnapshot("beta:toggle", "Beta Toggle", default, plainK),
                new ModBindingSnapshot("gamma:toggle", "Gamma Toggle", new KeyChord("<Keyboard>/g"), default)
            },
            new[]
            {
                new NativeBindingSnapshot("Controls:Gameplay:Fire", "GAME Gameplay/Fire", plainK),
                new NativeBindingSnapshot("Controls:Gameplay:Fire", "GAME Gameplay/Fire", plainK),
                new NativeBindingSnapshot("Controls:Gameplay:Command", "GAME Gameplay/Command", ctrlK)
            });

        var alpha = index.GetForAction("alpha:toggle");
        Check(alpha.Count == 3, "mod and native conflicts are indexed without duplicate native sources");
        Check(alpha.Any(conflict => conflict.Slot == 0 && conflict.SourceKind == BindingConflictSourceKind.Mod
            && conflict.SourceId == "beta:toggle"), "mod conflict identifies source action and slot binding");
        Check(alpha.Any(conflict => conflict.Slot == 0 && conflict.SourceKind == BindingConflictSourceKind.Native
            && conflict.SourceName == "GAME Gameplay/Fire"), "native conflict identifies game source");
        Check(alpha.Any(conflict => conflict.Slot == 1 && conflict.Binding == ctrlK
            && conflict.SourceName == "GAME Gameplay/Command"), "modifier-exact native conflict is indexed");

        Check(index.GetForAction("beta:toggle").Count == 2, "secondary slot participates in conflict index");
        Check(index.GetForAction("gamma:toggle").Count == 0, "unconflicted action has empty read-only result");

        string display = KeybindingUiController.FormatConflictSummary(alpha);
        Check(display == "Primary K\n- Beta Toggle\n- GAME Gameplay/Fire\nSecondary LeftCtrl+K\n- GAME Gameplay/Command",
            "conflicts render as grouped Primary and Secondary source lists");

        index.Rebuild(
            new[] { new ModBindingSnapshot("alpha:toggle", "Alpha Toggle", plainK, ctrlK) },
            Array.Empty<NativeBindingSnapshot>());
        Check(index.GetForAction("alpha:toggle").Count == 0, "index refresh removes stale conflicts after sources disappear");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Contract failed: {name}");
    }
}

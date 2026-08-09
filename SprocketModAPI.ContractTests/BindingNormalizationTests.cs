using System;
using SprocketModAPI;

internal static class BindingNormalizationTests
{
    internal static void Run()
    {
        var primary = new KeyChord("<Keyboard>/k");
        var secondary = new KeyChord("<Keyboard>/k");
        var slots = new BindingSlots(primary, secondary, primary, secondary);
        Check(slots.Primary == primary && slots.Secondary.IsEmpty,
            "duplicate Primary and Secondary keeps Primary and clears Secondary");

        primary = new KeyChord("<Keyboard>/k", ModifierKeys.LeftCtrl);
        secondary = new KeyChord("<Keyboard>/k");
        slots = new BindingSlots(primary, secondary, primary, secondary);
        Check(!slots.Secondary.IsEmpty, "same control with different modifiers remains distinct");

        var primaryDefault = new KeyChord("<Keyboard>/k");
        var secondaryDefault = new KeyChord("<Keyboard>/l");
        var loaded = new KeyChord("<Keyboard>/z");
        slots = new BindingSlots(primaryDefault, secondaryDefault, loaded, loaded);
        Check(slots.Primary == loaded && slots.Secondary.IsEmpty,
            "duplicate bindings loaded from configuration clear Secondary");

        slots.SetBinding(0, primaryDefault);
        slots.SetBinding(1, primaryDefault);
        Check(slots.Primary == primaryDefault && slots.Secondary.IsEmpty,
            "setting Secondary equal to Primary clears Secondary");

        slots.SetBinding(1, secondaryDefault);
        slots.SetBinding(0, secondaryDefault);
        Check(slots.Primary == secondaryDefault && slots.Secondary.IsEmpty,
            "setting Primary equal to Secondary keeps new Primary and clears Secondary");

        slots = new BindingSlots(primaryDefault, primaryDefault, secondaryDefault, secondaryDefault);
        slots.RestoreDefaults();
        Check(slots.Primary == primaryDefault && slots.Secondary.IsEmpty,
            "restoring duplicate defaults clears Secondary");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Contract failed: {name}");
    }
}

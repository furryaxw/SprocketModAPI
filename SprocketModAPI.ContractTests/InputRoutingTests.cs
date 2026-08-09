using System;
using SprocketModAPI;

internal static class InputRoutingTests
{
    internal static void Run()
    {
        CheckContextPriority();
        CheckSceneContextProbe();
        CheckTransitionStability();
        CheckSuppressionAndRearm();
        CheckGateIsolation();
    }

    private static void CheckContextPriority()
    {
        Check(InputContextResolver.Resolve(new InputContextSignals(true, false, false, true, false, false))
            == InputContextMask.PauseMenu, "PauseMenu overlays Gameplay");
        Check(InputContextResolver.Resolve(new InputContextSignals(true, true, true, true, true, false))
            == InputContextMask.Settings, "Settings has priority over menus and gameplay");
        Check(InputContextResolver.Resolve(new InputContextSignals(true, true, true, true, true, true))
            == InputContextMask.TextInput, "TextInput has highest priority");
    }

    private static void CheckTransitionStability()
    {
        var route = new InputRouteState();
        Check(!route.Observe(InputContextMask.Gameplay), "first context observation remains transition blocked");
        Check(route.Observe(InputContextMask.Gameplay), "second stable context observation opens transition gate");
        Check(!route.Observe(InputContextMask.PauseMenu), "context change immediately closes transition gate");
        Check(route.Observe(InputContextMask.PauseMenu), "changed context must stabilize before dispatch");
        route.BeginTransition();
        Check(!route.Observe(InputContextMask.PauseMenu), "scene transition immediately suppresses stable context");
        Check(route.Observe(InputContextMask.PauseMenu), "scene transition reopens after two stable observations");
    }

    private static void CheckSceneContextProbe()
    {
        Check(SceneInputContextProbe.Resolve("Sandbox", new[] { "Sandbox", "VehicleDesignerUI" },
            false, false, false) == InputContextMask.Designer,
            "VehicleDesignerUI identifies Designer while Sandbox remains active");
        Check(SceneInputContextProbe.Resolve("Sandbox", new[] { "Sandbox", "VehicleControlUI" },
            false, false, false) == InputContextMask.Gameplay,
            "VehicleControlUI identifies Gameplay while Sandbox remains active");
        Check(SceneInputContextProbe.Resolve("Sandbox", new[] { "Sandbox", "VehicleControlUI", "PauseMenu" },
            true, false, false) == InputContextMask.PauseMenu,
            "active PauseMenu watcher overlays VehicleControlUI gameplay");
    }

    private static void CheckSuppressionAndRearm()
    {
        var state = new ActionDispatchState();
        ActionDispatchResult pressed = state.Update(true, true);
        Check(pressed.PressedThisFrame && pressed.IsPressed, "physical press dispatches Pressed");

        ActionDispatchResult suppressed = state.Update(true, false);
        Check(suppressed.ReleasedThisFrame && !suppressed.IsPressed, "suppression releases held action once");
        ActionDispatchResult stillSuppressed = state.Update(true, false);
        Check(!stillSuppressed.ReleasedThisFrame, "release flag lasts one update only");

        ActionDispatchResult refocusedHeld = state.Update(true, true);
        Check(!refocusedHeld.PressedThisFrame && !refocusedHeld.IsPressed,
            "refocus while physical key remains held does not synthesize Pressed");
        ActionDispatchResult released = state.Update(false, true);
        Check(!released.PressedThisFrame && !released.ReleasedThisFrame, "physical release rearms without dispatch");
        ActionDispatchResult pressedAgain = state.Update(true, true);
        Check(pressedAgain.PressedThisFrame && pressedAgain.IsPressed, "press after rearm dispatches normally");

        var focusReset = new ActionDispatchState();
        focusReset.Update(true, true);
        ActionDispatchResult resetOnFocusLoss = focusReset.Update(false, false);
        Check(resetOnFocusLoss.ReleasedThisFrame, "focus loss releases even if the input device was reset first");
        ActionDispatchResult restoredHeldState = focusReset.Update(true, true);
        Check(!restoredHeldState.PressedThisFrame,
            "restored held state after an input device reset remains disarmed until release");

        var contextMismatch = new ActionDispatchState();
        contextMismatch.Update(true, true);
        contextMismatch.Update(true, false);
        contextMismatch.Update(false, false, true);
        ActionDispatchResult pressedAfterContextRelease = contextMismatch.Update(true, true);
        Check(pressedAfterContextRelease.PressedThisFrame,
            "physical release while only the action context is ineligible rearms the action");
    }

    private static void CheckGateIsolation()
    {
        int errors = 0;
        bool failed = ActionGateEvaluator.IsAllowed(true, true,
            () => throw new InvalidOperationException("gate failure"), _ => errors++);
        bool unaffected = ActionGateEvaluator.IsAllowed(true, true, () => true, _ => errors++);
        Check(!failed && unaffected && errors == 1, "gate exception only disables its own action evaluation");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Contract failed: {name}");
    }
}

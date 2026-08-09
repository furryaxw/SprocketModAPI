using System;
using System.Collections.Generic;

namespace SprocketModAPI
{
    internal readonly struct InputContextSignals
    {
        internal InputContextSignals(bool gameplay, bool designer, bool mainMenu, bool pauseMenu,
            bool settings, bool textInput)
        {
            Gameplay = gameplay;
            Designer = designer;
            MainMenu = mainMenu;
            PauseMenu = pauseMenu;
            Settings = settings;
            TextInput = textInput;
        }

        internal bool Gameplay { get; }
        internal bool Designer { get; }
        internal bool MainMenu { get; }
        internal bool PauseMenu { get; }
        internal bool Settings { get; }
        internal bool TextInput { get; }
    }

    internal static class InputContextResolver
    {
        internal static InputContextMask Resolve(InputContextSignals signals)
        {
            if (signals.TextInput) return InputContextMask.TextInput;
            if (signals.Settings) return InputContextMask.Settings;
            if (signals.PauseMenu) return InputContextMask.PauseMenu;
            if (signals.Designer) return InputContextMask.Designer;
            if (signals.MainMenu) return InputContextMask.MainMenu;
            if (signals.Gameplay) return InputContextMask.Gameplay;
            return InputContextMask.OtherMenu;
        }
    }

    internal static class SceneInputContextProbe
    {
        internal static InputContextMask Resolve(string activeSceneName, IEnumerable<string> loadedSceneNames,
            bool pauseMenuActive, bool settingsMenuActive, bool textInputActive)
        {
            bool gameplay = Contains(activeSceneName, "VehicleControl");
            bool designer = Contains(activeSceneName, "Designer");
            bool mainMenu = Contains(activeSceneName, "MainMenu");

            foreach (string sceneName in loadedSceneNames)
            {
                gameplay |= Contains(sceneName, "VehicleControlUI");
                designer |= Contains(sceneName, "VehicleDesignerUI");
                mainMenu |= Contains(sceneName, "MainMenu");
            }

            bool settings = settingsMenuActive || Contains(activeSceneName, "Settings");
            bool pause = pauseMenuActive || Contains(activeSceneName, "Pause")
                || Contains(activeSceneName, "EscapeMenu");
            return InputContextResolver.Resolve(new InputContextSignals(gameplay, designer, mainMenu, pause,
                settings, textInputActive));
        }

        private static bool Contains(string value, string expected)
            => value?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true;
    }

    internal sealed class InputRouteState
    {
        private const int StableObservationsRequired = 2;
        private InputContextMask observedContext = InputContextMask.None;
        private int stableObservations;
        private bool transitionBlocked = true;

        internal InputContextMask Context => observedContext;
        internal bool TransitionBlocked => transitionBlocked;

        internal void BeginTransition()
        {
            transitionBlocked = true;
            stableObservations = 0;
        }

        internal bool Observe(InputContextMask context)
        {
            if (context != observedContext)
            {
                observedContext = context;
                transitionBlocked = true;
                stableObservations = 1;
                return false;
            }

            if (!transitionBlocked)
                return true;

            stableObservations++;
            if (stableObservations < StableObservationsRequired)
                return false;

            transitionBlocked = false;
            return true;
        }
    }

    internal readonly struct ActionDispatchResult
    {
        internal ActionDispatchResult(bool isPressed, bool pressedThisFrame, bool releasedThisFrame)
        {
            IsPressed = isPressed;
            PressedThisFrame = pressedThisFrame;
            ReleasedThisFrame = releasedThisFrame;
        }

        internal bool IsPressed { get; }
        internal bool PressedThisFrame { get; }
        internal bool ReleasedThisFrame { get; }
    }

    internal sealed class ActionDispatchState
    {
        private bool logicalPressed;
        private bool disarmed;

        internal ActionDispatchResult Update(bool physicalPressed, bool allowed,
            bool canRearmWhileDisallowed = false)
        {
            bool pressed = false;
            bool released = false;

            if (!allowed)
            {
                released = logicalPressed;
                if (logicalPressed || physicalPressed)
                    disarmed = true;
                else if (canRearmWhileDisallowed)
                    disarmed = false;
                logicalPressed = false;
                return new ActionDispatchResult(false, false, released);
            }

            if (disarmed)
            {
                if (!physicalPressed)
                    disarmed = false;
                return new ActionDispatchResult(false, false, false);
            }

            if (!logicalPressed && physicalPressed)
            {
                logicalPressed = true;
                pressed = true;
            }
            else if (logicalPressed && !physicalPressed)
            {
                logicalPressed = false;
                released = true;
            }

            return new ActionDispatchResult(logicalPressed, pressed, released);
        }
    }

    internal static class ActionGateEvaluator
    {
        internal static bool IsAllowed(bool enabled, bool contextMatches, Func<bool>? gate,
            Action<Exception> onError)
        {
            if (!enabled || !contextMatches)
                return false;

            try
            {
                return gate?.Invoke() ?? true;
            }
            catch (Exception exception)
            {
                onError(exception);
                return false;
            }
        }
    }
}

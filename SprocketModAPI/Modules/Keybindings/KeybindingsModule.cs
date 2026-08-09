using System;
using Il2CppInterop.Runtime.Injection;

namespace SprocketModAPI
{
    internal sealed class KeybindingsModule : IRuntimeModule
    {
        private InputService? input;
        private IDisposable? serviceRegistration;
        internal static KeybindingUiController? Controller { get; private set; }

        public void Initialize(RuntimeModuleContext context)
        {
            RegisterIl2CppTypes(context.Error);
            input = new InputService(context.Warn, context.Error);
            Controller = new KeybindingUiController(input, context.Warn, context.Error);
            serviceRegistration = context.Services.Register<IInputService>(input);
            input.InitializeNativeBindings();
            Controller.InitializeSettingsPageListeners();
        }

        public void Update()
        {
            Controller?.Update();
            input?.Update(Controller?.IsVisible == true);
        }

        public void SceneLoaded(int buildIndex, string sceneName)
        {
            input?.NotifySceneChanged();
            Controller?.NotifySceneLoaded(sceneName);
        }

        public void SceneUnloaded(int buildIndex, string sceneName) { }

        public void Dispose()
        {
            Controller?.Dispose();
            Controller = null;
            serviceRegistration?.Dispose();
            serviceRegistration = null;
            input = null;
        }

        private static void RegisterIl2CppTypes(Action<string> error)
        {
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<ContentHierarchyWatcher>();
                ClassInjector.RegisterTypeInIl2Cpp<KeymappingActivationWatcher>();
                ClassInjector.RegisterTypeInIl2Cpp<ActionButtonsAlignmentWatcher>();
            }
            catch (Exception exception)
            {
                error($"[SMA] Settings UI observer registration failed: {exception}");
            }
        }
    }
}

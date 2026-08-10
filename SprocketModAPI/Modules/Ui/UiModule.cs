using System;

namespace SprocketModAPI
{
    internal sealed class UiModule : IRuntimeModule
    {
        private UiService? service;
        private IDisposable? registration;

        public void Initialize(RuntimeModuleContext context)
        {
            service = new UiService(context.Warn, context.Error);
            registration = context.Services.Register<IUiService>(service);
        }

        public void Update() => service?.Update();
        public void SceneLoaded(int buildIndex, string sceneName) => service?.SceneLoaded(sceneName);
        public void SceneUnloaded(int buildIndex, string sceneName) => service?.SceneUnloaded(sceneName);

        public void Dispose()
        {
            registration?.Dispose();
            registration = null;
            service = null;
        }
    }
}

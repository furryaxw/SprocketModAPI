using System;

namespace SprocketModAPI
{
    internal interface IRuntimeModule : IDisposable
    {
        void Initialize(RuntimeModuleContext context);
        void Update();
        void SceneLoaded(int buildIndex, string sceneName);
        void SceneUnloaded(int buildIndex, string sceneName);
    }

    internal sealed class RuntimeModuleContext
    {
        internal RuntimeModuleContext(ServiceRegistry services, Action<string> warn, Action<string> error)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Warn = warn ?? throw new ArgumentNullException(nameof(warn));
            Error = error ?? throw new ArgumentNullException(nameof(error));
        }

        internal ServiceRegistry Services { get; }
        internal Action<string> Warn { get; }
        internal Action<string> Error { get; }
    }
}

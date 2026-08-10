using System;
using MelonLoader;

[assembly: MelonInfo(typeof(SprocketModAPI.ApiMod), "Sprocket Mod API", "0.1.0", "furryAxw")]
[assembly: MelonGame("HD", "Sprocket")]

namespace SprocketModAPI
{
    public static class SprocketApi
    {
        private static ServiceRegistry? registry;

    public static Version ApiVersion { get; } = new(1, 1);

        public static bool IsCompatible(Version requested)
            => requested.Major == ApiVersion.Major && requested.Minor <= ApiVersion.Minor;

        public static bool TryGetService<T>(out T? service) where T : class
        {
            ServiceRegistry? current = registry;
            if (current == null)
            {
                service = null;
                return false;
            }

            return current.TryGet(out service);
        }

        public static T? TryGetService<T>() where T : class
            => TryGetService<T>(out T? service) ? service : null;

        internal static void Attach(ServiceRegistry services)
        {
            if (registry != null)
                throw new InvalidOperationException("Sprocket Mod API services are already attached.");
            registry = services ?? throw new ArgumentNullException(nameof(services));
        }

        internal static void Detach(ServiceRegistry services)
        {
            if (ReferenceEquals(registry, services))
                registry = null;
        }
    }

    public sealed class ApiMod : MelonMod
    {
        private ServiceRegistry? services;
        private IRuntimeModule[] modules = Array.Empty<IRuntimeModule>();

        public override void OnInitializeMelon()
        {
            services = new ServiceRegistry(LoggerInstance.Error);
            SprocketApi.Attach(services);

            modules = new IRuntimeModule[]
            {
                new KeybindingsModule(),
                new UiModule()
            };

            var context = new RuntimeModuleContext(services, LoggerInstance.Warning, LoggerInstance.Error);
            try
            {
                foreach (IRuntimeModule module in modules)
                    module.Initialize(context);
            }
            catch
            {
                ShutdownModules();
                throw;
            }

            LoggerInstance.Msg("Sprocket Mod API 0.1.0 initialized (API 1.1).");
        }

        public override void OnUpdate()
        {
            foreach (IRuntimeModule module in modules)
                module.Update();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            foreach (IRuntimeModule module in modules)
                module.SceneLoaded(buildIndex, sceneName);
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            foreach (IRuntimeModule module in modules)
                module.SceneUnloaded(buildIndex, sceneName);
        }

        public override void OnDeinitializeMelon() => ShutdownModules();

        private void ShutdownModules()
        {
            for (int index = modules.Length - 1; index >= 0; index--)
            {
                try { modules[index].Dispose(); }
                catch (Exception exception) { LoggerInstance.Error($"[SMA] Module shutdown failed: {exception}"); }
            }
            modules = Array.Empty<IRuntimeModule>();

            ServiceRegistry? current = services;
            if (current != null)
            {
                SprocketApi.Detach(current);
                current.Dispose();
                services = null;
            }
        }
    }
}

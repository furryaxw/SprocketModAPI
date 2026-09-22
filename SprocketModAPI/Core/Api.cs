using System;
using System.Reflection;
using MelonLoader;

[assembly: MelonInfo(typeof(SprocketModAPI.ApiMod), "Sprocket Mod API", "0.3.0", "furryAxw")]
[assembly: MelonGame("HD", "Sprocket")]
[assembly: AssemblyMetadata("Sprocket.Mod.Id", "furryaxw.sprocket-mod-api")]
[assembly: AssemblyMetadata("Sprocket.Mod.DisplayName", "Sprocket Mod API")]
[assembly: AssemblyMetadata("Sprocket.Mod.Description", "Shared runtime library: keybindings, input routing, native UI, mod metadata, declarative config and the in-game mod menu.")]
[assembly: AssemblyMetadata("Sprocket.Mod.Authors", "furryAxw")]
[assembly: AssemblyMetadata("Sprocket.Mod.Repository", "furryaxw/SprocketModAPI")]
[assembly: AssemblyMetadata("Sprocket.Mod.Category", "utility")]
[assembly: AssemblyMetadata("Sprocket.Mod.License", "LGPL-3.0-or-later")]

namespace SprocketModAPI
{
    public static class SprocketApi
    {
        private static ServiceRegistry? registry;

    public static Version ApiVersion { get; } = new(2, 0);

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
        private RuntimeModuleContext? context;
        private IRuntimeModule[] modules = Array.Empty<IRuntimeModule>();

        public override void OnInitializeMelon()
        {
            services = new ServiceRegistry(LoggerInstance.Error);
            SprocketApi.Attach(services);

            // ModConfig 必须最先：键位与 UI 模块的调试开关由 API 自身的诊断设置提供服务（ApiSelfSettings）。
            modules = new IRuntimeModule[]
            {
                new ModConfigModule(),
                new KeybindingsModule(),
                new UiModule(),
                new ModMetaModule(),
                new ModMenuModule()
            };

            context = new RuntimeModuleContext(services, LoggerInstance.Warning, LoggerInstance.Error, LoggerInstance.Msg);
            int failures = RuntimeModuleHost.InitializeAll(modules, context);
            if (failures != 0)
                LoggerInstance.Warning($"Sprocket Mod API {Info.Version} initialized with {failures} module(s) unavailable; the remaining services are still registered.");

            LoggerInstance.Msg($"Sprocket Mod API {Info.Version} initialized (API {SprocketApi.ApiVersion}).");
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
            if (context != null)
                RuntimeModuleHost.ShutdownAll(modules, context);
            modules = Array.Empty<IRuntimeModule>();
            context = null;

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

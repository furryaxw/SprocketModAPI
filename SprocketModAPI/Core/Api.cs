using System;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    [BepInPlugin(PluginGuid, "Sprocket Mod API", "0.3.0")]
    public sealed class ApiMod : BasePlugin
    {
        internal const string PluginGuid = "furryaxw.sprocket-mod-api";
        private const string ModVersion = "0.3.0";

        private ServiceRegistry? services;
        private RuntimeModuleContext? context;
        private IRuntimeModule[] modules = Array.Empty<IRuntimeModule>();

        public override void Load()
        {
            services = new ServiceRegistry(Log.LogError);
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

            context = new RuntimeModuleContext(services, Log.LogWarning, Log.LogError, Log.LogInfo);
            int failures = RuntimeModuleHost.InitializeAll(modules, context);
            if (failures != 0)
                Log.LogWarning($"Sprocket Mod API {ModVersion} initialized with {failures} module(s) unavailable; the remaining services are still registered.");

            AddComponent<ApiDriver>().Configure(this);
            Log.LogInfo($"Sprocket Mod API {ModVersion} initialized (API {SprocketApi.ApiVersion}).");
        }

        public override bool Unload()
        {
            ShutdownModules();
            return true;
        }

        internal void Tick()
        {
            foreach (IRuntimeModule module in modules)
                module.Update();
        }

        internal void SceneLoaded(int buildIndex, string sceneName)
        {
            foreach (IRuntimeModule module in modules)
                module.SceneLoaded(buildIndex, sceneName);
        }

        internal void SceneUnloaded(int buildIndex, string sceneName)
        {
            foreach (IRuntimeModule module in modules)
                module.SceneUnloaded(buildIndex, sceneName);
        }

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

    // BepInEx 没有每帧与场景回调；这个注入组件驱动模块的 Update，并在活动场景句柄变化时
    // 依次发布旧场景的卸载与新场景的加载。
    internal sealed class ApiDriver : MonoBehaviour
    {
        private ApiMod? host;
        private int lastHandle = int.MinValue;
        private int lastBuildIndex = -1;
        private string lastName = "";

        public ApiDriver(IntPtr ptr) : base(ptr)
        {
        }

        public void Configure(ApiMod mod) => host = mod;

        private void Update()
        {
            ApiMod? current = host;
            if (current == null)
                return;

            current.Tick();

            Scene active = SceneManager.GetActiveScene();
            if (active.handle == lastHandle)
                return;

            if (lastHandle != int.MinValue)
                current.SceneUnloaded(lastBuildIndex, lastName);

            lastHandle = active.handle;
            lastBuildIndex = active.buildIndex;
            lastName = active.name;
            current.SceneLoaded(lastBuildIndex, lastName);
        }
    }
}

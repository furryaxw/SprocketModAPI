using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Attributes;
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

    [BepInPlugin(PluginGuid, "Sprocket Mod API", "1.0.0")]
    public sealed class ApiMod : BasePlugin
    {
        internal const string PluginGuid = "furryaxw.sprocket-mod-api";
        private const string ModVersion = "1.0.0";

        private ServiceRegistry? services;
        private RuntimeModuleContext? context;
        private IRuntimeModule[] modules = Array.Empty<IRuntimeModule>();
        private readonly HashSet<IRuntimeModule> failedModules = new();
        private bool tickLogged;

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
            if (!tickLogged)
            {
                tickLogged = true;
                Log.LogInfo($"[SMA] driver tick running modules={modules.Length}");
            }

            // 一个模块抛异常不能拖垮其余模块，也不能每帧刷屏：失败的模块只报一次并停用。
            foreach (IRuntimeModule module in modules)
            {
                if (failedModules.Contains(module))
                    continue;

                try
                {
                    module.Update();
                }
                catch (Exception exception)
                {
                    failedModules.Add(module);
                    Log.LogError($"[SMA] module update failed and was disabled: {module.GetType().Name}: {exception}");
                }
            }
        }

        internal void SceneLoaded(int buildIndex, string sceneName)
        {
            Log.LogInfo($"[SMA] scene-loaded name={sceneName} buildIndex={buildIndex}");
            foreach (IRuntimeModule module in modules)
                module.SceneLoaded(buildIndex, sceneName);
        }

        internal void SceneUnloaded(int buildIndex, string sceneName)
        {
            Log.LogInfo($"[SMA] scene-unloaded name={sceneName} buildIndex={buildIndex}");
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

    // BepInEx 没有每帧与场景回调；这个注入组件驱动模块的 Update，并把已加载场景集合的差集
    // 报成加载/卸载事件。Sprocket 用加法加载切换设置、暂停等界面，只看活动场景会漏掉它们。
    internal sealed class ApiDriver : MonoBehaviour
    {
        private static readonly BepInEx.Logging.ManualLogSource DriverLog =
            BepInEx.Logging.Logger.CreateLogSource("Sprocket Mod API");

        private readonly Dictionary<int, LoadedScene> loadedScenes = new();
        private readonly List<int> staleScenes = new();
        private bool sceneProbeLogged;

        private ApiMod? host;

        public ApiDriver(IntPtr ptr) : base(ptr)
        {
        }

        // 带托管参数的成员注册不进 il2cpp 域，只从托管侧调用。
        [HideFromIl2Cpp]
        public void Configure(ApiMod mod) => host = mod;

        private void Update()
        {
            ApiMod? current = host;
            if (current == null)
                return;

            current.Tick();
            SyncScenes(current);
        }

        private void SyncScenes(ApiMod current)
        {
            if (!sceneProbeLogged)
            {
                sceneProbeLogged = true;
                DriverLog.LogInfo($"[SMA] scene probe sceneCount={SceneManager.sceneCount}");
            }

            staleScenes.Clear();
            foreach (KeyValuePair<int, LoadedScene> pair in loadedScenes)
            {
                if (!IsLoaded(pair.Key))
                    staleScenes.Add(pair.Key);
            }

            foreach (int handle in staleScenes)
            {
                LoadedScene scene = loadedScenes[handle];
                loadedScenes.Remove(handle);
                current.SceneUnloaded(scene.BuildIndex, scene.Name);
            }

            int count = SceneManager.sceneCount;
            for (int index = 0; index < count; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                int handle = scene.handle;
                if (loadedScenes.ContainsKey(handle))
                    continue;

                string name = scene.name;
                int buildIndex = scene.buildIndex;
                loadedScenes[handle] = new LoadedScene(buildIndex, name);
                current.SceneLoaded(buildIndex, name);
            }
        }

        private static bool IsLoaded(int handle)
        {
            int count = SceneManager.sceneCount;
            for (int index = 0; index < count; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.IsValid() && scene.isLoaded && scene.handle == handle)
                    return true;
            }

            return false;
        }

        private readonly struct LoadedScene
        {
            internal LoadedScene(int buildIndex, string name)
            {
                BuildIndex = buildIndex;
                Name = name;
            }

            internal int BuildIndex { get; }
            internal string Name { get; }
        }
    }
}

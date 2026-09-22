using System;
using System.IO;
using MelonLoader.Utils;

namespace SprocketModAPI
{
    // 声明式配置模块：向注册表提供 `IModConfigService`。
    // 持久化位置为 `UserData/SprocketModAPI/modconfig/<modId>.json`，每个模组一个文件。
    // 它还负责 API **自身**的诊断设置（`ApiSelfSettings`），所以必须最先初始化。
    internal sealed class ModConfigModule : IRuntimeModule
    {
        private ModConfigService? service;
        private ApiSelfSettings? selfSettings;
        private IDisposable? registration;

        public void Initialize(RuntimeModuleContext context)
        {
            string root = Path.Combine(MelonEnvironment.UserDataDirectory, "SprocketModAPI", "modconfig");
            service = new ModConfigService(root, context.Warn);
            registration = context.Services.Register<IModConfigService>(service);

            try
            {
                selfSettings = new ApiSelfSettings(service, context.Warn);
            }
            catch (Exception exception)
            {
                context.Warn($"[SMA-CONFIG] the API's own config page is unavailable: {exception.Message}");
            }
        }

        public void Update() { }
        public void SceneLoaded(int buildIndex, string sceneName) { }
        public void SceneUnloaded(int buildIndex, string sceneName) { }

        public void Dispose()
        {
            selfSettings?.Dispose();
            selfSettings = null;

            registration?.Dispose();
            registration = null;
            service?.Dispose();
            service = null;
        }
    }
}

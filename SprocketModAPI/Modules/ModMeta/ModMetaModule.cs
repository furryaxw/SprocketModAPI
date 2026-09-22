using System;
using MelonLoader;

namespace SprocketModAPI
{
    // 模组元数据模块：向注册表提供 `IModMetadataService`。
    // 快照在初始化时建立；已注册模组数量变化（后续加载或注销）时在 `Update` 中重建。
    internal sealed class ModMetaModule : IRuntimeModule
    {
        private ModMetadataService? service;
        private IDisposable? registration;
        private int lastRegisteredCount = -1;

        public void Initialize(RuntimeModuleContext context)
        {
            service = new ModMetadataService(new MelonLoaderModSource(context.Warn));
            service.Refresh();
            lastRegisteredCount = SafeRegisteredCount();
            registration = context.Services.Register<IModMetadataService>(service);
        }

        public void Update()
        {
            ModMetadataService? current = service;
            if (current == null)
                return;

            int count = SafeRegisteredCount();
            if (count != lastRegisteredCount)
            {
                lastRegisteredCount = count;
                current.Refresh();
            }
        }

        public void SceneLoaded(int buildIndex, string sceneName) { }
        public void SceneUnloaded(int buildIndex, string sceneName) { }

        public void Dispose()
        {
            registration?.Dispose();
            registration = null;
            service?.Dispose();
            service = null;
        }

        private static int SafeRegisteredCount()
        {
            try
            {
                return MelonBase.RegisteredMelons.Count;
            }
            catch (Exception)
            {
                // MelonLoader 尚未完成注册时不应让 API 初始化失败。
                return -1;
            }
        }
    }
}

using System;
using BepInEx.Unity.IL2CPP;

namespace SprocketModAPI
{
    // 模组元数据模块：向注册表提供 `IModMetadataService`。
    // 快照在初始化时建立；已加载插件数量变化时在 `Update` 中重建。
    internal sealed class ModMetaModule : IRuntimeModule
    {
        private ModMetadataService? service;
        private IDisposable? registration;
        private int lastPluginCount = -1;

        public void Initialize(RuntimeModuleContext context)
        {
            service = new ModMetadataService(new BepInExPluginSource(context.Warn));
            service.Refresh();
            lastPluginCount = SafePluginCount();
            registration = context.Services.Register<IModMetadataService>(service);
        }

        public void Update()
        {
            ModMetadataService? current = service;
            if (current == null)
                return;

            int count = SafePluginCount();
            if (count != lastPluginCount)
            {
                lastPluginCount = count;
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

        private static int SafePluginCount()
        {
            try
            {
                return IL2CPPChainloader.Instance.Plugins.Count;
            }
            catch (Exception)
            {
                // 链加载器尚未完成注册时不应让 API 初始化失败。
                return -1;
            }
        }
    }
}

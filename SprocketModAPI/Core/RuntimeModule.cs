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
        internal RuntimeModuleContext(ServiceRegistry services, Action<string> warn, Action<string> error, Action<string>? info = null)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Warn = warn ?? throw new ArgumentNullException(nameof(warn));
            Error = error ?? throw new ArgumentNullException(nameof(error));
            Info = info ?? (_ => { });
        }

        internal ServiceRegistry Services { get; }
        internal Action<string> Warn { get; }
        internal Action<string> Error { get; }
        internal Action<string> Info { get; }
    }

    // 模块装载/卸载的公共生命周期。
    // 单个模块初始化失败必须被隔离：其余模块仍然初始化并对外提供服务，
    // 否则一个未验收的新模块会把整个 API 一起拖垮。
    internal static class RuntimeModuleHost
    {
        // 依次初始化；返回初始化失败的模块数量。
        internal static int InitializeAll(IRuntimeModule[] modules, RuntimeModuleContext context)
        {
            if (modules == null)
                throw new ArgumentNullException(nameof(modules));
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            int failures = 0;
            foreach (IRuntimeModule module in modules)
            {
                try
                {
                    module.Initialize(context);
                }
                catch (Exception exception)
                {
                    failures++;
                    context.Error($"[SMA] Module initialization failed: {Describe(module)}: {exception}");
                }
            }

            return failures;
        }

        // 逆序销毁所有模块；单个模块的销毁异常同样被隔离。
        internal static void ShutdownAll(IRuntimeModule[] modules, RuntimeModuleContext context)
        {
            if (modules == null || context == null)
                return;

            for (int index = modules.Length - 1; index >= 0; index--)
            {
                try
                {
                    modules[index].Dispose();
                }
                catch (Exception exception)
                {
                    context.Error($"[SMA] Module shutdown failed: {Describe(modules[index])}: {exception}");
                }
            }
        }

        private static string Describe(IRuntimeModule? module)
            => module == null ? "<null>" : module.GetType().Name;
    }
}

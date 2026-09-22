using System;
using System.Collections.Generic;
using SprocketModAPI;

internal static class RuntimeModuleHostTests
{
    internal static void Run()
    {
        CheckFailingModuleIsIsolated();
        CheckShutdownIsolatesAndReverses();
        CheckNullArguments();
    }

    // 一个模块初始化失败时，后续模块仍必须初始化，并且失败被记录。
    private static void CheckFailingModuleIsIsolated()
    {
        var registry = new ServiceRegistry(_ => { });
        var errors = new List<string>();
        var context = new RuntimeModuleContext(registry, _ => { }, errors.Add, _ => { });

        var exploding = new FakeModule("exploding", throwOnInitialize: true);
        var healthy = new FakeModule("healthy");
        var third = new FakeModule("third");

        int failures = RuntimeModuleHost.InitializeAll(new IRuntimeModule[] { exploding, healthy, third }, context);

        Check(failures == 1, "exactly one module failure is counted");
        Check(!exploding.Initialized, "the failing module is not reported as initialized");
        Check(healthy.Initialized && third.Initialized, "modules after the failing one still initialize");
        Check(errors.Count == 1 && errors[0].Contains("exploding") && errors[0].Contains("Module initialization failed"),
            "the failing module is named in the error log");

        registry.Dispose();
    }

    // 销毁按逆序进行，且单个模块的销毁异常不影响其他模块。
    private static void CheckShutdownIsolatesAndReverses()
    {
        var registry = new ServiceRegistry(_ => { });
        var errors = new List<string>();
        var context = new RuntimeModuleContext(registry, _ => { }, errors.Add);

        var first = new FakeModule("first");
        var second = new FakeModule("second", throwOnDispose: true);
        var third = new FakeModule("third");
        FakeModule.ResetDisposeOrder();
        RuntimeModuleHost.ShutdownAll(new IRuntimeModule[] { first, second, third }, context);

        Check(first.Disposed && second.Disposed && third.Disposed, "every module is disposed");
        Check(third.DisposeOrder == 1 && second.DisposeOrder == 2 && first.DisposeOrder == 3,
            "modules are disposed in reverse order");
        Check(errors.Count == 1 && errors[0].Contains("second") && errors[0].Contains("Module shutdown failed"),
            "a failing dispose is logged and isolated");

        registry.Dispose();
    }

    private static void CheckNullArguments()
    {
        var registry = new ServiceRegistry(_ => { });
        var context = new RuntimeModuleContext(registry, _ => { }, _ => { });
        bool rejected = false;
        try { RuntimeModuleHost.InitializeAll(null!, context); }
        catch (ArgumentNullException) { rejected = true; }
        Check(rejected, "a null module array is rejected");

        RuntimeModuleHost.ShutdownAll(Array.Empty<IRuntimeModule>(), context);
        RuntimeModuleHost.ShutdownAll(null!, context);
        Check(true, "shutdown tolerates empty and null module lists");
        registry.Dispose();
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException($"Contract failed: {name}");
    }

    private sealed class FakeModule : IRuntimeModule
    {
        private readonly bool throwOnInitialize;
        private readonly bool throwOnDispose;
        private static int disposeCounter;

        internal static void ResetDisposeOrder() => disposeCounter = 0;

        internal FakeModule(string name, bool throwOnInitialize = false, bool throwOnDispose = false)
        {
            Name = name;
            this.throwOnInitialize = throwOnInitialize;
            this.throwOnDispose = throwOnDispose;
        }

        internal string Name { get; }
        internal bool Initialized { get; private set; }
        internal bool Disposed { get; private set; }
        internal int DisposeOrder { get; private set; }

        public void Initialize(RuntimeModuleContext context)
        {
            if (throwOnInitialize)
                throw new InvalidOperationException($"{Name} exploded during initialize");
            Initialized = true;
        }

        public void Update() { }
        public void SceneLoaded(int buildIndex, string sceneName) { }
        public void SceneUnloaded(int buildIndex, string sceneName) { }

        public void Dispose()
        {
            Disposed = true;
            DisposeOrder = ++disposeCounter;
            if (throwOnDispose)
                throw new InvalidOperationException($"{Name} exploded during dispose");
        }
    }
}

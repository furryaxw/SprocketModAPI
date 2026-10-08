using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;

namespace SprocketModAPI
{
    // 插件侧的帧/GUI/场景回调与协程宿主。BepInEx 不给模组帧循环，API 的驱动组件是进程里唯一的宿主，
    // 所以插件的每帧工作登记在这里，而不是各自注入组件；回调抛异常时该登记被停用并只报一次。
    public interface IModRuntimeService
    {
        // 登记返回注销句柄；Dispose 之后不再回调。动作必须留在主线程。
        IDisposable AddUpdate(Action tick);
        // 每帧 LateUpdate 阶段，用于必须排在其他系统之后的修正。
        IDisposable AddLateUpdate(Action lateTick);
        IDisposable AddGui(Action draw);
        // changed(新场景名, 上一个场景名)；切换与卸载都按加载/卸载顺序成对报出。
        IDisposable AddSceneChanged(Action<string, string> changed);
        // 进程退出时按登记顺序回调，用于释放插件自己的资源（每帧回调不再送达之后才触发）。
        IDisposable AddShutdown(Action shutdown);
        // 协程跑在驱动组件上，返回值交给 StopCoroutine；routine 为空或宿主已停用时返回 null。
        object? StartCoroutine(IEnumerator? routine);
        void StopCoroutine(object? handle);
    }

    // 统一的日志出口：显示名进 BepInEx 日志源，其余渠道（文件、控制台）由加载器决定。
    public interface IModLogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }

    public interface IModLogService
    {
        IModLogger Create(string displayName);
    }

    // 帧/GUI/场景回调的登记表。这里不碰 Unity，只做分发与隔离，所以可以离线测。
    internal sealed class ModRuntimeHost
    {
        private readonly Action<string> error;
        private readonly List<Entry> updates = new();
        private readonly List<Entry> lateUpdates = new();
        private readonly List<Entry> guis = new();
        private readonly List<Entry> sceneChanges = new();
        private readonly List<Entry> shutdowns = new();
        private bool disposed;

        internal ModRuntimeHost(Action<string> error)
        {
            this.error = error ?? throw new ArgumentNullException(nameof(error));
        }

        internal IDisposable AddUpdate(Action tick) => Add(updates, tick);

        internal IDisposable AddLateUpdate(Action lateTick) => Add(lateUpdates, lateTick);

        internal IDisposable AddGui(Action draw) => Add(guis, draw);

        internal IDisposable AddSceneChanged(Action<string, string> changed)
        {
            if (changed == null)
                throw new ArgumentNullException(nameof(changed));
            Entry entry = Add(sceneChanges, null);
            entry.SceneChanged = changed;
            return entry;
        }

        internal IDisposable AddShutdown(Action shutdown) => Add(shutdowns, shutdown);

        internal void Tick() => Dispatch(updates);

        internal void LateTick() => Dispatch(lateUpdates);

        internal void Draw() => Dispatch(guis);

        internal void SceneChanged(string sceneName, string previousSceneName)
        {
            if (disposed)
                return;
            foreach (Entry entry in Snapshot(sceneChanges))
            {
                if (entry.Disposed)
                    continue;
                try
                {
                    entry.SceneChanged!(sceneName ?? "", previousSceneName ?? "");
                }
                catch (Exception exception)
                {
                    Retire(sceneChanges, entry, exception);
                }
            }
        }

        internal void Shutdown()
        {
            if (disposed)
                return;
            foreach (Entry entry in Snapshot(shutdowns))
            {
                if (entry.Disposed || entry.Update == null)
                    continue;
                try
                {
                    entry.Update();
                }
                catch (Exception exception)
                {
                    Retire(shutdowns, entry, exception);
                }
            }
        }

        internal void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            updates.Clear();
            lateUpdates.Clear();
            guis.Clear();
            sceneChanges.Clear();
            shutdowns.Clear();
        }

        private Entry Add(List<Entry> target, Action? callback)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ModRuntimeHost));
            var entry = new Entry(target, callback);
            target.Add(entry);
            return entry;
        }

        private void Dispatch(List<Entry> target)
        {
            if (disposed)
                return;
            foreach (Entry entry in Snapshot(target))
            {
                if (entry.Disposed || entry.Update == null)
                    continue;
                try
                {
                    entry.Update();
                }
                catch (Exception exception)
                {
                    Retire(target, entry, exception);
                }
            }
        }

        private void Retire(List<Entry> target, Entry entry, Exception exception)
        {
            entry.Disposed = true;
            target.Remove(entry);
            error($"[SMA] runtime callback disabled after failure: {exception}");
        }

        private static Entry[] Snapshot(List<Entry> source)
        {
            var copy = new Entry[source.Count];
            source.CopyTo(copy);
            return copy;
        }

        private sealed class Entry : IDisposable
        {
            private readonly List<Entry> target;

            internal Entry(List<Entry> target, Action? update)
            {
                this.target = target;
                Update = update;
            }

            internal Action? Update { get; }
            internal Action<string, string>? SceneChanged { get; set; }
            internal bool Disposed { get; set; }

            public void Dispose()
            {
                if (Disposed)
                    return;
                Disposed = true;
                target.Remove(this);
            }
        }
    }

    internal sealed class ModRuntimeService : IModRuntimeService
    {
        private readonly ModRuntimeHost host;
        private readonly Func<IEnumerator, object?> startCoroutine;
        private readonly Action<object?> stopCoroutine;

        internal ModRuntimeService(
            ModRuntimeHost host,
            Func<IEnumerator, object?> startCoroutine,
            Action<object?> stopCoroutine)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.startCoroutine = startCoroutine ?? throw new ArgumentNullException(nameof(startCoroutine));
            this.stopCoroutine = stopCoroutine ?? throw new ArgumentNullException(nameof(stopCoroutine));
        }

        public IDisposable AddUpdate(Action tick)
            => host.AddUpdate(tick ?? throw new ArgumentNullException(nameof(tick)));

        public IDisposable AddLateUpdate(Action lateTick)
            => host.AddLateUpdate(lateTick ?? throw new ArgumentNullException(nameof(lateTick)));

        public IDisposable AddGui(Action draw)
            => host.AddGui(draw ?? throw new ArgumentNullException(nameof(draw)));

        public IDisposable AddSceneChanged(Action<string, string> changed)
            => host.AddSceneChanged(changed);

        public IDisposable AddShutdown(Action shutdown)
            => host.AddShutdown(shutdown ?? throw new ArgumentNullException(nameof(shutdown)));

        public object? StartCoroutine(IEnumerator? routine)
            => routine == null ? null : startCoroutine(routine);

        public void StopCoroutine(object? handle)
        {
            if (handle != null)
                stopCoroutine(handle);
        }
    }

    internal sealed class ModLogService : IModLogService
    {
        public IModLogger Create(string displayName)
            => new ModLogger(Logger.CreateLogSource(
                string.IsNullOrEmpty(displayName) ? "Sprocket Mod" : displayName));
    }

    internal sealed class ModLogger : IModLogger
    {
        private readonly ManualLogSource source;

        internal ModLogger(ManualLogSource source)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public void Info(string message) => source.LogInfo(message);

        public void Warn(string message) => source.LogWarning(message);

        public void Error(string message) => source.LogError(message);
    }
}

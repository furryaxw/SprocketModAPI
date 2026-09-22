using System;

namespace SprocketModAPI
{
    // UI 诊断日志。开关值来自 API 自身的诊断设置（`ApiSelfSettings`），**每次调用都重新读**，
    // 所以在游戏里改开关立刻生效，不需要重启，也没有第二份状态。
    // 配置页未就绪（`ApiSelfSettings.Current` 为 null）时退化为"全关"。
    internal sealed class UiDebugLog
    {
        private readonly ApiSelfSettings? settings;
        private readonly Action<string> write;

        internal UiDebugLog(ApiSelfSettings? settings, Action<string> write)
        {
            this.settings = settings;
            this.write = write;
        }

        internal void Lifecycle(string message)
        {
            if (settings != null && settings.UiDebug && settings.UiDebugLifecycle)
                write($"[SMA-UI-TRACE] {message}");
        }

        internal void EveryFrame(string message)
        {
            if (settings != null && settings.UiDebug && settings.UiDebugEveryFrame)
                write($"[SMA-UI-TRACE] {message}");
        }
    }

    internal sealed class UiStatusBroadcaster : IDisposable
    {
        private readonly Action<string> warn;
        private bool disposed;
        internal UiStatusBroadcaster(Action<string> warn) { this.warn = warn; }
        internal event EventHandler<UiStatusChangedEventArgs>? StatusChanged;
        internal void Publish(object sender, UiStatusChangedEventArgs args)
        {
            if (disposed) return;
            EventHandler<UiStatusChangedEventArgs>? subscribers = StatusChanged;
            if (subscribers == null) return;
            foreach (EventHandler<UiStatusChangedEventArgs> subscriber in subscribers.GetInvocationList())
            {
                try { subscriber(sender, args); }
                catch (Exception exception) { warn($"[SMA-UI] status subscriber failed: {exception}"); }
            }
        }
        public void Dispose() { disposed = true; StatusChanged = null; }
    }
}

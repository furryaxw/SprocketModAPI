using System;

namespace SprocketModAPI
{
    // 键位诊断日志。开关值来自 API 自身的诊断设置（`ApiSelfSettings`），每次调用都重新读，
    // 所以改开关立刻生效；配置页未就绪时退化为"全关"。
    internal sealed class KeybindingDebugLog
    {
        private readonly ApiSelfSettings? settings;
        private readonly Action<string> write;

        internal KeybindingDebugLog(ApiSelfSettings? settings, Action<string> write)
        {
            this.settings = settings;
            this.write = write;
        }

        private bool MasterOn => settings != null && settings.KeybindingDebug;

        internal bool Enabled => MasterOn;
        internal bool LogEveryFrame => MasterOn && settings!.KeybindingDebugEveryFrame;

        internal void Routing(string message)
        {
            if (MasterOn && settings!.KeybindingDebugRouting)
                write($"[SMA-KEY-ROUTE] {message}");
        }

        internal void Binding(string message, bool force = false)
        {
            if (!MasterOn || !settings!.KeybindingDebugBindings)
                return;
            if (force || settings.KeybindingDebugEveryFrame)
                write($"[SMA-KEY] {message}");
        }
    }
}

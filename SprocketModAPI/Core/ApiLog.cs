using System;
using System.Collections.Generic;

namespace SprocketModAPI
{
    // 统一的日志出口。规则：
    //
    // * `error`：功能已经坏掉/被停用（模块初始化失败、UI 因异常停用、切换失败）。
    // * `warn`：降级但仍可用、且用户能处理的情况；**同一条消息整个会话只报一次**。
    // * `info`：生命周期里程碑（初始化完成、入口挂上、菜单开/关、配置页注册）——
    // 验收脚本会 grep 这些行，所以不要拿它当进度条。
    // * `debug`：诊断信息，默认全部关闭，只有用户打开 API 自身的诊断开关才输出（见 `ApiSelfSettings`）。
    //
    // 另外：**禁止每帧日志**、禁止在逐文件扫描循环里刷 warn/info、禁止同一条件反复报。
    // 这个类负责去重，调用方只要选对级别。
    internal sealed class ApiLog
    {
        private readonly Action<string> warn;
        private readonly Action<string> error;
        private readonly Action<string> info;
        private readonly HashSet<string> alreadyWarned = new(StringComparer.Ordinal);
        private readonly HashSet<string> alreadyErrored = new(StringComparer.Ordinal);
        private readonly HashSet<string> alreadyLogged = new(StringComparer.Ordinal);

        internal ApiLog(Action<string> warn, Action<string> error, Action<string> info)
        {
            this.warn = warn ?? throw new ArgumentNullException(nameof(warn));
            this.error = error ?? throw new ArgumentNullException(nameof(error));
            this.info = info ?? throw new ArgumentNullException(nameof(info));
        }

        // 生命周期里程碑：每次都报（但调用方要保证它确实只在生命周期事件里出现）。
        internal void Info(string message) => info(message);

        // 错误：同一个消息只报一次，避免同一个坏条件在每帧/每次刷新里刷屏。
        internal void Error(string message)
        {
            if (alreadyErrored.Add(message))
                error(message);
        }

        // 警告：同上，同一条消息只报一次。
        internal void Warn(string message)
        {
            if (alreadyWarned.Add(message))
                warn(message);
        }

        // 诊断：只有用户开了对应开关才输出；由调用方在需要时做频率控制（例如逐帧开关）。
        internal void Debug(string message, bool enabled)
        {
            if (enabled)
                info(message);
        }

        // 诊断（按消息去重）：适合"只在第一次发生时想看一眼"的诊断。
        internal void DebugOnce(string message, bool enabled)
        {
            if (enabled && alreadyLogged.Add(message))
                info(message);
        }

        // 只有一条 `warn` 出口的调用方（只读扫描、观察器）用它包一层，拿到去重能力。
        internal static ApiLog FromWarn(Action<string> warn)
        {
            var sink = warn ?? throw new ArgumentNullException(nameof(warn));
            return new ApiLog(sink, sink, sink);
        }
    }
}

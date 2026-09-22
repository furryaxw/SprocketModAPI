using System;
using System.Collections.Generic;
using SprocketModAPI;

// 日志纪律的离线契约：去重、诊断开关、生命周期里程碑不被吞掉。
internal static class ApiLogTests
{
    internal static void Run()
    {
        var warn = new List<string>();
        var error = new List<string>();
        var info = new List<string>();
        var log = new ApiLog(warn.Add, error.Add, info.Add);

        log.Warn("[SMA-KEY] first");
        log.Warn("[SMA-KEY] first");
        log.Warn("[SMA-KEY] second");
        Check(warn.Count == 2 && warn[0] == "[SMA-KEY] first" && warn[1] == "[SMA-KEY] second",
            "the same warning is reported exactly once");

        log.Error("[SMA] boom");
        log.Error("[SMA] boom");
        Check(error.Count == 1, "the same error is reported exactly once");

        log.Info("[SMA-MENU] menu opened");
        log.Info("[SMA-MENU] menu closed");
        Check(info.Count == 2, "lifecycle milestones are never de-duplicated");

        var debug = new List<string>();
        var quiet = new ApiLog(debug.Add, debug.Add, debug.Add);
        quiet.Debug("[SMA-UI-TRACE] hidden", enabled: false);
        Check(debug.Count == 0, "diagnostics stay silent while their switch is off");
        quiet.Debug("[SMA-UI-TRACE] visible", enabled: true);
        quiet.Debug("[SMA-UI-TRACE] visible", enabled: true);
        quiet.DebugOnce("[SMA-UI-TRACE] once", enabled: true);
        quiet.DebugOnce("[SMA-UI-TRACE] once", enabled: true);
        Check(debug.Count == 3, "diagnostics obey the switch; DebugOnce also de-duplicates");

        var viaWarn = new List<string>();
        ApiLog.FromWarn(viaWarn.Add).Warn("[SMA-KEY] single sink");
        Check(viaWarn.Count == 1, "FromWarn wraps a single sink");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Contract failed: {name}");
    }
}

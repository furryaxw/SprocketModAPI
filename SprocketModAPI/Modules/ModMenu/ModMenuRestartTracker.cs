using System;
using System.Collections.Generic;

namespace SprocketModAPI
{
    // 本次启动内的启停记录。基准是某个模组第一次被点击前的磁盘状态：
    // 只有当前状态与基准不同的模组才需要重启，同一个模组先禁再启回到原样就不算改动。
    //
    // 纯数据、不依赖 UnityEngine，离线合约可以直接覆盖（见 `ModMenuRestartTests`）。
    internal sealed class ModMenuRestartTracker
    {
        private readonly Dictionary<string, bool> baseline = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> pending = new(StringComparer.OrdinalIgnoreCase);

        // 需要重启才生效的模组数；0 表示磁盘状态与启动时一致。
        internal int PendingCount => pending.Count;

        internal bool RestartPending => pending.Count != 0;

        // 记住点击前的状态；同一个身份再次进入时保留最初的基准，来回切换始终对齐同一份记录。
        internal void Record(string identity, bool disabledBeforeClick)
        {
            if (!string.IsNullOrEmpty(identity) && !baseline.ContainsKey(identity))
                baseline[identity] = disabledBeforeClick;
        }

        // 改名成功后登记新状态：回到基准即撤销待重启。
        internal void Update(string identity, bool disabledAfterClick)
        {
            if (string.IsNullOrEmpty(identity))
                return;

            if (baseline.TryGetValue(identity, out bool started) && started == disabledAfterClick)
                pending.Remove(identity);
            else
                pending.Add(identity);
        }
    }
}

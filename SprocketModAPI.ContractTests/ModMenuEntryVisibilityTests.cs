using System;

namespace SprocketModAPI
{
    // 「设置 → General」入口显示门禁的真值表（纯逻辑，离线可断言）。
    //
    // 这两种毛病在离线环境看不出来，所以必须写成断言：按钮该出现时不出现（门禁过严）、
    // 或者盖在菜单窗口上抢点击（门禁过松）。
    internal static class ModMenuEntryVisibilityTests
    {
        internal static void Run()
        {
            CheckEntryOnlyShowsOnTheGeneralPage();
            CheckOpenMenuHidesTheEntry();
            CheckAlignmentGate();
        }

        private static void CheckEntryOnlyShowsOnTheGeneralPage()
        {
            Check(ModMenuEntryVisibility.ShouldShow(true, true, false),
                "an active settings menu on the General page shows the entry");
            Check(!ModMenuEntryVisibility.ShouldShow(true, false, false),
                "switching to another settings page hides the entry");
            Check(!ModMenuEntryVisibility.ShouldShow(false, true, false),
                "leaving the settings menu hides the entry");
            Check(!ModMenuEntryVisibility.ShouldShow(false, false, false),
                "a closed settings menu never shows the entry");
        }

        private static void CheckOpenMenuHidesTheEntry()
        {
            // 菜单窗口打开时入口必须消失：否则两者互相遮挡，而且入口会抢走窗口上的点击。
            Check(!ModMenuEntryVisibility.ShouldShow(true, true, true),
                "an open mod menu hides the settings entry");
            Check(!ModMenuEntryVisibility.ShouldShow(false, false, true),
                "an open mod menu keeps the entry hidden even after leaving the settings menu");
        }

        private static void CheckAlignmentGate()
        {
            // 没对齐到原生按钮左边界之前不许 SetActive：否则按钮会停在画布原点（画面外）或闪一下。
            Check(ModMenuEntryVisibility.ShouldApply(true, true),
                "the entry shows once it is requested and aligned");
            Check(!ModMenuEntryVisibility.ShouldApply(true, false),
                "a requested but unaligned entry stays hidden");
            Check(!ModMenuEntryVisibility.ShouldApply(false, true),
                "an unrequested entry stays hidden even when aligned");
            Check(!ModMenuEntryVisibility.ShouldApply(false, false),
                "neither requested nor aligned means hidden");
        }

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException($"Contract failed: {name}");
        }
    }
}

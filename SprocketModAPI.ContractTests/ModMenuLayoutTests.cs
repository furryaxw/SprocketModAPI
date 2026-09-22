using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SprocketModAPI
{
    // Mod 菜单「视觉设计」的离线契约：窗口几何不变量（纯数据求值，不加载 UnityEngine）。
    //
    // 为什么值得测：这些数字决定"看起来对不对"，改错一个常量（例如把列表拉宽到挤掉详情列、
    // 把窗口撑出画布）会在契约套件里当场失败。全部是纯数据求值，不加载 UnityEngine。
    internal static class ModMenuLayoutTests
    {
        internal static void Run()
        {
            CheckCanvasAndWindowFit();
            CheckContentColumns();
            CheckVerticalStack();
            CheckRowAndControlMetrics();
        }

        private static void CheckCanvasAndWindowFit()
        {
            Check(Math.Abs(ModMenuLayout.CanvasWidth - 1122.519685f) < 0.01f
                && Math.Abs(ModMenuLayout.CanvasHeight - 793.700787f) < 0.01f,
                "the mod menu canvas keeps the confirmed reference resolution");
            Check(ModMenuLayout.FitsInCanvas,
                "the window (including its top inset) must fit inside the reference canvas");
            Check(ModMenuLayout.WindowInnerWidth > 0f && ModMenuLayout.WindowInnerWidth < ModMenuLayout.WindowWidth,
                "window padding leaves a positive inner width smaller than the window");
            Check(ModMenuLayout.ContentWidth <= ModMenuLayout.WindowInnerWidth,
                "content must never be wider than the window minus padding");
        }

        private static void CheckContentColumns()
        {
            Check(Math.Abs(ModMenuLayout.DetailWidth - 578f) < 0.01f,
                "the detail column keeps its designed width (content - list - spacing)");
            Check(ModMenuLayout.DetailWidth >= 420f,
                "the detail column must stay wide enough for label/value rows");
            Check(ModMenuLayout.ListWidth >= 300f,
                "the mod list must stay wide enough for a display name plus chips");
            Check(Math.Abs(ModMenuLayout.ListWidth + ModMenuLayout.SplitSpacing + ModMenuLayout.DetailWidth
                - ModMenuLayout.ContentWidth) < 0.01f,
                "list + spacing + detail must add up to the content width exactly (no overlap, no gap)");
        }

        private static void CheckVerticalStack()
        {
            Check(ModMenuLayout.VerticalStackHeight + (ModMenuLayout.WindowPadding * 2f) <= ModMenuLayout.WindowHeight,
                "header + search + content + footer must fit the window with its padding");
            Check(ModMenuLayout.ContentHeight >= ModMenuLayout.ListRowHeight * 8f,
                "the list area must show at least eight rows before scrolling");
            Check(ModMenuLayout.VisibleListRows >= 8,
                "VisibleListRows derives at least eight rows from the content height");
        }

        private static void CheckRowAndControlMetrics()
        {
            Check(ModMenuLayout.ListRowHeight >= 32f && ModMenuLayout.DetailRowHeight >= 22f,
                "list and detail rows keep a legible minimum height");
            Check(ModMenuLayout.SectionRowHeight >= ModMenuLayout.DetailRowHeight,
                "section headers are at least as tall as detail rows");
            Check(ModMenuLayout.ActionButtonWidth >= 88f && ModMenuLayout.SmallButtonWidth >= 64f
                && ModMenuLayout.NumberFieldWidth >= 64f && ModMenuLayout.StepButtonWidth >= 28f,
                "action, small, number and step controls keep usable minimum widths");
            Check(ModMenuLayout.CloseWidth <= ModMenuLayout.ContentWidth
                && ModMenuLayout.RefreshWidth <= ModMenuLayout.ContentWidth,
                "footer buttons are narrower than the content area");
            Check(ModMenuLayout.ScrollbarWidth > 0f && ModMenuLayout.ScrollbarWidth <= 16f,
                "the scrollbar stays thin");
            Check(ModMenuLayout.InputHeight >= 24f && ModMenuLayout.InputPadding > 0f
                && ModMenuLayout.InputHeight <= ModMenuLayout.SectionRowHeight,
                "config input rows are tall enough to hit and never taller than a section row");
        }

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException($"Contract failed: {name}");
        }
    }
}

namespace SprocketModAPI
{
    // Mod 菜单窗口的几何常量（纯数据，刻意不依赖 UnityEngine）。
    //
    // 为什么要单独一个类：这些数字决定"看起来对不对"，`ModMenuLayoutTests` 据此离线断言
    // "窗口放得进画布""详情列够宽""一屏至少 8 行"这类设计不变量——改错一个常量会当场被契约套件拦住。
    //
    // 这里**没有任何 UnityEngine 类型**：本机 IL2CPP 下 `UnityEngine.Color` 之类的静态构造会直接
    // 抛异常，离线套件根本加载不了（见 `ModMenuStyle` 的同类说明）。
    internal static class ModMenuLayout
    {
        internal const float CanvasWidth = 1122.519685f;
        internal const float CanvasHeight = 793.700787f;
        internal const float WindowWidth = 1062f;
        internal const float WindowHeight = 688f;
        internal const float WindowTop = 52f;
        internal const float WindowPadding = 24f;
        internal const float SectionSpacing = 16f;
        internal const float ContentWidth = 1014f;
        internal const float HeaderHeight = 33.688477f;
        internal const float TitleWidth = 236.617188f;
        internal const float SearchHeight = 40.159180f;
        internal const float SearchSpacing = 12f;
        internal const float SearchButtonWidth = 71.851562f;
        internal const float ContentHeight = 478f;
        internal const float FooterHeight = 38.343750f;
        internal const float FooterSpacing = 16f;
        internal const float CloseWidth = 101.067383f;
        internal const float RefreshWidth = 101.067383f;
        internal const float ScrollbarWidth = 8f;
        internal const float ListWidth = 420f;
        internal const float SplitSpacing = 16f;
        internal const float ListRowHeight = 36f;
        internal const float DetailHeaderHeight = 56f;
        internal const float DetailActionsHeight = 34f;
        internal const float DetailRowHeight = 24f;
        internal const float DetailLabelWidth = 118f;
        internal const float ActionButtonWidth = 108f;
        internal const float SmallButtonWidth = 74f;
        internal const float NumberFieldWidth = 88f;
        internal const float StepButtonWidth = 32f;
        internal const float InputHeight = 28f;
        internal const float PanelPadding = 10f;
        internal const float InputPadding = 12f;
        internal const float ListRowPadding = 12f;
        internal const float SelectionBarWidth = 3f;
        internal const float SectionRowHeight = 30f;

        internal static float DetailWidth => ContentWidth - ListWidth - SplitSpacing;

        internal static float WindowInnerWidth => WindowWidth - (WindowPadding * 2f);

        // 四个区块的高度加它们之间的三段间距：这才是窗口垂直方向要装下的总高，
        // 漏掉间距会让 footer 从窗口底边溢出去。
        internal static float VerticalStackHeight
            => HeaderHeight + SearchHeight + ContentHeight + FooterHeight + (SectionSpacing * 3f);

        internal static bool FitsInCanvas => WindowWidth <= CanvasWidth && WindowTop + WindowHeight <= CanvasHeight;

        internal static int VisibleListRows => (int)(ContentHeight / ListRowHeight);
    }
}

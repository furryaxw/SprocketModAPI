namespace SprocketModAPI
{
    // Mod 菜单的共用观感：入口按钮与窗口按钮从这里取值，"和 Sprocket 原生按钮一致"只有一份定义。
    //
    // ⚠️ 这里**刻意不使用 `UnityEngine.Color`**：在本机的 IL2CPP 环境里 `UnityEngine.Color`
    // 是互操作包装，静态构造会去调 `IL2CPP.GetIl2CppClass`，离线契约套件里根本初始化不了
    // （实测 `Color.white` 直接抛 `TypeInitializationException`）。所以纯数据层用自带的
    // `Rgba`，只在真正写 Unity 组件的地方（`ModMenuSettingsEntry`）转成 `Color`。
    //
    // 两条硬规则（都由离线契约测试守住）：
    // 1. **底图必须是白色，灰色放在 ColorBlock 里**。UGUI 的最终着色 = `Image.color × 状态色 × colorMultiplier`，
    // 两边都塞灰色会变成灰的平方（58/255 → ≈16/255），按钮肉眼上比原生暗一大截。
    // 2. 调色板只用中性灰（R=G=B），不引入强调色。
    internal static class ModMenuStyle
    {
        // 入口尺寸与底边距：运行时会对齐到设置页原生按钮的实际矩形（两个画布缩放比例不同），
        // 这里只是建对象时的初值，也是取不到原生按钮时的回落值——Sprocket 原生按钮实测 148×36。
        internal const float EntryWidth = 148f;
        internal const float EntryHeight = 36f;
        internal const float EntryBottom = 84f;
        internal const float EntryFontSize = 14f;
        internal const string EntryLabel = "MODS";

        // 底图恒为白色：灰色交给 `EntryAppearance` 的状态色。
        internal static readonly Rgba White = new(1f, 1f, 1f, 1f);

        internal static readonly Rgba Surface = Rgb(58, 58, 58);
        internal static readonly Rgba SurfaceHover = Rgb(72, 72, 72);
        internal static readonly Rgba SurfacePressed = Rgb(44, 44, 44);
        internal static readonly Rgba Border = Rgb(90, 90, 90);
        internal static readonly Rgba Text = Rgb(235, 235, 235);
        internal const float FadeDuration = 0.08f;

        // 采到原生按钮时报告的来源；日志与验收脚本靠它区分"真原生"与"回落"。
        internal const string SourceNative = "native";
        internal const string SourceFallback = "fallback";

        // 不依赖 UnityEngine 的颜色值：离线可构造、可断言。
        internal readonly struct Rgba
        {
            internal Rgba(float red, float green, float blue, float alpha)
            {
                Red = red;
                Green = green;
                Blue = blue;
                Alpha = alpha;
            }

            internal float Red { get; }
            internal float Green { get; }
            internal float Blue { get; }
            internal float Alpha { get; }

            internal bool IsGrey
            {
                get
                {
                    float near = 0.001f;
                    return System.Math.Abs(Red - Green) < near && System.Math.Abs(Green - Blue) < near;
                }
            }

            private static bool Near(float left, float right) => System.Math.Abs(left - right) < 0.001f;

            public override string ToString()
                => $"({Red:0.###}, {Green:0.###}, {Blue:0.###}, {Alpha:0.###})";
        }

        // 按钮观感的纯数据快照：采样值与回落值都走这个结构，方便离线断言。
        internal readonly struct EntryAppearance
        {
            internal EntryAppearance(
                Rgba surface,
                Rgba hover,
                Rgba pressed,
                Rgba border,
                Rgba text,
                float fontSize,
                float fadeDuration,
                bool hasOutline,
                bool fromNative)
            {
                Surface = surface;
                Hover = hover;
                Pressed = pressed;
                Border = border;
                Text = text;
                FontSize = fontSize;
                FadeDuration = fadeDuration;
                HasOutline = hasOutline;
                FromNative = fromNative;
            }

            internal Rgba Surface { get; }
            internal Rgba Hover { get; }
            internal Rgba Pressed { get; }
            internal Rgba Border { get; }
            internal Rgba Text { get; }
            internal float FontSize { get; }
            internal float FadeDuration { get; }
            internal bool HasOutline { get; }
            internal bool FromNative { get; }
        }

        // 没有可采样的原生按钮时使用的观感（= 已验收的原生灰按钮观感）。
        internal static EntryAppearance Fallback => new(
            Surface,
            SurfaceHover,
            SurfacePressed,
            Border,
            Text,
            EntryFontSize,
            FadeDuration,
            hasOutline: true,
            fromNative: false);

        // 决定最终观感：能采到原生按钮就用原生的，但**逐字段**回落到原生灰。
        //
        // 为什么要逐字段回落：原生按钮的某些字段本来就可能是"没有意义的值"——`Image` 透明、
        // 状态色 alpha 为 0、字号为 0、没有 `Outline`。整块照抄会做出一个不可见或排版崩掉的按钮，
        // 所以只接受"看起来可用"的字段，其余用回落值补齐。
        internal static EntryAppearance Resolve(bool sampled, EntryAppearance sample)
        {
            EntryAppearance fallback = Fallback;
            if (!sampled)
                return fallback;

            return new EntryAppearance(
                surface: Usable(sample.Surface) ? sample.Surface : fallback.Surface,
                hover: Usable(sample.Hover) ? sample.Hover : fallback.Hover,
                pressed: Usable(sample.Pressed) ? sample.Pressed : fallback.Pressed,
                border: Usable(sample.Border) ? sample.Border : fallback.Border,
                text: Usable(sample.Text) ? sample.Text : fallback.Text,
                fontSize: PlausibleFontSize(sample.FontSize) ? sample.FontSize : fallback.FontSize,
                fadeDuration: PlausibleFadeDuration(sample.FadeDuration) ? sample.FadeDuration : fallback.FadeDuration,
                hasOutline: sample.HasOutline,
                fromNative: true);
        }

        // alpha 为 0 的着色是"不可见"，一律视为没采到。
        internal static bool Usable(Rgba color) => color.Alpha > 0.01f;

        internal static bool PlausibleFontSize(float size) => size >= 6f && size <= 64f;

        internal static bool PlausibleFadeDuration(float duration) => duration > 0f && duration <= 1f;

        internal static Rgba Rgb(int red, int green, int blue)
            => new(red / 255f, green / 255f, blue / 255f, 1f);
    }
}

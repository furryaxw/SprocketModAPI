using System;

namespace SprocketModAPI
{
    // Mod 菜单观感的离线契约：入口按钮的尺寸/底边距、原生灰色调、以及"从原生按钮采样"的回落规则。
    //
    // 这些是**行为**断言（对纯数据求值），不是源码文本匹配；源码布局断言在 Program.CheckSourceLayout 里。
    // 这里完全不碰 `UnityEngine.Color`——它在 IL2CPP 环境下静态构造就会抛异常，离线套件用不了。
    internal static class ModMenuStyleTests
    {
        internal static void Run()
        {
            CheckFallbackIsTheNativeGreyButton();
            CheckSampledButtonWins();
            CheckInvisibleNativeFieldsFallBack();
            CheckNonsenseFontAndFadeFallBack();
            CheckPaletteStaysNeutral();
            CheckDoubleTintGuard();
        }

        private static void CheckFallbackIsTheNativeGreyButton()
        {
            ModMenuStyle.EntryAppearance fallback = ModMenuStyle.Resolve(false, default);
            Check(!fallback.FromNative, "an unsampled entry reports the fallback source");
            Check(Same(fallback.Surface, ModMenuStyle.Surface) && Same(fallback.Hover, ModMenuStyle.SurfaceHover)
                && Same(fallback.Pressed, ModMenuStyle.SurfacePressed), "fallback uses the native button state colours");
            Check(Same(fallback.Surface, ModMenuStyle.Rgb(58, 58, 58))
                && Same(fallback.Hover, ModMenuStyle.Rgb(72, 72, 72))
                && Same(fallback.Pressed, ModMenuStyle.Rgb(44, 44, 44)), "fallback greys are the confirmed native values");
            Check(Same(fallback.Border, ModMenuStyle.Rgb(90, 90, 90)) && Same(fallback.Text, ModMenuStyle.Rgb(235, 235, 235)),
                "fallback border and text match the native button");
            Check(Math.Abs(fallback.FontSize - 14f) < 0.001f && Math.Abs(fallback.FadeDuration - 0.08f) < 0.001f,
                "fallback font size and fade duration match the confirmed native values");
            Check(fallback.HasOutline, "the fallback draws the native 1px border");

            // 明确回落到原生灰时，采样值一律被忽略（防止"采样失败但仍用了半截数据"）。
            var bogus = new ModMenuStyle.EntryAppearance(
                ModMenuStyle.Rgb(200, 20, 20), ModMenuStyle.Rgb(200, 20, 20), ModMenuStyle.Rgb(200, 20, 20),
                ModMenuStyle.Rgb(200, 20, 20), ModMenuStyle.Rgb(200, 20, 20), 33f, 0.5f, false, true);
            ModMenuStyle.EntryAppearance resolved = ModMenuStyle.Resolve(false, bogus);
            Check(Same(resolved.Surface, ModMenuStyle.Surface) && Same(resolved.Text, ModMenuStyle.Text)
                && resolved.HasOutline,
                "an unsampled entry ignores the sample entirely and keeps the native fallback (colours and border)");
        }

        private static void CheckSampledButtonWins()
        {
            var sample = new ModMenuStyle.EntryAppearance(
                ModMenuStyle.Rgb(30, 31, 32), ModMenuStyle.Rgb(40, 41, 42), ModMenuStyle.Rgb(20, 21, 22),
                ModMenuStyle.Rgb(70, 71, 72), ModMenuStyle.Rgb(220, 221, 222), 16f, 0.12f, true, true);
            ModMenuStyle.EntryAppearance resolved = ModMenuStyle.Resolve(true, sample);
            Check(resolved.FromNative, "a usable native sample marks the entry as native");
            Check(Same(resolved.Surface, sample.Surface) && Same(resolved.Hover, sample.Hover)
                && Same(resolved.Pressed, sample.Pressed), "native state colours are copied field by field");
            Check(Math.Abs(resolved.FontSize - 16f) < 0.001f && Math.Abs(resolved.FadeDuration - 0.12f) < 0.001f,
                "native font size and fade duration are copied when plausible");
        }

        private static void CheckInvisibleNativeFieldsFallBack()
        {
            // 原生按钮的底图常常是白色/透明，某些状态色也可能是全透明：整块照抄会做出隐形按钮。
            var sample = new ModMenuStyle.EntryAppearance(
                surface: new ModMenuStyle.Rgba(1f, 1f, 1f, 0f),
                hover: new ModMenuStyle.Rgba(1f, 1f, 1f, 0f),
                pressed: ModMenuStyle.Rgb(20, 21, 22),
                border: new ModMenuStyle.Rgba(0f, 0f, 0f, 0f),
                text: new ModMenuStyle.Rgba(1f, 1f, 1f, 0f),
                fontSize: 0f,
                fadeDuration: 0f,
                hasOutline: false,
                fromNative: true);
            ModMenuStyle.EntryAppearance resolved = ModMenuStyle.Resolve(true, sample);
            Check(Same(resolved.Surface, ModMenuStyle.Surface), "a transparent native surface falls back to the native grey");
            Check(Same(resolved.Hover, ModMenuStyle.SurfaceHover), "a transparent native hover colour falls back");
            Check(Same(resolved.Pressed, sample.Pressed), "a usable native pressed colour is still copied");
            Check(Same(resolved.Border, ModMenuStyle.Border) && Same(resolved.Text, ModMenuStyle.Text),
                "a transparent border and label colour fall back");
            Check(Math.Abs(resolved.FontSize - 14f) < 0.001f, "a zero font size falls back");
            Check(Math.Abs(resolved.FadeDuration - 0.08f) < 0.001f, "a zero fade duration falls back");
            Check(!resolved.HasOutline, "a native button without an outline is not given a fake border");
            Check(resolved.FromNative, "partial sampling still reports the native source");
        }

        private static void CheckNonsenseFontAndFadeFallBack()
        {
            Check(ModMenuStyle.PlausibleFontSize(14f) && !ModMenuStyle.PlausibleFontSize(0f)
                && !ModMenuStyle.PlausibleFontSize(200f), "font size plausibility window");
            Check(ModMenuStyle.PlausibleFadeDuration(0.08f) && !ModMenuStyle.PlausibleFadeDuration(0f)
                && !ModMenuStyle.PlausibleFadeDuration(5f), "fade duration plausibility window");
            Check(!ModMenuStyle.Usable(new ModMenuStyle.Rgba(1f, 1f, 1f, 0f)) && ModMenuStyle.Usable(ModMenuStyle.Surface),
                "alpha decides whether a sampled colour is usable");
        }

        private static void CheckPaletteStaysNeutral()
        {
            ModMenuStyle.Rgba[] palette = { ModMenuStyle.Surface, ModMenuStyle.SurfaceHover, ModMenuStyle.SurfacePressed,
                ModMenuStyle.Border, ModMenuStyle.Text };
            foreach (ModMenuStyle.Rgba color in palette)
                Check(color.IsGrey, $"the mod menu palette stays neutral grey (no accent colour): {color}");

            Check(Math.Abs(ModMenuStyle.EntryWidth - 148f) < 0.001f && Math.Abs(ModMenuStyle.EntryHeight - 36f) < 0.001f,
                "the entry fallback keeps the native 148x36 button size");
            Check(Math.Abs(ModMenuStyle.EntryBottom - 84f) < 0.001f,
                "the entry keeps the 84px bottom inset used by the keybinding entry");
            Check(Math.Abs(ModMenuStyle.EntryFontSize - 14f) < 0.001f, "the entry keeps the native 14px label");
            Check(ModMenuStyle.EntryLabel == "MODS", "the settings entry is labelled MODS");
            Check(ModMenuStyle.SourceNative == "native" && ModMenuStyle.SourceFallback == "fallback",
                "style sources keep stable names for logs and acceptance scripts");
        }

        private static void CheckDoubleTintGuard()
        {
            // UGUI 最终着色 = Image.color × 状态色 × multiplier。底图必须是白色，
            // 否则灰色会被平方（58/255 → ≈16/255），按钮看起来比原生暗一大截。
            Check(Same(ModMenuStyle.White, new ModMenuStyle.Rgba(1f, 1f, 1f, 1f)),
                "the button background must stay white so the grey is not squared");
            Check(!Same(ModMenuStyle.White, ModMenuStyle.Surface),
                "the button background must not be the grey surface colour");
        }

        private static bool Same(ModMenuStyle.Rgba left, ModMenuStyle.Rgba right)
            => Math.Abs(left.Red - right.Red) < 0.001f && Math.Abs(left.Green - right.Green) < 0.001f
               && Math.Abs(left.Blue - right.Blue) < 0.001f && Math.Abs(left.Alpha - right.Alpha) < 0.001f;

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException($"Contract failed: {name}");
        }
    }
}

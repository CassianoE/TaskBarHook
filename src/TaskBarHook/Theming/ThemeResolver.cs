using System.Windows.Media;
using TaskBarHook.Desktop;
using Color = System.Windows.Media.Color;

namespace TaskBarHook.Theming;

public static class ThemeResolver
{
    // Non-text UI (focus ring) must stay ≥ 3:1 on the pixels actually behind
    // it: rest, hover, and pressed. Matches the hover/pressed opacities in
    // SharedResources.xaml (AnimatedIconButtonStyle).
    public const double FocusContrastFloor = 3.0;
    public const double AnimatedHoverOpacity = 0.16;
    public const double AnimatedPressedOpacity = 0.10;

    public static ThemePalette Resolve(ThemeInputs inputs)
    {
        if (inputs.HighContrast)
        {
            return HighContrastPalette();
        }

        // The compact lives in the taskbar, which follows the SYSTEM mode;
        // the apps mode is read explicitly and deliberately not used here.
        return inputs.SystemLight ? LightPalette(inputs.Accent) : DarkPalette(inputs.Accent);
    }

    private static ThemePalette DarkPalette(Color accent)
    {
        var surface = Color.FromRgb(0x1C, 0x1C, 0x1E);
        var ink = Color.FromRgb(0xF3, 0xF3, 0xF3);
        var hoverOverlay = Colors.White;
        var hoverFill = Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF);
        var pressedFill = WithAlpha(accent, 0x38);
        return new ThemePalette(
            "Dark",
            Surface: surface,
            SurfaceAlt: Color.FromRgb(0x2A, 0x2A, 0x2C),
            BorderSubtle: Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF),
            CompactBorder: Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
            TextPrimary: ink,
            TextSecondary: Color.FromRgb(0xB3, 0xB3, 0xB3),
            PlaceholderIcon: Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF),
            HoverOverlay: hoverOverlay,
            PressedOverlay: accent,
            HoverFill: hoverFill,
            PressedFill: pressedFill,
            HoverText: ink,
            FocusRing: FocusRingFor(accent, surface, ink, hoverFill, hoverOverlay, pressedFill),
            ProgressTrack: Color.FromRgb(0x3A, 0x3A, 0x3C),
            ProgressFill: ink,
            ProgressRemainder: Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
            ProgressThumb: ink,
            ErrorText: Color.FromRgb(0xB3, 0xB3, 0xB3),
            HintSurface: Color.FromRgb(0x2A, 0x2A, 0x2C));
    }

    private static ThemePalette LightPalette(Color accent)
    {
        var surface = Color.FromRgb(0xF3, 0xF3, 0xF3);
        var ink = Color.FromRgb(0x1B, 0x1B, 0x1B);
        var hoverOverlay = Colors.Black;
        var hoverFill = Color.FromArgb(0x28, 0x00, 0x00, 0x00);
        var pressedFill = WithAlpha(accent, 0x38);
        return new ThemePalette(
            "Light",
            Surface: surface,
            SurfaceAlt: Color.FromRgb(0xE8, 0xE8, 0xE8),
            BorderSubtle: Color.FromArgb(0x14, 0x00, 0x00, 0x00),
            CompactBorder: Color.FromArgb(0x14, 0x00, 0x00, 0x00),
            TextPrimary: ink,
            TextSecondary: Color.FromRgb(0x5D, 0x5D, 0x5D),
            PlaceholderIcon: Color.FromArgb(0x66, 0x00, 0x00, 0x00),
            HoverOverlay: hoverOverlay,
            PressedOverlay: accent,
            HoverFill: hoverFill,
            PressedFill: pressedFill,
            HoverText: ink,
            FocusRing: FocusRingFor(accent, surface, ink, hoverFill, hoverOverlay, pressedFill),
            ProgressTrack: Color.FromRgb(0xD4, 0xD4, 0xD4),
            ProgressFill: ink,
            ProgressRemainder: Color.FromArgb(0x33, 0x00, 0x00, 0x00),
            ProgressThumb: ink,
            ErrorText: Color.FromRgb(0x6E, 0x6E, 0x6E),
            HintSurface: Colors.White);
    }

    private static ThemePalette HighContrastPalette()
    {
        var window = SysColor(NativeMethods.COLOR_WINDOW);
        var text = SysColor(NativeMethods.COLOR_WINDOWTEXT);
        var gray = SysColor(NativeMethods.COLOR_GRAYTEXT);
        var highlight = SysColor(NativeMethods.COLOR_HIGHLIGHT);
        var highlightText = SysColor(NativeMethods.COLOR_HIGHLIGHTTEXT);
        return new ThemePalette(
            "HighContrast",
            Surface: window,
            SurfaceAlt: window,
            BorderSubtle: text,
            CompactBorder: text,
            TextPrimary: text,
            TextSecondary: gray,
            PlaceholderIcon: gray,
            HoverOverlay: highlight,
            PressedOverlay: highlight,
            HoverFill: highlight,
            PressedFill: highlight,
            HoverText: highlightText,
            FocusRing: highlight,
            ProgressTrack: gray,
            ProgressFill: highlight,
            ProgressRemainder: gray,
            ProgressThumb: highlight,
            ErrorText: text,
            HintSurface: window);
    }

    internal static Color FocusRingFor(
        Color accent,
        Color surface,
        Color ink,
        Color hoverFill,
        Color hoverOverlay,
        Color pressedFill)
    {
        var backgrounds = FocusBackgrounds(surface, hoverFill, hoverOverlay, accent, pressedFill);
        if (TryFitFocusRing(accent, backgrounds) is { } fitted)
        {
            return fitted;
        }

        foreach (var shifted in LightnessVariants(accent, surface))
        {
            if (TryFitFocusRing(shifted, backgrounds) is { } fittedShift)
            {
                return fittedShift;
            }
        }

        foreach (var mixed in MixToward(accent, ink))
        {
            if (TryFitFocusRing(mixed, backgrounds) is { } fittedMix)
            {
                return fittedMix;
            }
        }

        return TryFitFocusRing(ink, backgrounds) ?? WithAlpha(ink, 0xFF);
    }

    internal static Color[] FocusBackgrounds(
        Color surface,
        Color hoverFill,
        Color hoverOverlay,
        Color pressedOverlay,
        Color pressedFill)
    {
        var hoverInstant = BlendOver(hoverFill, surface);
        var hoverAnimated = BlendOver(WithAlpha(hoverOverlay, OpacityByte(AnimatedHoverOpacity)), surface);
        var pressedInstant = BlendOver(pressedFill, surface);
        var pressedAnimated = BlendOver(WithAlpha(pressedOverlay, OpacityByte(AnimatedPressedOpacity)), surface);
        var hoverPressed = BlendOver(WithAlpha(pressedOverlay, OpacityByte(AnimatedPressedOpacity)), hoverAnimated);
        return [surface, hoverInstant, hoverAnimated, pressedInstant, pressedAnimated, hoverPressed];
    }

    internal static bool MeetsFocusContrast(Color ring, IReadOnlyList<Color> backgrounds)
    {
        foreach (var background in backgrounds)
        {
            var painted = BlendOver(ring, background);
            if (ContrastRatio(painted, background) < FocusContrastFloor)
            {
                return false;
            }
        }

        return true;
    }

    internal static Color? TryFitFocusRing(Color rgb, IReadOnlyList<Color> backgrounds)
    {
        for (var alpha = 0x80; alpha <= 0xFF; alpha += 8)
        {
            var ring = WithAlpha(rgb, (byte)alpha);
            if (MeetsFocusContrast(ring, backgrounds))
            {
                return ring;
            }
        }

        var opaque = WithAlpha(rgb, 0xFF);
        return MeetsFocusContrast(opaque, backgrounds) ? opaque : null;
    }

    internal static IEnumerable<Color> LightnessVariants(Color accent, Color surface)
    {
        RgbToHsl(accent, out var hue, out var saturation, out var lightness);
        var lighten = RelativeLuminance(surface) < 0.5;
        for (var step = 1; step <= 24; step++)
        {
            var delta = 0.025 * step;
            var next = lighten
                ? Math.Min(0.92, lightness + delta)
                : Math.Max(0.08, lightness - delta);
            if (Math.Abs(next - lightness) < 0.001)
            {
                yield break;
            }

            yield return HslToRgb(hue, saturation, next);
        }
    }

    private static IEnumerable<Color> MixToward(Color accent, Color ink)
    {
        for (var step = 1; step <= 10; step++)
        {
            var t = step / 10.0;
            yield return Color.FromRgb(
                MixByte(accent.R, ink.R, t),
                MixByte(accent.G, ink.G, t),
                MixByte(accent.B, ink.B, t));
        }
    }

    internal static void RgbToHsl(Color color, out double hue, out double saturation, out double lightness)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        lightness = (max + min) / 2;
        if (max.Equals(min))
        {
            hue = 0;
            saturation = 0;
            return;
        }

        var delta = max - min;
        saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        if (max.Equals(r))
        {
            hue = (g - b) / delta + (g < b ? 6 : 0);
        }
        else if (max.Equals(g))
        {
            hue = (b - r) / delta + 2;
        }
        else
        {
            hue = (r - g) / delta + 4;
        }

        hue /= 6;
    }

    internal static Color HslToRgb(double hue, double saturation, double lightness)
    {
        if (saturation <= 0)
        {
            var gray = (byte)Math.Clamp((int)Math.Round(lightness * 255), 0, 255);
            return Color.FromRgb(gray, gray, gray);
        }

        var q = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
        var p = 2 * lightness - q;
        byte ToByte(double channel) => (byte)Math.Clamp((int)Math.Round(channel * 255), 0, 255);

        double HueChannel(double t)
        {
            if (t < 0)
            {
                t += 1;
            }

            if (t > 1)
            {
                t -= 1;
            }

            if (t < 1.0 / 6)
            {
                return p + (q - p) * 6 * t;
            }

            if (t < 0.5)
            {
                return q;
            }

            if (t < 2.0 / 3)
            {
                return p + (q - p) * (2.0 / 3 - t) * 6;
            }

            return p;
        }

        return Color.FromRgb(
            ToByte(HueChannel(hue + 1.0 / 3)),
            ToByte(HueChannel(hue)),
            ToByte(HueChannel(hue - 1.0 / 3)));
    }

    internal static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    internal static Color SysColor(int index)
    {
        var value = NativeMethods.GetSysColor(index);
        return Color.FromRgb(
            (byte)(value & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)((value >> 16) & 0xFF));
    }

    internal static Color BlendOver(Color foreground, Color background)
    {
        var alpha = foreground.A / 255.0;
        byte Mix(byte fg, byte bg) => (byte)Math.Round(fg * alpha + bg * (1 - alpha));
        return Color.FromRgb(Mix(foreground.R, background.R), Mix(foreground.G, background.G), Mix(foreground.B, background.B));
    }

    internal static double ContrastRatio(Color a, Color b)
    {
        var first = RelativeLuminance(a);
        var second = RelativeLuminance(b);
        var (lighter, darker) = first > second ? (first, second) : (second, first);
        return (lighter + 0.05) / (darker + 0.05);
    }

    internal static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var linear = value / 255.0;
            return linear <= 0.03928 ? linear / 12.92 : Math.Pow((linear + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    internal static byte OpacityByte(double opacity) =>
        (byte)Math.Clamp((int)Math.Round(opacity * 255), 0, 255);

    private static byte MixByte(byte from, byte to, double t) =>
        (byte)Math.Clamp((int)Math.Round(from + (to - from) * t), 0, 255);
}

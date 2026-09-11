using System.Windows.Media;
using TaskBarHook.Desktop;
using Color = System.Windows.Media.Color;

namespace TaskBarHook.Theming;

public static class ThemeResolver
{
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
        return new ThemePalette(
            "Dark",
            Surface: surface,
            SurfaceAlt: Color.FromRgb(0x2A, 0x2A, 0x2C),
            BorderSubtle: Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF),
            CompactBorder: Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
            TextPrimary: ink,
            TextSecondary: Color.FromRgb(0xB3, 0xB3, 0xB3),
            PlaceholderIcon: Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF),
            HoverOverlay: Colors.White,
            PressedOverlay: accent,
            HoverFill: Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF),
            PressedFill: WithAlpha(accent, 0x38),
            HoverText: ink,
            FocusRing: FocusRingFor(accent, surface, ink),
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
        return new ThemePalette(
            "Light",
            Surface: surface,
            SurfaceAlt: Color.FromRgb(0xE8, 0xE8, 0xE8),
            BorderSubtle: Color.FromArgb(0x14, 0x00, 0x00, 0x00),
            CompactBorder: Color.FromArgb(0x14, 0x00, 0x00, 0x00),
            TextPrimary: ink,
            TextSecondary: Color.FromRgb(0x5D, 0x5D, 0x5D),
            PlaceholderIcon: Color.FromArgb(0x66, 0x00, 0x00, 0x00),
            HoverOverlay: Colors.Black,
            PressedOverlay: accent,
            HoverFill: Color.FromArgb(0x28, 0x00, 0x00, 0x00),
            PressedFill: WithAlpha(accent, 0x38),
            HoverText: ink,
            FocusRing: FocusRingFor(accent, surface, ink),
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

    private static Color FocusRingFor(Color accent, Color surface, Color ink)
    {
        // A 1px dashed indicator stays clearly visible well below text
        // contrast: keep any accent that clears 1.5, fall back only when the
        // ring would melt into the surface (a 3.0 bar suppressed even the
        // default blue, defeating the accent).
        var candidate = WithAlpha(accent, 0x80);
        return ContrastRatio(BlendOver(candidate, surface), surface) >= 1.5
            ? candidate
            : WithAlpha(ink, 0x80);
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

    private static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var linear = value / 255.0;
            return linear <= 0.03928 ? linear / 12.92 : Math.Pow((linear + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}

using System.Windows.Media;
using Microsoft.Win32;
using TaskBarHook.Desktop;
using Color = System.Windows.Media.Color;

namespace TaskBarHook.Theming;

public readonly record struct ThemeInputs(
    bool SystemLight,
    bool AppsLight,
    Color Accent,
    bool HighContrast);

public static class ThemeInputsReader
{
    public const string PersonalizeKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static readonly Color DefaultAccent = Color.FromRgb(0x00, 0x78, 0xD4);

    public static ThemeInputs Read()
    {
        var systemLight = true;
        var appsLight = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            systemLight = ParseLightMode(key?.GetValue("SystemUsesLightTheme"));
            appsLight = ParseLightMode(key?.GetValue("AppsUseLightTheme"));
        }
        catch
        {
            // Locked-down profile: fall back to light, documented below.
        }

        return new ThemeInputs(systemLight, appsLight, ReadAccent(), ReadHighContrast());
    }

    internal static bool ParseLightMode(object? value) =>
        value switch
        {
            int i => i != 0,
            long l => l != 0,
            _ => true,
        };

    private static Color ReadAccent()
    {
        // The Settings accent lives in AccentColor (ABGR DWORD). DWM
        // colorization can diverge (wallpaper-derived), so it is only a
        // fallback. Verified live: DWM reported orange while the user accent
        // was default blue.
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\DWM");
            if (TryParseAccent(key?.GetValue("AccentColor")) is { } accent)
            {
                return accent;
            }
        }
        catch
        {
            // Fall through to DWM, then to the default.
        }

        try
        {
            if (NativeMethods.DwmGetColorizationColor(out var color, out _) == 0)
            {
                return Color.FromRgb(
                    (byte)(color >> 16),
                    (byte)(color >> 8),
                    (byte)color);
            }
        }
        catch
        {
            // Fall through to the default.
        }

        return DefaultAccent;
    }

    internal static Color? TryParseAccent(object? value)
    {
        uint raw = value switch
        {
            int i => unchecked((uint)i),
            uint u => u,
            long l => unchecked((uint)l),
            _ => 0,
        };

        if (value is not (int or uint or long))
        {
            return null;
        }

        return Color.FromRgb(
            (byte)(raw & 0xFF),
            (byte)((raw >> 8) & 0xFF),
            (byte)((raw >> 16) & 0xFF));
    }

    private static bool ReadHighContrast()
    {
        try
        {
            return System.Windows.SystemParameters.HighContrast;
        }
        catch
        {
            return false;
        }
    }
}

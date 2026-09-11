using System.Windows;
using System.Windows.Media;
using TaskBarHook.Logging;
using Color = System.Windows.Media.Color;

namespace TaskBarHook.Theming;

internal sealed class ThemeManager
{
    public static IReadOnlyDictionary<string, Func<ThemePalette, Color>> Bindings { get; } =
        new Dictionary<string, Func<ThemePalette, Color>>
        {
            ["TaskBarHook.Surface"] = palette => palette.Surface,
            ["TaskBarHook.SurfaceAlt"] = palette => palette.SurfaceAlt,
            ["TaskBarHook.BorderSubtle"] = palette => palette.BorderSubtle,
            ["TaskBarHook.CompactBorder"] = palette => palette.CompactBorder,
            ["TaskBarHook.TextPrimary"] = palette => palette.TextPrimary,
            ["TaskBarHook.TextSecondary"] = palette => palette.TextSecondary,
            ["TaskBarHook.PlaceholderIcon"] = palette => palette.PlaceholderIcon,
            ["TaskBarHook.HoverOverlay"] = palette => palette.HoverOverlay,
            ["TaskBarHook.PressedOverlay"] = palette => palette.PressedOverlay,
            ["TaskBarHook.HoverFill"] = palette => palette.HoverFill,
            ["TaskBarHook.PressedFill"] = palette => palette.PressedFill,
            ["TaskBarHook.HoverText"] = palette => palette.HoverText,
            ["TaskBarHook.FocusRing"] = palette => palette.FocusRing,
            ["TaskBarHook.ProgressTrack"] = palette => palette.ProgressTrack,
            ["TaskBarHook.ProgressFill"] = palette => palette.ProgressFill,
            ["TaskBarHook.ProgressRemainder"] = palette => palette.ProgressRemainder,
            ["TaskBarHook.ProgressThumb"] = palette => palette.ProgressThumb,
            ["TaskBarHook.ErrorText"] = palette => palette.ErrorText,
            ["TaskBarHook.HintSurface"] = palette => palette.HintSurface,
        };

    private readonly ResourceDictionary _resources;
    private readonly IAppLogger? _logger;
    private ThemePalette? _current;

    public ThemeManager(ResourceDictionary resources, IAppLogger? logger = null)
    {
        _resources = resources;
        _logger = logger;
    }

    internal ThemePalette? Current => _current;

    public void EnsureRegistered()
    {
        foreach (var key in Bindings.Keys)
        {
            _resources[key] = new SolidColorBrush(Colors.Transparent);
        }
    }

    public bool Refresh()
    {
        return Apply(ThemeResolver.Resolve(ThemeInputsReader.Read()));
    }

    public bool Apply(ThemePalette palette)
    {
        if (palette.Equals(_current))
        {
            return false;
        }

        foreach (var (key, select) in Bindings)
        {
            // Replace, never mutate: brushes living in Application.Resources
            // may be frozen, which makes in-place Color sets throw.
            // DynamicResource references follow the swap with no window or
            // control recreation and no flash.
            _resources[key] = new SolidColorBrush(select(palette));
        }

        _current = palette;
        _logger?.Info($"Theme applied: {palette.Name}.");
        return true;
    }
}

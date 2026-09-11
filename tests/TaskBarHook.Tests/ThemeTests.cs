using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TaskBarHook.Logging;
using TaskBarHook.Media;
using TaskBarHook.Models;
using TaskBarHook.Presentation;
using TaskBarHook.Theming;
using TaskBarHook.Views;

namespace TaskBarHook.Tests;

public sealed class ThemeTests
{
    private static readonly Color BlueAccent = Color.FromRgb(0x00, 0x78, 0xD4);

    [Fact]
    public void Dark_matches_the_approved_values()
    {
        var palette = ThemeResolver.Resolve(new ThemeInputs(
            SystemLight: false, AppsLight: false, Accent: BlueAccent, HighContrast: false));

        Assert.Equal("Dark", palette.Name);
        Assert.Equal(Color.FromRgb(0x1C, 0x1C, 0x1E), palette.Surface);
        Assert.Equal(Color.FromRgb(0x2A, 0x2A, 0x2C), palette.SurfaceAlt);
        Assert.Equal(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), palette.BorderSubtle);
        Assert.Equal(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), palette.CompactBorder);
        Assert.Equal(Color.FromRgb(0xF3, 0xF3, 0xF3), palette.TextPrimary);
        Assert.Equal(Color.FromRgb(0xB3, 0xB3, 0xB3), palette.TextSecondary);
        Assert.Equal(Color.FromRgb(0xB3, 0xB3, 0xB3), palette.ErrorText);
        Assert.Equal(Color.FromRgb(0x3A, 0x3A, 0x3C), palette.ProgressTrack);
        Assert.Equal(Color.FromRgb(0xF3, 0xF3, 0xF3), palette.ProgressFill);
        Assert.Equal(Color.FromRgb(0xF3, 0xF3, 0xF3), palette.ProgressThumb);
        Assert.Equal(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF), palette.HoverFill);
    }

    [Fact]
    public void Text_contrast_holds_in_both_themes()
    {
        foreach (var light in new[] { false, true })
        {
            var palette = ThemeResolver.Resolve(new ThemeInputs(
                light, light, BlueAccent, HighContrast: false));

            Assert.True(Contrast(palette.TextPrimary, palette.Surface) >= 4.5, $"primary/{palette.Name}");
            Assert.True(Contrast(palette.TextSecondary, palette.Surface) >= 4.5, $"secondary/{palette.Name}");
            Assert.True(Contrast(palette.ErrorText, palette.Surface) >= 4.5, $"error/{palette.Name}");
            Assert.True(Contrast(palette.ProgressFill, palette.ProgressTrack) >= 3.0, $"progress/{palette.Name}");
        }
    }

    [Fact]
    public void Mixed_modes_follow_the_system_mode()
    {
        var systemLight = ThemeResolver.Resolve(new ThemeInputs(
            SystemLight: true, AppsLight: false, Accent: BlueAccent, HighContrast: false));
        var systemDark = ThemeResolver.Resolve(new ThemeInputs(
            SystemLight: false, AppsLight: true, Accent: BlueAccent, HighContrast: false));

        Assert.Equal("Light", systemLight.Name);
        Assert.Equal("Dark", systemDark.Name);
    }

    [Fact]
    public void Focus_ring_keeps_contrast_for_any_accent()
    {
        var accents = new[]
        {
            Color.FromRgb(0x00, 0x78, 0xD4),
            Color.FromRgb(0xD8, 0x3B, 0x01),
            Color.FromRgb(0x10, 0x7C, 0x10),
            Color.FromRgb(0x86, 0x61, 0xC5),
            Color.FromRgb(0xFF, 0xFF, 0x00),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0x00, 0x00, 0x00),
            Color.FromRgb(0xE8, 0x11, 0x23),
        };

        foreach (var light in new[] { false, true })
        {
            foreach (var accent in accents)
            {
                var palette = ThemeResolver.Resolve(new ThemeInputs(
                    light, light, accent, HighContrast: false));
                var effective = ThemeResolver.BlendOver(palette.FocusRing, palette.Surface);
                Assert.True(
                    ThemeResolver.ContrastRatio(effective, palette.Surface) >= 1.5,
                    $"accent={accent} light={light}");
            }
        }
    }

    [Fact]
    public void Focus_ring_uses_accent_when_legible_and_falls_back_otherwise()
    {
        var yellow = Color.FromRgb(0xFF, 0xFF, 0x00);
        var kept = ThemeResolver.Resolve(new ThemeInputs(
            SystemLight: false, AppsLight: false, Accent: yellow, HighContrast: false));
        Assert.Equal(ThemeResolver.WithAlpha(yellow, 0x80), kept.FocusRing);

        var blackOnDark = ThemeResolver.Resolve(new ThemeInputs(
            SystemLight: false, AppsLight: false, Accent: Colors.Black, HighContrast: false));
        Assert.Equal(ThemeResolver.WithAlpha(Color.FromRgb(0xF3, 0xF3, 0xF3), 0x80), blackOnDark.FocusRing);

        var whiteOnLight = ThemeResolver.Resolve(new ThemeInputs(
            SystemLight: true, AppsLight: true, Accent: Colors.White, HighContrast: false));
        Assert.Equal(ThemeResolver.WithAlpha(Color.FromRgb(0x1B, 0x1B, 0x1B), 0x80), whiteOnLight.FocusRing);
    }

    [Fact]
    public void High_contrast_uses_system_colors()
    {
        var palette = ThemeResolver.Resolve(new ThemeInputs(
            SystemLight: false, AppsLight: false, Accent: BlueAccent, HighContrast: true));

        Assert.Equal("HighContrast", palette.Name);
        Assert.Equal(SysColor(5), palette.Surface);
        Assert.Equal(SysColor(8), palette.TextPrimary);
        Assert.Equal(SysColor(17), palette.TextSecondary);
        Assert.Equal(SysColor(13), palette.ProgressFill);
        Assert.Equal(SysColor(13), palette.FocusRing);
        Assert.Equal(SysColor(14), palette.HoverText);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetSysColor(int nIndex);

    private static Color SysColor(int index)
    {
        var value = GetSysColor(index);
        return Color.FromRgb(
            (byte)(value & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)((value >> 16) & 0xFF));
    }

    [Fact]
    public void Missing_or_invalid_mode_values_fall_back_to_light()
    {
        Assert.True(ThemeInputsReader.ParseLightMode(null));
        Assert.True(ThemeInputsReader.ParseLightMode(1));
        Assert.False(ThemeInputsReader.ParseLightMode(0));
        Assert.True(ThemeInputsReader.ParseLightMode("garbage"));
    }

    [Fact]
    public void Accent_registry_value_parses_as_abgr()
    {
        Assert.Equal(
            Color.FromRgb(0x00, 0x78, 0xD4),
            ThemeInputsReader.TryParseAccent(unchecked((int)0xFFD47800)));
        Assert.Equal(
            Color.FromRgb(0xD8, 0x3B, 0x00),
            ThemeInputsReader.TryParseAccent(0xFF003BD8u));
        Assert.Null(ThemeInputsReader.TryParseAccent(null));
        Assert.Null(ThemeInputsReader.TryParseAccent("garbage"));
    }

    [Fact]
    public void Manager_swaps_brush_values_and_skips_unchanged_palettes()
    {
        var resources = new ResourceDictionary();
        var manager = new ThemeManager(resources);
        manager.EnsureRegistered();

        var dark = ThemeResolver.Resolve(new ThemeInputs(
            SystemLight: false, AppsLight: false, Accent: BlueAccent, HighContrast: false));
        Assert.True(manager.Apply(dark));
        Assert.Equal(dark.Surface, ((SolidColorBrush)resources["TaskBarHook.Surface"]).Color);

        var light = ThemeResolver.Resolve(new ThemeInputs(
            SystemLight: true, AppsLight: true, Accent: BlueAccent, HighContrast: false));
        Assert.True(manager.Apply(light));
        Assert.Equal(light.Surface, ((SolidColorBrush)resources["TaskBarHook.Surface"]).Color);

        Assert.False(manager.Apply(light));
    }

    private static double Contrast(Color foreground, Color background) =>
        ThemeResolver.ContrastRatio(ThemeResolver.BlendOver(foreground, background), background);

    [Fact]
    public void Template_focus_rects_follow_theme_switches()
    {
        // The adorner-based FocusVisualStyle never resolved theme resources;
        // the in-template rects must track the live FocusRing brush instead.
        WpfStaRunner.Run(() =>
        {
            var manager = new ThemeManager(Application.Current.Resources);
            manager.EnsureRegistered();
            var dark = ThemeResolver.Resolve(new ThemeInputs(
                SystemLight: false, AppsLight: false, Accent: BlueAccent, HighContrast: false));
            manager.Apply(dark);

            var animated = new Button { Style = (Style)Application.Current.Resources["AnimatedIconButtonStyle"] };
            var instant = new Button { Style = (Style)Application.Current.Resources["IconButtonStyle"] };
            var slider = new Slider { Style = (Style)Application.Current.Resources["SeekSliderStyle"] };
            animated.ApplyTemplate();
            instant.ApplyTemplate();
            slider.ApplyTemplate();
            var rects = new[]
            {
                (Rectangle)animated.Template.FindName("FocusRect", animated)!,
                (Rectangle)instant.Template.FindName("FocusRect", instant)!,
                (Rectangle)slider.Template.FindName("FocusRect", slider)!,
            };
            foreach (var rect in rects)
            {
                Assert.Equal(dark.FocusRing, ((SolidColorBrush)rect.Stroke).Color);
            }

            var light = ThemeResolver.Resolve(new ThemeInputs(
                SystemLight: true, AppsLight: true, Accent: BlueAccent, HighContrast: false));
            manager.Apply(light);
            foreach (var rect in rects)
            {
                Assert.Equal(light.FocusRing, ((SolidColorBrush)rect.Stroke).Color);
            }
        });
    }

    [Fact]
    public void Compact_window_constructed_before_registration_follows_live_brushes()
    {
        WpfStaRunner.Run(() =>
        {
            var media = new ThemeFakeMedia();
            using var vm = new CapsuleViewModel(media, new SystemClock(), new ThemeNullLogger());
            var window = new CompactWindow(vm);

            var manager = new ThemeManager(Application.Current.Resources);
            manager.EnsureRegistered();
            var light = ThemeResolver.Resolve(new ThemeInputs(
                SystemLight: true, AppsLight: true, Accent: BlueAccent, HighContrast: false));
            Assert.True(manager.Apply(light));
            Assert.Equal(light.Surface, ((SolidColorBrush)window.Surface.Background).Color);

            var dark = ThemeResolver.Resolve(new ThemeInputs(
                SystemLight: false, AppsLight: false, Accent: BlueAccent, HighContrast: false));
            Assert.True(manager.Apply(dark));
            Assert.Equal(dark.Surface, ((SolidColorBrush)window.Surface.Background).Color);
            Assert.Equal(dark.TextPrimary, ((SolidColorBrush)window.Foreground).Color);
        });
    }

    private sealed class ThemeFakeMedia : IMediaSessionService
    {
        public MediaSnapshot Current { get; } = MediaSnapshot.Empty;

        public event EventHandler<MediaSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> PlayPauseAsync() => Task.FromResult(true);

        public Task<bool> SkipNextAsync() => Task.FromResult(true);

        public Task<bool> SkipPreviousAsync() => Task.FromResult(true);

        public Task<bool> SeekAsync(long positionTicks) => Task.FromResult(true);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThemeNullLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}

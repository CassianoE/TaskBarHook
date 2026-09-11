using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TaskBarHook.Logging;
using TaskBarHook.Media;
using TaskBarHook.Models;
using TaskBarHook.Presentation;
using TaskBarHook.Views;

namespace TaskBarHook.Tests;

// Guards the panel composition: header side by side on top, controls with
// room in the middle, seek pinned at the bottom, nothing clipped.
[Collection("WPF")]
public sealed class PanelLayoutTests
{
    [Fact]
    public void Header_places_cover_and_text_side_by_side()
    {
        WpfStaRunner.Run(() =>
        {
            var window = CreatePanel(out _, artist: "Artista de teste");
            var header = window.HeaderGrid;

            // Cover 56x56 on the left, text block to its right, same row.
            var titleBox = BoundsIn(window.TitleText, header);
            var artistBox = BoundsIn(window.ArtistText, header);
            Assert.True(titleBox.Left >= 56, $"Title starts at {titleBox.Left}.");
            Assert.True(artistBox.Left >= 56, $"Artist starts at {artistBox.Left}.");
            Assert.True(artistBox.Top >= titleBox.Bottom - 1, "Artist is not below the title.");
            Assert.True(header.ActualHeight >= 56, $"Header is {header.ActualHeight} DIP.");
            var gridBox = new Rect(0, 0, window.ContentGrid.ActualWidth, window.ContentGrid.ActualHeight);
            AssertInside(gridBox, BoundsIn(window.TitleText, window.ContentGrid));
            AssertInside(gridBox, BoundsIn(window.ArtistText, window.ContentGrid));
        });
    }

    [Fact]
    public void Missing_artist_keeps_a_stable_header_without_gaps()
    {
        WpfStaRunner.Run(() =>
        {
            var window = CreatePanel(out _, artist: "");
            Assert.Equal(Visibility.Collapsed, window.ArtistText.Visibility);
            Assert.True(window.HeaderGrid.ActualHeight >= 56, "Header collapsed without artist.");
            var titleBox = BoundsIn(window.TitleText, window.HeaderGrid);
            // Single-line title stays vertically centered against the cover.
            Assert.True(titleBox.Top > 4, $"Title glued to the top: {titleBox.Top}.");
            Assert.True(window.HeaderGrid.ActualHeight - titleBox.Bottom > 4, "Title glued to the bottom.");
        });
    }

    [Fact]
    public void Controls_row_fits_the_buttons_without_touching_the_slider()
    {
        WpfStaRunner.Run(() =>
        {
            var window = CreatePanel(out _);
            var grid = window.ContentGrid;

            var controlsHeight = grid.RowDefinitions[1].ActualHeight;
            Assert.True(controlsHeight >= 60, $"Controls row is {controlsHeight} DIP.");

            Assert.Equal(36, window.PrevButton.ActualHeight, 1);
            Assert.Equal(40, window.PlayPauseButton.ActualHeight, 1);
            Assert.Equal(36, window.NextButton.ActualHeight, 1);

            // The empty failure line must not reserve space.
            Assert.Equal(0, window.SeekFailureText.ActualHeight, 1);
            Assert.Equal(Visibility.Collapsed, window.SeekFailureText.Visibility);

            var gridBox = new Rect(0, 0, grid.ActualWidth, grid.ActualHeight);
            AssertInside(gridBox, BoundsIn(window.PrevButton, grid));
            AssertInside(gridBox, BoundsIn(window.PlayPauseButton, grid));
            AssertInside(gridBox, BoundsIn(window.NextButton, grid));

            var sliderBox = BoundsIn(window.SeekSlider, grid);
            Assert.False(sliderBox.IntersectsWith(BoundsIn(window.PrevButton, grid)), "Slider overlaps prev button.");
            Assert.False(sliderBox.IntersectsWith(BoundsIn(window.PlayPauseButton, grid)), "Slider overlaps play button.");
            Assert.False(sliderBox.IntersectsWith(BoundsIn(window.NextButton, grid)), "Slider overlaps next button.");
        });
    }

    [Fact]
    public void Buttons_still_fit_while_the_failure_line_is_shown()
    {
        WpfStaRunner.Run(() =>
        {
            var window = CreatePanel(out var media);
            media.FailSeek = true;

            // Real gesture through the manual owner: begin, move, release.
            var slider = window.SeekSlider;
            Assert.True(window.TryBeginPointerSeekAt(
                new Point(slider.ActualWidth * 0.5, slider.ActualHeight / 2)));
            window.UpdatePointerPreviewAt(new Point(slider.ActualWidth * 0.6, slider.ActualHeight / 2));
            window.CompletePointerSeekAt(null);
            Relayout(window);

            Assert.NotNull(window.ViewModel.SeekFailureText);
            Assert.True(window.SeekFailureText.ActualHeight > 0, "Failure line did not take space.");
            Assert.Equal(Visibility.Visible, window.SeekFailureText.Visibility);

            var grid = window.ContentGrid;
            Assert.True(grid.RowDefinitions[1].ActualHeight >= 60,
                $"Controls row shrank to {grid.RowDefinitions[1].ActualHeight} DIP with the failure line.");
            var gridBox = new Rect(0, 0, grid.ActualWidth, grid.ActualHeight);
            AssertInside(gridBox, BoundsIn(window.PlayPauseButton, grid));
        });
    }

    [Fact]
    public void Buttons_fit_with_larger_text()
    {
        WpfStaRunner.Run(() =>
        {
            var window = CreatePanel(out _);
            // Display-scale changes are DIP-neutral, but prove headroom anyway
            // by growing the inherited text 25%.
            window.FontSize *= 1.25;
            Relayout(window);

            Assert.True(window.ContentGrid.RowDefinitions[1].ActualHeight >= 60,
                $"Controls row is {window.ContentGrid.RowDefinitions[1].ActualHeight} DIP at 125% text.");
            Assert.Equal(40, window.PlayPauseButton.ActualHeight, 1);
        });
    }

    private static PanelWindow CreatePanel(out FakeMedia media, string artist = "Artista de teste")
    {
        var duration = TimeSpan.FromMinutes(12);
        var inner = new FakeMedia(new MediaSnapshot(
            MediaPlaybackStatus.Paused,
            new TrackInfo("Faixa de teste com um título razoavelmente longo", artist, null, null, "test"),
            new TimelineInfo(TimeSpan.FromMinutes(6), duration, DateTimeOffset.UtcNow, 1, false)
            {
                StartTime = TimeSpan.Zero,
                EndTime = duration,
                MinSeekTime = TimeSpan.Zero,
                MaxSeekTime = duration
            },
            new CommandAvailability(PlayPause: true, Next: true, Previous: true, Seek: true),
            "test-session",
            "test-app",
            "test-session#1",
            1));
        media = inner;

        var vm = new CapsuleViewModel(inner, new SystemClock(), new TestLogger());
        vm.ApplySnapshot(inner.Current);
        var window = new PanelWindow(vm);
        // A Window without an HWND never measures; lay out its content at
        // the exact client size instead (borderless window: client == window).
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(PanelWindow.PanelWidth, PanelWindow.PanelHeight));
        content.Arrange(new Rect(0, 0, PanelWindow.PanelWidth, PanelWindow.PanelHeight));
        content.UpdateLayout();
        return window;
    }

    private static void Relayout(PanelWindow window)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(PanelWindow.PanelWidth, PanelWindow.PanelHeight));
        content.Arrange(new Rect(0, 0, PanelWindow.PanelWidth, PanelWindow.PanelHeight));
        content.UpdateLayout();
    }

    private static Rect BoundsIn(FrameworkElement element, Visual ancestor)
    {
        var origin = element.TransformToAncestor(ancestor).Transform(new Point(0, 0));
        return new Rect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
    }

    private sealed class FakeMedia : IMediaSessionService
    {
        public FakeMedia(MediaSnapshot snapshot)
        {
            Current = snapshot;
        }

        public MediaSnapshot Current { get; private set; }

        public bool FailSeek { get; set; }

        public event EventHandler<MediaSnapshot>? SnapshotChanged;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> PlayPauseAsync() => Task.FromResult(true);

        public Task<bool> SkipNextAsync() => Task.FromResult(false);

        public Task<bool> SkipPreviousAsync() => Task.FromResult(false);

        public Task<bool> SeekAsync(long positionTicks)
        {
            if (!FailSeek)
            {
                Current = Current with
                {
                    Timeline = Current.Timeline with
                    {
                        Position = TimeSpan.FromTicks(positionTicks),
                        LastUpdatedUtc = DateTimeOffset.UtcNow
                    }
                };
                SnapshotChanged?.Invoke(this, Current);
            }

            return Task.FromResult(!FailSeek);
        }
    }

    private sealed class TestLogger : IAppLogger
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

    private static void AssertInside(Rect outer, Rect inner)
    {
        Assert.True(
            inner.Left >= -0.5 && inner.Top >= -0.5 &&
            inner.Right <= outer.Width + 0.5 && inner.Bottom <= outer.Height + 0.5,
            $"Inner {inner} escapes outer {outer.Width}x{outer.Height}.");
    }
}

using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskBarHook.Logging;
using TaskBarHook.Media;
using TaskBarHook.Models;
using TaskBarHook.Queue;

namespace TaskBarHook.Presentation;

public sealed partial class QueuePresenter : ObservableObject, IDisposable
{
    private readonly IPlaybackQueueService _queue;
    private readonly IClock _clock;
    private readonly IAppLogger _logger;
    private readonly QueueFlyoutCoordinator _flyout = new();
    private readonly PlaybackQueueCoordinator _refresh = new();
    private readonly DispatcherTimer? _timer;
    private readonly List<string> _itemKeys = [];
    private MediaSnapshot _session = MediaSnapshot.Empty;
    private PlaybackQueueSnapshot _snapshot = PlaybackQueueSnapshot.Idle;
    private bool _disposed;
    private bool _refreshing;

    public QueuePresenter(IPlaybackQueueService queue, IClock clock, IAppLogger logger)
    {
        _queue = queue;
        _clock = clock;
        _logger = logger;
        _queue.QueueChanged += OnQueueChanged;
        ToggleCommand = new RelayCommand(Toggle);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => CanConnect);
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Input, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(32)
            };
            _timer.Tick += OnTimerTick;
        }
    }

    private void OnTimerTick(object? sender, EventArgs e) => Tick(_clock.UtcNow);

    public ObservableCollection<QueueItemViewModel> Items { get; } = [];

    public IRelayCommand ToggleCommand { get; }

    public IAsyncRelayCommand ConnectCommand { get; }

    [ObservableProperty] private bool _isOpen;

    [ObservableProperty] private bool _wantsFocus;

    [ObservableProperty] private bool _showList;

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private bool _showStatus;

    [ObservableProperty] private string _statusText = "";

    [ObservableProperty] private string _headerText = "A seguir";

    [ObservableProperty] private bool _canConnect;

    [ObservableProperty] private bool _isSimulated;

    [ObservableProperty] private string? _missingCredential;

    public event EventHandler? OpenedChanged;

    public void SetSession(MediaSnapshot session)
    {
        var changed = !string.Equals(
            PlaybackQueueCoordinator.SessionKey(session.SourceAppId, session.Track.Title, session.Track.Artist),
            PlaybackQueueCoordinator.SessionKey(_session.SourceAppId, _session.Track.Title, _session.Track.Artist),
            StringComparison.Ordinal);
        _session = session;
        if (changed && IsOpen)
        {
            _ = RefreshAsync(force: true);
        }
    }

    public void SetTriggerPointer(bool over)
    {
        _flyout.SetTrigger(over, _clock.UtcNow);
        SyncFlyout();
    }

    public void SetFlyoutPointer(bool over)
    {
        _flyout.SetFlyout(over, _clock.UtcNow);
        SyncFlyout();
    }

    public void NotifyPanelPointerDown()
    {
        if (IsOpen)
        {
            _flyout.CloseImmediate();
            SyncFlyout();
        }
    }

    public bool CloseFromEscape()
    {
        var closed = _flyout.CloseFromEscape();
        SyncFlyout();
        return closed;
    }

    public void CloseImmediate()
    {
        _flyout.CloseImmediate();
        SyncFlyout();
    }

    public void Tick(DateTimeOffset now)
    {
        var wasOpen = _flyout.IsOpen;
        _flyout.Tick(now);
        SyncFlyout();
        if (_flyout.IsOpen && !wasOpen)
        {
            _ = RefreshAsync(force: false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.QueueChanged -= OnQueueChanged;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
        }
    }

    private void Toggle()
    {
        _flyout.ToggleExplicit();
        SyncFlyout();
        if (_flyout.IsOpen)
        {
            _ = RefreshAsync(force: false);
        }
    }

    private async Task ConnectAsync()
    {
        if (!_queue.CanAuthorize)
        {
            return;
        }

        IsLoading = true;
        StatusText = "Abrindo autorização do Spotify…";
        ShowStatus = true;
        try
        {
            await _queue.AuthorizeAsync();
            await RefreshAsync(force: true);
        }
        catch (Exception ex)
        {
            _logger.Error("Spotify authorize failed.", ex);
            StatusText = "Não foi possível autorizar o Spotify";
            ShowStatus = true;
        }
    }

    private async Task RefreshAsync(bool force)
    {
        var key = PlaybackQueueCoordinator.SessionKey(
            _session.SourceAppId,
            _session.Track.Title,
            _session.Track.Artist);
        if (!_refresh.ShouldFetch(_flyout.IsOpen, _clock.UtcNow, key, force) || _refreshing)
        {
            return;
        }

        _refresh.Begin(key, _clock.UtcNow);
        _refreshing = true;
        if (_snapshot.Items.Count == 0)
        {
            IsLoading = true;
            StatusText = "Carregando fila…";
            ShowStatus = true;
        }

        try
        {
            await _queue.RefreshAsync(_session);
            ApplySnapshot(_queue.Current);
        }
        catch (Exception ex)
        {
            _logger.Error("Queue refresh failed.", ex);
            ApplySnapshot(PlaybackQueueSnapshot.Failed("Não foi possível ler a fila"));
        }
        finally
        {
            _refreshing = false;
            _refresh.End();
        }
    }

    private void OnQueueChanged(object? sender, PlaybackQueueSnapshot snapshot)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ApplySnapshot(snapshot));
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(PlaybackQueueSnapshot snapshot)
    {
        _snapshot = snapshot;
        IsSimulated = snapshot.Source == PlaybackQueueSource.Simulated;
        MissingCredential = snapshot.MissingCredential;
        CanConnect = snapshot.Kind == PlaybackQueueKind.NeedsAuthorization && _queue.CanAuthorize;
        ConnectCommand.NotifyCanExecuteChanged();
        IsLoading = snapshot.Kind == PlaybackQueueKind.Loading;
        HeaderText = snapshot.Kind == PlaybackQueueKind.MismatchedSession ? "Fila da conta" : "A seguir";
        ShowList = snapshot.Kind is PlaybackQueueKind.Ready or PlaybackQueueKind.MismatchedSession && snapshot.Items.Count > 0;
        ShowStatus = !ShowList || snapshot.Kind == PlaybackQueueKind.MismatchedSession || IsSimulated;
        StatusText = StatusFor(snapshot);
        ApplyItems(snapshot.Items);
    }

    private void ApplyItems(IReadOnlyList<QueueTrack> items)
    {
        var keys = items.Select(item => item.Id).ToList();
        if (_itemKeys.Count == keys.Count && _itemKeys.SequenceEqual(keys, StringComparer.Ordinal))
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].ArtworkBytes is { Length: > 0 })
                {
                    Items[i].ApplyArtwork(items[i].ArtworkBytes);
                }
            }

            return;
        }

        Items.Clear();
        _itemKeys.Clear();
        foreach (var item in items)
        {
            Items.Add(new QueueItemViewModel(item));
            _itemKeys.Add(item.Id);
        }
    }

    private static string StatusFor(PlaybackQueueSnapshot snapshot)
    {
        if (snapshot.Source == PlaybackQueueSource.Simulated && snapshot.Kind == PlaybackQueueKind.Ready)
        {
            return "Simulação";
        }

        return snapshot.Message;
    }

    private void SyncFlyout()
    {
        var changed = IsOpen != _flyout.IsOpen || WantsFocus != _flyout.WantsFocus;
        IsOpen = _flyout.IsOpen;
        WantsFocus = _flyout.WantsFocus;
        if (_timer is not null)
        {
            var due = _flyout.NextDue(_clock.UtcNow);
            if (due is { } delay)
            {
                _timer.Interval = delay < TimeSpan.FromMilliseconds(16) ? TimeSpan.FromMilliseconds(16) : delay;
                if (!_timer.IsEnabled)
                {
                    _timer.Start();
                }
            }
            else if (_timer.IsEnabled)
            {
                _timer.Stop();
            }
        }

        if (changed)
        {
            if (IsOpen)
            {
                _ = RefreshAsync(force: false);
            }

            OpenedChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

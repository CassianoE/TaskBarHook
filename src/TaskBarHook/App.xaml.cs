using System.IO;
using System.Windows;
using System.Windows.Threading;
using TaskBarHook.Desktop;
using TaskBarHook.Logging;
using TaskBarHook.Media;
using TaskBarHook.Presentation;
using TaskBarHook.Queue;
using TaskBarHook.Tray;
using TaskBarHook.Views;

namespace TaskBarHook;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\TaskBarHook.SingleInstance";
    private const string ShowEventName = @"Local\TaskBarHook.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showWait;
    private FileLogger? _logger;
    private IMediaSessionService? _media;
    private IPlaybackQueueService? _queue;
    private CapsuleViewModel? _viewModel;
    private CapsuleHost? _host;
    private TrayIconService? _tray;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _logger = new FileLogger();
        var simulate = e.Args.Contains("--simulate", StringComparer.OrdinalIgnoreCase) ||
                       string.Equals(Environment.GetEnvironmentVariable("TASKBARHOOK_SIMULATE"), "1", StringComparison.OrdinalIgnoreCase);

        if (e.Args.Contains("--probe", StringComparer.OrdinalIgnoreCase))
        {
            var report = await MediaAccessProbe.RunAsync();
            _logger.Info(report);
            await File.WriteAllTextAsync(Path.Combine(_logger.DirectoryPath, "probe.txt"), report);
            Shutdown();
            return;
        }

        if (e.Args.Contains("--seek-selftest", StringComparer.OrdinalIgnoreCase))
        {
            var report = await MediaAccessProbe.RunSeekRoundtripAsync();
            _logger.Info(report);
            await File.WriteAllTextAsync(Path.Combine(_logger.DirectoryPath, "seek-selftest.txt"), report);
            Shutdown();
            return;
        }

        if (e.Args.Contains("--inspect-taskbar", StringComparer.OrdinalIgnoreCase))
        {
            var inspector = new TaskbarOccupancySource(new Win32DesktopEnvironment(), _logger);
            var report = inspector.Dump();
            _logger.Info(report);
            await File.WriteAllTextAsync(Path.Combine(_logger.DirectoryPath, "taskbar.txt"), report);
            Shutdown();
            return;
        }

        if (!TryClaimSingleInstance())
        {
            Shutdown();
            return;
        }

        _logger.Info(simulate
            ? "Starting TaskBarHook with simulated media."
            : "Starting TaskBarHook.");

        try
        {
            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        }
        catch
        {
            // Optional for NotifyIcon.
        }

        var clock = new SystemClock();
        if (simulate)
        {
            var simulated = new SimulatedMediaSessionService(clock, _logger);
            _media = simulated;
            _queue = simulated;
        }
        else
        {
            _media = new SystemMediaSessionService(_logger);
            _queue = new SpotifyPlaybackQueueService(
                SpotifyOptions.FromEnvironment(),
                new ProtectedSecretStore(),
                new SpotifyHttpClient(),
                new LoopbackSpotifyAuth(),
                clock,
                _logger);
        }

        _viewModel = new CapsuleViewModel(_media, clock, _logger, _queue);
        var environment = new Win32DesktopEnvironment();
        var occupancySource = new TaskbarOccupancySource(environment, _logger);
        _host = new CapsuleHost(_viewModel, environment, occupancySource, _logger);
        _tray = new TrayIconService(_viewModel, _logger, ShutdownApp);
        _tray.Show();

        _host.Shell.FullscreenChanged += (_, fullscreen) => _viewModel.SetFullscreen(fullscreen);
        _host.Shell.ExplorerRestarted += (_, _) =>
        {
            _tray.Recreate();
            _host.RefreshPlacement();
        };
        _host.Shell.PowerResumed += async (_, _) =>
        {
            try
            {
                await _media.ReconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.Error("Reconnect after resume failed.", ex);
            }

            _host.RefreshPlacement();
        };

        _host.OccupancyApplied += (_, occupancy) => _tray.WarnLayoutIfNeeded(
            occupancy.LayoutSupported,
            occupancy.LayoutSupported
                ? null
                : "A barra de tarefas está fora do recorte suportado. Use o ícone da área de notificação.");
        _host.Start();

        try
        {
            await _media.StartAsync();
            _logger.Info($"Media current status={_media.Current.Status} title={_media.Current.Track.Title ?? "(none)"}.");
            _viewModel.ApplySnapshot(_media.Current);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to start media service.", ex);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _showWait?.Unregister(null);
        _showSignal?.Dispose();
        _tray?.Dispose();
        _viewModel?.Dispose();
        _host?.Dispose();
        if (_queue is not null && !ReferenceEquals(_queue, _media))
        {
            await _queue.DisposeAsync();
        }

        if (_media is not null)
        {
            await _media.DisposeAsync();
        }

        _logger?.Info("TaskBarHook exited.");
        _logger?.Dispose();
        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _mutex.Dispose();
        }

        base.OnExit(e);
    }

    private bool TryClaimSingleInstance()
    {
        _mutex = new Mutex(true, MutexName, out var created);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (created)
        {
            _showWait = ThreadPool.RegisterWaitForSingleObject(
                _showSignal,
                (_, _) => Dispatcher.BeginInvoke(() => _viewModel?.ShowPanelFromTray()),
                null,
                Timeout.Infinite,
                false);
            return true;
        }

        try
        {
            _showSignal.Set();
        }
        catch
        {
            // First instance may be shutting down.
        }

        _mutex.Dispose();
        _mutex = null;
        _showSignal.Dispose();
        _showSignal = null;
        return false;
    }

    private void ShutdownApp()
    {
        Dispatcher.BeginInvoke(Shutdown);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("Dispatcher exception.", e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _logger?.Error("Unhandled exception.", ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.Error("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }
}

using TaskBarHook.Logging;

namespace TaskBarHook.Desktop;

public sealed class OccupancyRefreshCoordinator : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    private readonly ITaskbarOccupancySource _source;
    private readonly TimeSpan _timeout;
    private readonly IAppLogger? _logger;
    private readonly object _gate = new();

    private int _generation;
    private bool _running;
    private bool _queued;
    private bool _disposed;

    public OccupancyRefreshCoordinator(
        ITaskbarOccupancySource source,
        TimeSpan? timeout = null,
        IAppLogger? logger = null)
    {
        _source = source;
        _timeout = timeout ?? DefaultTimeout;
        _logger = logger;
    }

    public event EventHandler<OccupancyRefreshResult>? OccupancyReady;

    public int Generation
    {
        get
        {
            lock (_gate)
            {
                return _generation;
            }
        }
    }

    public void Request()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _generation++;
            if (_running)
            {
                _queued = true;
                return;
            }

            _running = true;
        }

        _ = Task.Run(RunAsync);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _queued = false;
        }
    }

    private async Task RunAsync()
    {
        while (true)
        {
            int gen;
            lock (_gate)
            {
                if (_disposed)
                {
                    _running = false;
                    return;
                }

                gen = _generation;
                _queued = false;
            }

            TaskbarOccupancy? result = null;
            try
            {
                result = await ReadGenerationAsync(gen).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Error("Occupancy worker failed.", ex);
                if (IsCurrent(gen))
                {
                    result = TaskbarOccupancy.Unavailable(ex.GetType().Name);
                }
            }

            if (result is not null && IsCurrent(gen))
            {
                Publish(gen, result);
            }

            lock (_gate)
            {
                if (_disposed || (!_queued && _generation == gen))
                {
                    _running = false;
                    return;
                }
            }
        }
    }

    private async Task<TaskbarOccupancy?> ReadGenerationAsync(int gen)
    {
        var read = Task.Run(() => _source.Read());
        var completed = await Task.WhenAny(read, Task.Delay(_timeout)).ConfigureAwait(false);
        if (completed != read)
        {
            if (!_disposed)
            {
                Publish(gen, TaskbarOccupancy.Unavailable("occupancy-timeout"));
            }

            try
            {
                await read.ConfigureAwait(false);
            }
            catch
            {
                if (!IsCurrent(gen))
                {
                    return null;
                }

                throw;
            }
        }

        if (!IsCurrent(gen))
        {
            return null;
        }

        return await read.ConfigureAwait(false);
    }

    private void Publish(int generation, TaskbarOccupancy occupancy)
    {
        OccupancyReady?.Invoke(this, new OccupancyRefreshResult(generation, occupancy));
    }

    private bool IsCurrent(int generation)
    {
        lock (_gate)
        {
            return !_disposed && _generation == generation;
        }
    }
}

using System.IO;

namespace TaskBarHook.Logging;

public sealed class FileLogger : IAppLogger, IDisposable
{
    public const long MaxBytes = 512 * 1024;

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _path;
    private readonly string _previousPath;
    private bool _disposed;

    public FileLogger(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskBarHook",
            "logs");
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "app.log");
        _previousPath = Path.Combine(_directory, "app.prev.log");
    }

    public string DirectoryPath => _directory;

    public string FilePath => _path;

    public void Info(string message) => Write("INF", message, null);

    public void Warn(string message) => Write("WRN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERR", message, exception);

    public void Dispose()
    {
        _disposed = true;
    }

    private void Write(string level, string message, Exception? exception)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                RotateIfNeeded();
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (exception is not null)
                {
                    line += Environment.NewLine + exception;
                }

                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        var info = new FileInfo(_path);
        if (info.Length < MaxBytes)
        {
            return;
        }

        if (File.Exists(_previousPath))
        {
            File.Delete(_previousPath);
        }

        File.Move(_path, _previousPath);
    }
}

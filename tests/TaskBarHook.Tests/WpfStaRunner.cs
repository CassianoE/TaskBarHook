using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;

namespace TaskBarHook.Tests;

// All WPF control tests share one STA thread with a running Dispatcher and a
// plain Application holding the shared styles. Styles are thread-affine, so a
// fresh thread per test would break resource sharing. The Application must be
// plain: instantiating TaskBarHook.App would run its real OnStartup (single
// instance mutex, tray icon, shell hooks) as soon as the dispatcher pumps.
public static class WpfStaRunner
{
    private sealed record Work(Func<object?> Func, TaskCompletionSource<object?> Completion);

    private static readonly BlockingCollection<Work> Queue = new();
    private static readonly Thread StaThread;
    private static readonly ManualResetEventSlim Ready = new(false);
    private static Exception? _startupError;

    static WpfStaRunner()
    {
        StaThread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "WpfTestSta"
        };
        StaThread.SetApartmentState(ApartmentState.STA);
        StaThread.Start();
    }

    public static void Run(Action action)
    {
        Run(() =>
        {
            action();
            return (object?)null;
        });
    }

    public static T Run<T>(Func<T> func)
    {
        Ready.Wait(TimeSpan.FromSeconds(30));
        if (_startupError is not null)
        {
            throw new InvalidOperationException("WPF STA thread failed to start.", _startupError);
        }

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Add(new Work(() => func(), completion));
        return (T)completion.Task.GetAwaiter().GetResult()!;
    }

    private static void Loop()
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/TaskBarHook;component/themes/sharedresources.xaml", UriKind.Relative)
            });
            var dispatcher = Dispatcher.CurrentDispatcher;
            Ready.Set();

            foreach (var work in Queue.GetConsumingEnumerable())
            {
                dispatcher.Invoke(() =>
                {
                    try
                    {
                        work.Completion.SetResult(work.Func());
                    }
                    catch (Exception ex)
                    {
                        work.Completion.SetException(ex);
                    }
                });
                // Let data binding, layout and fire-and-forget continuations settle.
                dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            }
        }
        catch (Exception ex)
        {
            _startupError = ex;
            Ready.Set();
        }
    }
}

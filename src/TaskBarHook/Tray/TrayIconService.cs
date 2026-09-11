using TaskBarHook.Desktop;
using TaskBarHook.Logging;
using TaskBarHook.Presentation;

namespace TaskBarHook.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly CapsuleViewModel _viewModel;
    private readonly IAppLogger _logger;
    private readonly Action _exit;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private System.Drawing.Icon? _icon;
    private bool _layoutWarned;
    private bool _disposed;

    public TrayIconService(CapsuleViewModel viewModel, IAppLogger logger, Action exit)
    {
        _viewModel = viewModel;
        _logger = logger;
        _exit = exit;
    }

    public void Show()
    {
        _icon = CreateIcon();
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _icon,
            Visible = true,
            Text = "TaskBarHook"
        };
        _notifyIcon.ContextMenuStrip = BuildMenu();
        _notifyIcon.DoubleClick += (_, _) => _viewModel.ShowPanelFromTray();
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel.PropertyChanged += OnViewModelChanged;
    }

    public void Recreate()
    {
        _viewModel.PropertyChanged -= OnViewModelChanged;
        DisposeIcon();
        Show();
        _logger.Info("Tray icon recreated.");
    }

    public void WarnLayoutIfNeeded(bool layoutSupported, string? warning)
    {
        if (layoutSupported || _layoutWarned || string.IsNullOrWhiteSpace(warning))
        {
            return;
        }

        _layoutWarned = true;
        _notifyIcon?.ShowBalloonTip(4000, "TaskBarHook", warning, System.Windows.Forms.ToolTipIcon.Info);
        _logger.Warn(warning);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelChanged;
        DisposeIcon();
    }

    private void DisposeIcon()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _icon?.Dispose();
        _icon = null;
    }

    private System.Windows.Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var open = new System.Windows.Forms.ToolStripMenuItem("Abrir painel");
        open.Click += (_, _) => _viewModel.ShowPanelFromTray();
        var toggle = new System.Windows.Forms.ToolStripMenuItem(ToggleText);
        toggle.Click += (_, _) => _viewModel.ToggleUserHidden();
        var exit = new System.Windows.Forms.ToolStripMenuItem("Sair");
        exit.Click += (_, _) => _exit();
        menu.Items.Add(open);
        menu.Items.Add(toggle);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(exit);
        menu.Opening += (_, _) => toggle.Text = ToggleText;
        return menu;
    }

    private string ToggleText => _viewModel.UserHidden ? "Mostrar cápsula" : "Ocultar cápsula";

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        if (e.PropertyName is nameof(CapsuleViewModel.Title) or nameof(CapsuleViewModel.HasLiveSession))
        {
            var text = _viewModel.HasLiveSession
                ? Truncate("TaskBarHook — " + _viewModel.Title)
                : "TaskBarHook";
            _notifyIcon.Text = text;
        }
    }

    private static string Truncate(string value) => value.Length <= 63 ? value : value[..60] + "...";

    private static System.Drawing.Icon CreateIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(System.Drawing.Color.Transparent);
        using (var background = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 28, 28, 30)))
        {
            graphics.FillEllipse(background, 1, 1, 30, 30);
        }

        using (var foreground = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 243, 243, 243)))
        {
            graphics.FillPolygon(foreground,
            [
                new System.Drawing.Point(12, 10),
                new System.Drawing.Point(12, 22),
                new System.Drawing.Point(23, 16)
            ]);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var created = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)created.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TaskBarHook.Desktop;
using TaskBarHook.Presentation;

namespace TaskBarHook.Views;

public partial class CompactWindow : Window
{
    public CompactWindow(CapsuleViewModel viewModel)
    {
        DataContext = viewModel;
        ViewModel = viewModel;
        InitializeComponent();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CapsuleViewModel.CompactFit))
            {
                ApplyFit();
            }
        };
        ApplyFit();
    }

    public CapsuleViewModel ViewModel { get; }

    public nint Handle { get; private set; }

    public void Attach()
    {
        Handle = WindowChromeHelper.Apply(this, allowActivation: false);
    }

    public void ApplyFit()
    {
        var mini = ViewModel.CompactFit == CompactFit.Mini;
        FullHost.Visibility = mini ? Visibility.Collapsed : Visibility.Visible;
        MiniHost.Visibility = mini ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowPassive()
    {
        ShowActivated = false;
        if (!IsVisible)
        {
            Show();
        }

        WindowChromeHelper.RaiseWithoutActivating(this);
        Dispatcher.BeginInvoke(() => WindowChromeHelper.RaiseWithoutActivating(this), DispatcherPriority.Render);
    }

    private void Surface_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInsideButton(source))
        {
            return;
        }

        ViewModel.ToggleExpandedCommand.Execute(null);
        e.Handled = true;
    }

    private static bool IsInsideButton(DependencyObject source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is Button)
            {
                return true;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current)
                      ?? LogicalTreeHelper.GetParent(current);
        }

        return false;
    }
}

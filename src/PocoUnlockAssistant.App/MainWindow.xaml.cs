using System.Windows;
using PocoUnlockAssistant.App.ViewModels;

namespace PocoUnlockAssistant.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        FitToWorkArea();
    }

    /// <summary>
    /// The window uses a custom title bar, so a window taller or wider than the current work
    /// area would put the caption buttons off-screen. At 150% scaling a 1920x1080 display
    /// exposes only 1280x720 DIPs, which is smaller than the designed default size.
    /// </summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        if (Width > area.Width)
        {
            Width = Math.Max(MinWidth, area.Width);
        }

        if (Height > area.Height)
        {
            Height = Math.Max(MinHeight, area.Height);
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}

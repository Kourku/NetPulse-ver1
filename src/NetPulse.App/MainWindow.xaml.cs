using System;
using System.ComponentModel;
using System.Windows;
using NetPulse.App.Tray;
using NetPulse.App.ViewModels;

namespace NetPulse.App;

public partial class MainWindow : Window
{
    private SystemTrayManager? _trayManager;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            _trayManager = new SystemTrayManager(this, vm);
        }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        // Minimize to tray if minimized
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            _trayManager?.ShowNotification("NetPulse Running in Background", "Click tray icon to restore window.", System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OnAppExiting();
        }
        _trayManager?.Dispose();
    }
}
using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using NetPulse.App.ViewModels;

namespace NetPulse.App.Tray;

public class SystemTrayManager : IDisposable
{
    private readonly Window _mainWindow;
    private readonly MainViewModel _viewModel;
    private NotifyIcon? _notifyIcon;

    public SystemTrayManager(Window mainWindow, MainViewModel viewModel)
    {
        _mainWindow = mainWindow;
        _viewModel = viewModel;
        InitializeTray();
    }

    private void InitializeTray()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "NetPulse - Real-time Network Quality",
            Visible = true
        };

        try
        {
            var iconUri = new Uri("pack://application:,,,/app.ico");
            var streamInfo = System.Windows.Application.GetResourceStream(iconUri);
            if (streamInfo != null)
            {
                using var s = streamInfo.Stream;
                _notifyIcon.Icon = new Icon(s);
            }
            else
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }
        }
        catch
        {
            _notifyIcon.Icon = SystemIcons.Application;
        }

        var contextMenu = new ContextMenuStrip();
        var showItem = new ToolStripMenuItem("Show NetPulse", null, (s, e) => RestoreWindow());
        showItem.Font = new Font(showItem.Font, System.Drawing.FontStyle.Bold);
        contextMenu.Items.Add(showItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        contextMenu.Items.Add("Run Bufferbloat Test", null, (s, e) =>
        {
            RestoreWindow();
            _viewModel.CurrentTab = "Bufferbloat";
            _viewModel.RunBufferbloatCommand.Execute(null);
        });

        contextMenu.Items.Add("Run Speed Test", null, (s, e) =>
        {
            RestoreWindow();
            _viewModel.CurrentTab = "SpeedTest";
            _viewModel.RunSpeedTestCommand.Execute(null);
        });

        contextMenu.Items.Add(new ToolStripSeparator());

        contextMenu.Items.Add("Exit NetPulse", null, (s, e) =>
        {
            _notifyIcon.Visible = false;
            _mainWindow.Close();
            System.Windows.Application.Current.Shutdown();
        });

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => RestoreWindow();
    }

    public void RestoreWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (_notifyIcon != null && _notifyIcon.Visible)
        {
            _notifyIcon.ShowBalloonTip(3000, title, message, icon);
        }
    }

    public void Dispose()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}

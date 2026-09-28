using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace NetPulse.Installer;

[SupportedOSPlatform("windows")]
public partial class MainWindow : Window
{
    private readonly bool _isUninstallMode;
    private readonly string _defaultInstallDir;

    public MainWindow()
    {
        InitializeComponent();

        _defaultInstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "NetPulse");

        TxtInstallDir.Text = _defaultInstallDir;

        var args = Environment.GetCommandLineArgs();
        _isUninstallMode = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
                                         a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase) ||
                                         a.Equals("-u", StringComparison.OrdinalIgnoreCase));

        if (_isUninstallMode)
        {
            Title = "Uninstall NetPulse";
            TitleBlock.Text = "Uninstall NetPulse Diagnostics";
            SubtitleBlock.Text = "Remove NetPulse and its components from your computer.";
            InstallPanel.Visibility = Visibility.Collapsed;
            UninstallPanel.Visibility = Visibility.Visible;
            BtnAction.Content = "Uninstall";
            TxtStatus.Text = "Ready to uninstall.";
        }
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select NetPulse Installation Folder",
            InitialDirectory = TxtInstallDir.Text
        };

        if (dialog.ShowDialog() == true)
        {
            TxtInstallDir.Text = dialog.FolderName;
        }
    }

    private async void OnActionClick(object sender, RoutedEventArgs e)
    {
        if (BtnAction.Content.ToString() == "Finished" || BtnAction.Content.ToString() == "Close")
        {
            Close();
            return;
        }

        if (_isUninstallMode)
        {
            await RunUninstallAsync();
        }
        else
        {
            await RunInstallAsync();
        }
    }

    private async Task RunInstallAsync()
    {
        string installDir = TxtInstallDir.Text.Trim();
        if (string.IsNullOrWhiteSpace(installDir)) return;

        BtnAction.IsEnabled = false;
        BtnCancel.IsEnabled = false;
        ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.Value = 15;
        TxtStatus.Text = "Creating installation directory...";

        await Task.Run(() =>
        {
            Directory.CreateDirectory(installDir);
        });

        ProgressBar.Value = 35;
        TxtStatus.Text = "Locating application binaries...";

        string? sourceExe = FindSourceBinary();
        if (string.IsNullOrEmpty(sourceExe) || !File.Exists(sourceExe))
        {
            MessageBox.Show(
                "Could not find NetPulse.exe binary. Please ensure NetPulse.exe is present in the installer directory or dist/portable folder.",
                "Installation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            BtnAction.IsEnabled = true;
            BtnCancel.IsEnabled = true;
            ProgressBar.Visibility = Visibility.Collapsed;
            TxtStatus.Text = "Installation failed: missing binary.";
            return;
        }

        ProgressBar.Value = 55;
        TxtStatus.Text = "Copying files to installation folder...";

        string targetExe = Path.Combine(installDir, "NetPulse.exe");
        string targetIco = Path.Combine(installDir, "app.ico");
        string targetSetup = Path.Combine(installDir, "NetPulse-Setup.exe");

        await Task.Run(() =>
        {
            File.Copy(sourceExe, targetExe, true);

            string currentProcessExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (!string.IsNullOrEmpty(currentProcessExe) && File.Exists(currentProcessExe) &&
                !currentProcessExe.Equals(targetSetup, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(currentProcessExe, targetSetup, true);
            }

            string? srcIco = Path.Combine(Path.GetDirectoryName(sourceExe) ?? "", "app.ico");
            if (File.Exists(srcIco))
            {
                File.Copy(srcIco, targetIco, true);
            }
        });

        ProgressBar.Value = 75;
        TxtStatus.Text = "Creating desktop and start menu shortcuts...";

        await Task.Run(() =>
        {
            if (ChkDesktopShortcut.IsChecked == true)
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                CreateShortcut(Path.Combine(desktopPath, "NetPulse.lnk"), targetExe, installDir, "NetPulse - Real-time Network Diagnostics");
            }

            if (ChkStartMenuShortcut.IsChecked == true)
            {
                string programsPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                CreateShortcut(Path.Combine(programsPath, "NetPulse.lnk"), targetExe, installDir, "NetPulse - Real-time Network Diagnostics");
            }

            // Register with Windows Uninstall in registry
            RegisterUninstall(installDir, targetSetup, targetExe);
        });

        ProgressBar.Value = 100;
        TxtStatus.Text = "Installation completed successfully!";

        if (ChkLaunchAfter.IsChecked == true)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = targetExe,
                    WorkingDirectory = installDir,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        BtnAction.Content = "Finished";
        BtnAction.IsEnabled = true;
        BtnCancel.Visibility = Visibility.Collapsed;
    }

    private async Task RunUninstallAsync()
    {
        BtnAction.IsEnabled = false;
        BtnCancel.IsEnabled = false;
        ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.Value = 30;
        TxtStatus.Text = "Removing shortcuts and registry entries...";

        string installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

        await Task.Run(() =>
        {
            // Remove Shortcuts
            string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "NetPulse.lnk");
            if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

            string startMenuLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "NetPulse.lnk");
            if (File.Exists(startMenuLnk)) File.Delete(startMenuLnk);

            // Remove Registry
            UnregisterUninstall();
        });

        ProgressBar.Value = 80;
        TxtStatus.Text = "Cleaning up installation files...";

        // Self-deletion cmd script
        string cmdScript = $"/c timeout /t 2 /nobreak > NUL & rmdir /s /q \"{installDir}\"";
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = cmdScript,
            CreateNoWindow = true,
            UseShellExecute = false
        });

        ProgressBar.Value = 100;
        TxtStatus.Text = "NetPulse has been uninstalled.";

        MessageBox.Show("NetPulse has been successfully removed from your computer.", "NetPulse Uninstall", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private static string? FindSourceBinary()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates = new[]
        {
            Path.Combine(baseDir, "NetPulse.exe"),
            Path.Combine(baseDir, "payload", "NetPulse.exe"),
            Path.Combine(baseDir, "portable", "NetPulse.exe"),
            Path.Combine(baseDir, "..", "dist", "portable", "NetPulse.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "dist", "portable", "NetPulse.exe"),
            @"E:\LAGG\dist\portable\NetPulse.exe"
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return Path.GetFullPath(c);
        }

        return null;
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = targetPath;
                    shortcut.WorkingDirectory = workingDir;
                    shortcut.Description = description;
                    shortcut.Save();
                }
            }
        }
        catch { }
    }

    private static void RegisterUninstall(string installDir, string setupExe, string appExe)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NetPulse");
            if (key != null)
            {
                key.SetValue("DisplayName", "NetPulse Real-Time Network Quality & Path Diagnostics");
                key.SetValue("DisplayVersion", "1.0.0");
                key.SetValue("Publisher", "NetPulse Diagnostics");
                key.SetValue("DisplayIcon", $"{appExe},0");
                key.SetValue("InstallLocation", installDir);
                key.SetValue("UninstallString", $"\"{setupExe}\" --uninstall");
                key.SetValue("EstimatedSize", 175000, RegistryValueKind.DWord); // in KB
            }
        }
        catch { }
    }

    private static void UnregisterUninstall()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NetPulse", false);
        }
        catch { }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
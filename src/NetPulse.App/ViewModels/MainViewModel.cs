using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using NetPulse.Core.Models;
using NetPulse.Core.Services;

namespace NetPulse.App.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly NetworkDiscoveryService _discoveryService;
    private readonly PingService _pingService;
    private readonly TcpProbeService _tcpService;
    private readonly HttpProbeService _httpService;
    private readonly DnsProbeService _dnsService;
    private readonly DiagnosticEngine _diagnosticEngine;
    private readonly EventLogService _eventLogService;
    private readonly StorageService _storageService;
    private readonly WindowsIntegrationService _winIntegrationService;
    private readonly TracerouteService _tracerouteService;
    private readonly MtrService _mtrService;
    private readonly RouteAnalysisService _routeAnalysisService;
    private readonly BufferbloatService _bufferbloatService;
    private readonly SpeedTestService _speedTestService;
    private readonly MeasurementEngine _engine;

    // View Navigation
    private string _currentTab = "Dashboard";
    private bool _isBeginnerMode = true;

    // Diagnostic state
    private string _statusHeadline = "Initializing connection diagnostics...";
    private string _statusCategory = "Analyzing";
    private string _likelyCause = "Gathering initial network packets across local and Internet hops.";
    private string _recommendedAction = "Monitoring active.";
    private Brush _statusBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
    private string _evidenceSummary = string.Empty;

    // Quick stats
    private double _quickGatewayLatency;
    private double _quickInternetLatency;
    private double _quickJitter;
    private double _quickPacketLoss;
    private double _quickDnsLatency;

    // Network Interface
    private NetworkInterfaceInfo _activeNic = new();

    // MTR / Route Analysis
    private string _mtrDestination = "1.1.1.1";
    private bool _isMtrRunning;
    private string _mtrStatusText = "Ready to trace route path.";
    private string _pathCompareDestA = "1.1.1.1";
    private string _pathCompareDestB = "8.8.8.8";
    private string _pathCompareSummary = string.Empty;

    // Bufferbloat
    private bool _isBufferbloatRunning;
    private int _bufferbloatProgress;
    private string _bufferbloatStatus = "Ready to test bufferbloat under load.";
    private BufferbloatResult? _latestBufferbloatResult;

    // Speed Test
    private bool _isSpeedTestRunning;
    private int _speedTestProgress;
    private double _currentSpeedTestMbps;
    private string _speedTestStatus = "Ready to measure bandwidth. Warning: generates heavy traffic.";
    private SpeedTestResult? _latestSpeedTestResult;

    public ObservableCollection<TargetMetricsViewModel> Targets { get; } = new();
    public ObservableCollection<ConnectionEvent> Events { get; } = new();
    public ObservableCollection<RouteHop> MtrHops { get; } = new();
    public ObservableCollection<string> RouteAnomalies { get; } = new();

    // Commands
    public RelayCommand SwitchTabCommand { get; }
    public RelayCommand ToggleModeCommand { get; }
    public RelayCommand StartMtrCommand { get; }
    public RelayCommand StopMtrCommand { get; }
    public RelayCommand ComparePathsCommand { get; }
    public RelayCommand RunBufferbloatCommand { get; }
    public RelayCommand RunSpeedTestCommand { get; }
    public RelayCommand ExportCsvCommand { get; }
    public RelayCommand ExportJsonCommand { get; }
    public RelayCommand ExportReportCommand { get; }
    public RelayCommand ClearEventsCommand { get; }

    public MainViewModel()
    {
        _discoveryService = new NetworkDiscoveryService();
        _pingService = new PingService();
        _tcpService = new TcpProbeService();
        _httpService = new HttpProbeService();
        _dnsService = new DnsProbeService();
        _diagnosticEngine = new DiagnosticEngine();
        _eventLogService = new EventLogService();
        _storageService = new StorageService();
        _winIntegrationService = new WindowsIntegrationService();
        _tracerouteService = new TracerouteService();
        _mtrService = new MtrService(_tracerouteService);
        _routeAnalysisService = new RouteAnalysisService();
        _bufferbloatService = new BufferbloatService(_pingService);
        _speedTestService = new SpeedTestService();

        _engine = new MeasurementEngine(
            _discoveryService,
            _pingService,
            _tcpService,
            _httpService,
            _dnsService,
            _diagnosticEngine,
            _eventLogService);

        // Bind engine events
        _engine.MetricsUpdated += (s, metrics) => Application.Current.Dispatcher.Invoke(() => UpdateMetrics(metrics));
        _engine.DiagnosisUpdated += (s, diag) => Application.Current.Dispatcher.Invoke(() => UpdateDiagnosis(diag));
        _engine.NetworkInterfaceUpdated += (s, nic) => Application.Current.Dispatcher.Invoke(() => ActiveNic = nic);
        _eventLogService.EventLogged += (s, evt) => Application.Current.Dispatcher.Invoke(() =>
        {
            Events.Insert(0, evt);
            if (Events.Count > 300) Events.RemoveAt(Events.Count - 1);
        });

        _mtrService.HopsUpdated += (s, hops) => Application.Current.Dispatcher.Invoke(() => UpdateMtrHops(hops));
        _mtrService.RouteChanged += (s, chg) => Application.Current.Dispatcher.Invoke(() =>
        {
            _eventLogService.LogEvent("RouteChange", "Warning", chg.ChangeDescription, chg.Destination, chg.CurrentLatencyMs);
        });

        // Initialize Commands
        SwitchTabCommand = new RelayCommand(p => CurrentTab = p?.ToString() ?? "Dashboard");
        ToggleModeCommand = new RelayCommand(() => IsBeginnerMode = !IsBeginnerMode);
        StartMtrCommand = new RelayCommand(async () => await StartMtrAsync());
        StopMtrCommand = new RelayCommand(() => StopMtr());
        ComparePathsCommand = new RelayCommand(async () => await ComparePathsAsync());
        RunBufferbloatCommand = new RelayCommand(async () => await RunBufferbloatAsync());
        RunSpeedTestCommand = new RelayCommand(async () => await RunSpeedTestAsync());
        ExportCsvCommand = new RelayCommand(async () => await ExportCsvAsync());
        ExportJsonCommand = new RelayCommand(async () => await ExportJsonAsync());
        ExportReportCommand = new RelayCommand(async () => await ExportReportAsync());
        ClearEventsCommand = new RelayCommand(() =>
        {
            _eventLogService.Clear();
            Events.Clear();
        });

        // Start measurement engine
        _engine.Start();
    }

    #region Properties

    public string CurrentTab
    {
        get => _currentTab;
        set => SetProperty(ref _currentTab, value);
    }

    public bool IsBeginnerMode
    {
        get => _isBeginnerMode;
        set => SetProperty(ref _isBeginnerMode, value);
    }

    public string StatusHeadline
    {
        get => _statusHeadline;
        set => SetProperty(ref _statusHeadline, value);
    }

    public string StatusCategory
    {
        get => _statusCategory;
        set => SetProperty(ref _statusCategory, value);
    }

    public string LikelyCause
    {
        get => _likelyCause;
        set => SetProperty(ref _likelyCause, value);
    }

    public string RecommendedAction
    {
        get => _recommendedAction;
        set => SetProperty(ref _recommendedAction, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        set => SetProperty(ref _statusBrush, value);
    }

    public string EvidenceSummary
    {
        get => _evidenceSummary;
        set => SetProperty(ref _evidenceSummary, value);
    }

    public double QuickGatewayLatency
    {
        get => _quickGatewayLatency;
        set => SetProperty(ref _quickGatewayLatency, value);
    }

    public double QuickInternetLatency
    {
        get => _quickInternetLatency;
        set => SetProperty(ref _quickInternetLatency, value);
    }

    public double QuickJitter
    {
        get => _quickJitter;
        set => SetProperty(ref _quickJitter, value);
    }

    public double QuickPacketLoss
    {
        get => _quickPacketLoss;
        set => SetProperty(ref _quickPacketLoss, value);
    }

    public double QuickDnsLatency
    {
        get => _quickDnsLatency;
        set => SetProperty(ref _quickDnsLatency, value);
    }

    public NetworkInterfaceInfo ActiveNic
    {
        get => _activeNic;
        set => SetProperty(ref _activeNic, value);
    }

    public string MtrDestination
    {
        get => _mtrDestination;
        set => SetProperty(ref _mtrDestination, value);
    }

    public bool IsMtrRunning
    {
        get => _isMtrRunning;
        set => SetProperty(ref _isMtrRunning, value);
    }

    public string MtrStatusText
    {
        get => _mtrStatusText;
        set => SetProperty(ref _mtrStatusText, value);
    }

    public string PathCompareDestA
    {
        get => _pathCompareDestA;
        set => SetProperty(ref _pathCompareDestA, value);
    }

    public string PathCompareDestB
    {
        get => _pathCompareDestB;
        set => SetProperty(ref _pathCompareDestB, value);
    }

    public string PathCompareSummary
    {
        get => _pathCompareSummary;
        set => SetProperty(ref _pathCompareSummary, value);
    }

    public bool IsBufferbloatRunning
    {
        get => _isBufferbloatRunning;
        set => SetProperty(ref _isBufferbloatRunning, value);
    }

    public int BufferbloatProgress
    {
        get => _bufferbloatProgress;
        set => SetProperty(ref _bufferbloatProgress, value);
    }

    public string BufferbloatStatus
    {
        get => _bufferbloatStatus;
        set => SetProperty(ref _bufferbloatStatus, value);
    }

    public BufferbloatResult? LatestBufferbloatResult
    {
        get => _latestBufferbloatResult;
        set => SetProperty(ref _latestBufferbloatResult, value);
    }

    public bool IsSpeedTestRunning
    {
        get => _isSpeedTestRunning;
        set => SetProperty(ref _isSpeedTestRunning, value);
    }

    public int SpeedTestProgress
    {
        get => _speedTestProgress;
        set => SetProperty(ref _speedTestProgress, value);
    }

    public double CurrentSpeedTestMbps
    {
        get => _currentSpeedTestMbps;
        set => SetProperty(ref _currentSpeedTestMbps, value);
    }

    public string SpeedTestStatus
    {
        get => _speedTestStatus;
        set => SetProperty(ref _speedTestStatus, value);
    }

    public SpeedTestResult? LatestSpeedTestResult
    {
        get => _latestSpeedTestResult;
        set => SetProperty(ref _latestSpeedTestResult, value);
    }

    #endregion

    private void UpdateMetrics(IReadOnlyList<TargetMetrics> metrics)
    {
        foreach (var m in metrics)
        {
            var existing = Targets.FirstOrDefault(t => t.TargetId == m.TargetId);
            if (existing != null)
            {
                existing.UpdateFromModel(m);
            }
            else
            {
                Targets.Add(new TargetMetricsViewModel(m));
            }
        }

        var gw = metrics.FirstOrDefault(m => m.IsGateway);
        if (gw != null) QuickGatewayLatency = gw.CurrentLatency;

        var dns = metrics.FirstOrDefault(m => m.IsDnsResolver);
        if (dns != null) QuickDnsLatency = dns.CurrentLatency;

        var internet = metrics.Where(m => !m.IsGateway && !m.IsDnsResolver && m.TotalProbes > 0).ToList();
        if (internet.Count > 0)
        {
            QuickInternetLatency = internet.Average(i => i.CurrentLatency);
            QuickJitter = internet.Average(i => i.Jitter);
            QuickPacketLoss = internet.Average(i => i.PacketLossPercent);
        }
    }

    private void UpdateDiagnosis(DiagnosticResult diag)
    {
        StatusHeadline = diag.Headline;
        StatusCategory = diag.Category.ToString();
        LikelyCause = diag.LikelyCause;
        RecommendedAction = diag.RecommendedAction;
        EvidenceSummary = diag.DetailSummary;

        StatusBrush = diag.Status switch
        {
            DiagnosticStatus.Critical => new SolidColorBrush(Color.FromRgb(239, 68, 68)), // Red
            DiagnosticStatus.Degraded => new SolidColorBrush(Color.FromRgb(245, 158, 11)), // Amber
            _ => new SolidColorBrush(Color.FromRgb(16, 185, 129)) // Green
        };
    }

    private void UpdateMtrHops(List<RouteHop> hops)
    {
        MtrHops.Clear();
        foreach (var h in hops) MtrHops.Add(h);

        var anomalies = _routeAnalysisService.DetectAnomalies(new RoutePath
        {
            Destination = MtrDestination,
            Hops = hops,
            DestinationReached = hops.Any(h => h.IsResponding && h.HopNumber == hops.Count)
        });

        RouteAnomalies.Clear();
        foreach (var a in anomalies) RouteAnomalies.Add(a);
    }

    private async Task StartMtrAsync()
    {
        if (string.IsNullOrWhiteSpace(MtrDestination)) return;
        IsMtrRunning = true;
        MtrStatusText = $"Tracing route path to {MtrDestination}...";
        MtrHops.Clear();
        RouteAnomalies.Clear();

        await _mtrService.StartAsync(MtrDestination.Trim(), 25, 1000);
        MtrStatusText = $"Continuous MTR active for {MtrDestination}.";
    }

    private void StopMtr()
    {
        _mtrService.Stop();
        IsMtrRunning = false;
        MtrStatusText = "MTR paused.";
    }

    private async Task ComparePathsAsync()
    {
        PathCompareSummary = "Tracing both paths for comparison...";
        try
        {
            var traceA = await _tracerouteService.TraceRouteAsync(PathCompareDestA.Trim(), 20, 1500);
            var traceB = await _tracerouteService.TraceRouteAsync(PathCompareDestB.Trim(), 20, 1500);

            var comparison = _routeAnalysisService.ComparePaths(traceA, traceB);
            PathCompareSummary = comparison.Summary;
        }
        catch (Exception ex)
        {
            PathCompareSummary = $"Path comparison error: {ex.Message}";
        }
    }

    private async Task RunBufferbloatAsync()
    {
        if (IsBufferbloatRunning) return;
        IsBufferbloatRunning = true;
        BufferbloatProgress = 5;
        BufferbloatStatus = "Initiating bufferbloat test...";

        var progress = new Progress<BufferbloatProgress>(p =>
        {
            BufferbloatProgress = p.Percent;
            BufferbloatStatus = p.StatusText;
        });

        try
        {
            var res = await _bufferbloatService.RunTestAsync("1.1.1.1", progress);
            LatestBufferbloatResult = res;
            BufferbloatStatus = $"Completed with Grade {res.Grade}!";
            _eventLogService.LogEvent(
                "BufferbloatTest",
                res.Grade == "D" || res.Grade == "F" ? "Warning" : "Info",
                $"Bufferbloat test finished: Grade {res.Grade} (+{Math.Max(res.DownloadDeltaMs, res.UploadDeltaMs):F0} ms delta)",
                "Bufferbloat");
        }
        catch (Exception ex)
        {
            BufferbloatStatus = $"Test error: {ex.Message}";
        }
        finally
        {
            IsBufferbloatRunning = false;
        }
    }

    private async Task RunSpeedTestAsync()
    {
        if (IsSpeedTestRunning) return;

        var confirm = MessageBox.Show(
            "The speed test generates substantial network download and upload traffic to measure maximum connection throughput. Proceed?",
            "Start Bandwidth Throughput Test",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        IsSpeedTestRunning = true;
        SpeedTestProgress = 5;
        SpeedTestStatus = "Starting multi-stream bandwidth test...";

        var progress = new Progress<SpeedTestProgress>(p =>
        {
            SpeedTestProgress = p.Percent;
            CurrentSpeedTestMbps = p.CurrentSpeedMbps;
            SpeedTestStatus = p.StatusText;
        });

        try
        {
            var res = await _speedTestService.RunSpeedTestAsync(progress);
            LatestSpeedTestResult = res;
            if (res.CompletedSuccessfully)
            {
                SpeedTestStatus = $"Finished: {res.DownloadSpeedMbps:F1} Mbps Download | {res.UploadSpeedMbps:F1} Mbps Upload";
                _eventLogService.LogEvent(
                    "SpeedTest",
                    "Info",
                    $"Throughput test: {res.DownloadSpeedMbps:F1} Mbps Down, {res.UploadSpeedMbps:F1} Mbps Up",
                    "SpeedTest");
            }
            else
            {
                SpeedTestStatus = $"Test stopped: {res.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            SpeedTestStatus = $"Speed test error: {ex.Message}";
        }
        finally
        {
            IsSpeedTestRunning = false;
        }
    }

    private async Task ExportCsvAsync()
    {
        var sfd = new SaveFileDialog
        {
            Filter = "CSV File (*.csv)|*.csv",
            FileName = $"NetPulse_Measurements_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv"
        };
        if (sfd.ShowDialog() == true)
        {
            var history = _engine.GetAllHistory();
            await _storageService.ExportToCsvAsync(sfd.FileName, history);
            MessageBox.Show($"Exported {history.Count} measurements to CSV successfully!", "NetPulse Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task ExportJsonAsync()
    {
        var sfd = new SaveFileDialog
        {
            Filter = "JSON File (*.json)|*.json",
            FileName = $"NetPulse_Snapshot_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json"
        };
        if (sfd.ShowDialog() == true)
        {
            var snapshot = new
            {
                Timestamp = DateTime.UtcNow,
                ActiveInterface = ActiveNic,
                Diagnosis = _engine.CurrentDiagnosis,
                Metrics = _engine.GetCurrentMetrics(),
                Events = Events.Take(100).ToList(),
                LatestBufferbloat = LatestBufferbloatResult,
                LatestSpeedTest = LatestSpeedTestResult
            };
            await _storageService.ExportToJsonAsync(sfd.FileName, snapshot);
            MessageBox.Show("Exported diagnostic snapshot to JSON successfully!", "NetPulse Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task ExportReportAsync()
    {
        var sfd = new SaveFileDialog
        {
            Filter = "Markdown Report (*.md)|*.md|Text Report (*.txt)|*.txt",
            FileName = $"NetPulse_DiagnosticReport_{DateTime.UtcNow:yyyyMMdd_HHmmss}.md"
        };
        if (sfd.ShowDialog() == true)
        {
            var routePath = new RoutePath
            {
                Destination = MtrDestination,
                Hops = MtrHops.ToList(),
                TotalLatencyMs = MtrHops.LastOrDefault(h => h.IsResponding)?.CurrentRttMs ?? 0
            };

            string report = await _storageService.GenerateDiagnosticReportAsync(
                ActiveNic,
                _engine.CurrentDiagnosis,
                _engine.GetCurrentMetrics(),
                Events.ToList(),
                routePath,
                LatestBufferbloatResult,
                LatestSpeedTestResult);

            await File.WriteAllTextAsync(sfd.FileName, report);
            MessageBox.Show("Exported comprehensive Diagnostic Report successfully!", "NetPulse Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public void OnAppExiting()
    {
        _mtrService.Stop();
        _engine.Stop();
    }
}

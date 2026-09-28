using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetPulse.Core.Models;
using NetPulse.Core.Statistics;

namespace NetPulse.Core.Services;

public class MeasurementEngine
{
    private readonly NetworkDiscoveryService _discoveryService;
    private readonly PingService _pingService;
    private readonly TcpProbeService _tcpService;
    private readonly HttpProbeService _httpService;
    private readonly DnsProbeService _dnsService;
    private readonly DiagnosticEngine _diagnosticEngine;
    private readonly EventLogService _eventLogService;

    private readonly ConcurrentDictionary<string, TargetMetrics> _metrics = new();
    private readonly ConcurrentDictionary<string, double> _previousLatencies = new();
    private readonly List<Measurement> _allHistory = new();
    private readonly object _historyLock = new();

    private CancellationTokenSource? _cts;
    private Task? _monitoringTask;

    public AppSettings Settings { get; set; } = AppSettings.CreateDefault();
    public NetworkInterfaceInfo CurrentNic { get; private set; } = new();
    public DiagnosticResult CurrentDiagnosis { get; private set; } = new();
    public bool IsRunning { get; private set; }

    public event EventHandler<IReadOnlyList<TargetMetrics>>? MetricsUpdated;
    public event EventHandler<DiagnosticResult>? DiagnosisUpdated;
    public event EventHandler<NetworkInterfaceInfo>? NetworkInterfaceUpdated;

    public MeasurementEngine(
        NetworkDiscoveryService discoveryService,
        PingService pingService,
        TcpProbeService tcpService,
        HttpProbeService httpService,
        DnsProbeService dnsService,
        DiagnosticEngine diagnosticEngine,
        EventLogService eventLogService)
    {
        _discoveryService = discoveryService;
        _pingService = pingService;
        _tcpService = tcpService;
        _httpService = httpService;
        _dnsService = dnsService;
        _diagnosticEngine = diagnosticEngine;
        _eventLogService = eventLogService;

        _discoveryService.NetworkChanged += (s, nic) =>
        {
            CurrentNic = nic;
            UpdateGatewayTarget(nic);
            _eventLogService.LogEvent(
                "InterfaceChanged",
                "Info",
                $"Network interface changed: {nic.Name} ({nic.InterfaceType}) IP: {nic.Ipv4Address}");
            NetworkInterfaceUpdated?.Invoke(this, nic);
        };
    }

    public void Start()
    {
        if (IsRunning) return;

        IsRunning = true;
        _cts = new CancellationTokenSource();

        // Initial discovery
        CurrentNic = _discoveryService.GetActiveInterfaceInfo();
        UpdateGatewayTarget(CurrentNic);
        NetworkInterfaceUpdated?.Invoke(this, CurrentNic);

        _monitoringTask = Task.Run(() => MonitoringLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        if (!IsRunning) return;

        IsRunning = false;
        _cts?.Cancel();
        try
        {
            _monitoringTask?.Wait(2000);
        }
        catch { }
        _cts?.Dispose();
        _cts = null;
    }

    private void UpdateGatewayTarget(NetworkInterfaceInfo nic)
    {
        var gwTarget = Settings.Targets.FirstOrDefault(t => t.IsGateway);
        if (gwTarget != null && !string.IsNullOrEmpty(nic.Ipv4Gateway))
        {
            gwTarget.Host = nic.Ipv4Gateway;
            gwTarget.Name = $"Gateway ({nic.Ipv4Gateway})";
        }
    }

    private async Task MonitoringLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var cycleStart = DateTime.UtcNow;

                // 1. Probe all enabled targets asynchronously
                var enabledTargets = Settings.Targets.Where(t => t.IsEnabled).ToList();
                var probeTasks = enabledTargets.Select(t => ProbeSingleTargetAsync(t, ct)).ToList();

                var measurements = await Task.WhenAll(probeTasks);

                // 2. Process measurements and update stats
                foreach (var m in measurements)
                {
                    ProcessMeasurement(m);
                }

                // 3. Run diagnostic engine
                var gwMetrics = _metrics.Values.FirstOrDefault(m => m.IsGateway);
                var dnsMetrics = _metrics.Values.FirstOrDefault(m => m.IsDnsResolver);
                var internetMetrics = _metrics.Values.Where(m => !m.IsGateway && !m.IsDnsResolver).ToList();

                var diagnosis = _diagnosticEngine.Analyze(
                    CurrentNic,
                    gwMetrics,
                    internetMetrics,
                    dnsMetrics);

                CurrentDiagnosis = diagnosis;

                // 4. Notify UI
                MetricsUpdated?.Invoke(this, _metrics.Values.ToList());
                DiagnosisUpdated?.Invoke(this, diagnosis);

                // 5. Sleep for remaining interval
                int elapsedMs = (int)(DateTime.UtcNow - cycleStart).TotalMilliseconds;
                int delayMs = Math.Max(100, Settings.MonitoringIntervalMs - elapsedMs);
                await Task.Delay(delayMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _eventLogService.LogEvent("EngineError", "Warning", $"Monitoring error: {ex.Message}");
                await Task.Delay(1000, ct);
            }
        }
    }

    private async Task<Measurement> ProbeSingleTargetAsync(TargetConfig target, CancellationToken ct)
    {
        if (target.IsGateway && (target.Host == "auto" || string.IsNullOrEmpty(target.Host)))
        {
            if (!string.IsNullOrEmpty(CurrentNic.Ipv4Gateway))
            {
                target.Host = CurrentNic.Ipv4Gateway;
            }
            else
            {
                return new Measurement
                {
                    TargetId = target.Id,
                    TargetName = target.Name,
                    TargetHost = target.Host,
                    Protocol = target.Protocol,
                    Success = false,
                    ErrorMessage = "No default gateway found"
                };
            }
        }

        return target.Protocol switch
        {
            NetworkProtocol.Icmp => await _pingService.ProbeAsync(target, 1500, ct),
            NetworkProtocol.Tcp => await _tcpService.ProbeAsync(target, 2000, ct),
            NetworkProtocol.Https => await _httpService.ProbeAsync(target, 3500, ct),
            NetworkProtocol.Dns => await _dnsService.ProbeAsync(target, 2000, ct),
            _ => await _pingService.ProbeAsync(target, 1500, ct)
        };
    }

    private void ProcessMeasurement(Measurement m)
    {
        var metrics = _metrics.GetOrAdd(m.TargetId, id => new TargetMetrics
        {
            TargetId = id,
            TargetName = m.TargetName,
            TargetHost = m.TargetHost,
            Protocol = m.Protocol,
            IsGateway = Settings.Targets.FirstOrDefault(t => t.Id == id)?.IsGateway ?? false,
            IsDnsResolver = Settings.Targets.FirstOrDefault(t => t.Id == id)?.IsDnsResolver ?? false
        });

        lock (metrics)
        {
            metrics.TotalProbes++;
            metrics.LastUpdate = m.Timestamp;

            if (m.Success)
            {
                metrics.CurrentLatency = m.RttMs;
                metrics.MinLatency = Math.Min(metrics.MinLatency, m.RttMs);
                metrics.MaxLatency = Math.Max(metrics.MaxLatency, m.RttMs);
                metrics.IsReachable = true;
                metrics.LastError = null;
                metrics.LastHttpsBreakdown = m.HttpsBreakdown;

                if (_previousLatencies.TryGetValue(m.TargetId, out double prevLatency))
                {
                    metrics.Jitter = StatisticalCalculator.UpdateRfc3550Jitter(metrics.Jitter, m.RttMs, prevLatency);

                    if (m.RttMs - prevLatency > 75.0 && Settings.NotifyOnLatencySpike)
                    {
                        _eventLogService.LogEvent(
                            "LatencySpike",
                            "Warning",
                            $"Latency to {m.TargetName} spiked from {prevLatency:F1} ms to {m.RttMs:F1} ms (+{m.RttMs - prevLatency:F1} ms)",
                            m.TargetName,
                            m.RttMs);
                    }
                }
                _previousLatencies[m.TargetId] = m.RttMs;

                metrics.RecentHistory.Add(m);
                int maxSamples = Math.Max(60, (Settings.HistoryWindowMinutes * 60 * 1000) / Settings.MonitoringIntervalMs);
                while (metrics.RecentHistory.Count > maxSamples)
                {
                    metrics.RecentHistory.RemoveAt(0);
                }

                var rtts = metrics.RecentHistory.Where(x => x.Success).Select(x => x.RttMs).ToList();
                if (rtts.Count > 0)
                {
                    metrics.RollingAvg = StatisticalCalculator.CalculateMean(rtts);
                    metrics.MedianLatency = StatisticalCalculator.CalculateMedian(rtts);
                    metrics.P95Latency = StatisticalCalculator.CalculatePercentile(rtts, 0.95);
                }
            }
            else
            {
                metrics.FailedProbes++;
                metrics.LastError = m.ErrorMessage;

                if (metrics.RecentHistory.Count > 0 && metrics.RecentHistory.TakeLast(3).All(x => !x.Success))
                {
                    metrics.IsReachable = false;
                }

                metrics.RecentHistory.Add(m);
                if (metrics.RecentHistory.Count > 60)
                {
                    metrics.RecentHistory.RemoveAt(0);
                }

                if (metrics.IsGateway)
                {
                    _eventLogService.LogEvent(
                        "GatewayDegraded",
                        "Critical",
                        $"Gateway probe failed: {m.ErrorMessage}",
                        m.TargetName);
                }
                else if (metrics.IsDnsResolver)
                {
                    _eventLogService.LogEvent(
                        "DnsFailure",
                        "Warning",
                        $"DNS resolution failed: {m.ErrorMessage}",
                        m.TargetName);
                }
            }

            metrics.PacketLossPercent = StatisticalCalculator.CalculatePacketLoss(metrics.FailedProbes, metrics.TotalProbes);
        }

        lock (_historyLock)
        {
            _allHistory.Add(m);
            if (_allHistory.Count > 5000)
            {
                _allHistory.RemoveAt(0);
            }
        }
    }

    public IReadOnlyList<Measurement> GetAllHistory()
    {
        lock (_historyLock)
        {
            return _allHistory.ToList();
        }
    }

    public IReadOnlyList<TargetMetrics> GetCurrentMetrics()
    {
        return _metrics.Values.ToList();
    }
}

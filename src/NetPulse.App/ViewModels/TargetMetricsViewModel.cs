using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using NetPulse.Core.Models;

namespace NetPulse.App.ViewModels;

public class TargetMetricsViewModel : ViewModelBase
{
    private TargetMetrics _model;
    private List<double> _chartValues = new();

    public TargetMetricsViewModel(TargetMetrics model)
    {
        _model = model;
        UpdateFromModel(model);
    }

    public string TargetId => _model.TargetId;
    public string TargetName => _model.TargetName;
    public string TargetHost => _model.TargetHost;
    public NetworkProtocol Protocol => _model.Protocol;
    public bool IsGateway => _model.IsGateway;
    public bool IsDnsResolver => _model.IsDnsResolver;

    public double CurrentLatency => _model.CurrentLatency;
    public double RollingAvg => _model.RollingAvg;
    public double MinLatency => _model.MinLatency == double.MaxValue ? 0 : _model.MinLatency;
    public double MaxLatency => _model.MaxLatency;
    public double MedianLatency => _model.MedianLatency;
    public double P95Latency => _model.P95Latency;
    public double Jitter => _model.Jitter;
    public double PacketLossPercent => _model.PacketLossPercent;
    public int TotalProbes => _model.TotalProbes;
    public bool IsReachable => _model.IsReachable;
    public string? LastError => _model.LastError;
    public HttpsTimingBreakdown? HttpsBreakdown => _model.LastHttpsBreakdown;

    public string LatencyDisplay => _model.IsReachable && _model.TotalProbes > 0
        ? $"{_model.CurrentLatency:F1} ms"
        : "Failed";

    public string AvgDisplay => $"{RollingAvg:F1} ms";
    public string JitterDisplay => $"{Jitter:F1} ms";
    public string LossDisplay => $"{PacketLossPercent:F1}%";

    public Brush StatusBrush => !_model.IsReachable || PacketLossPercent > 10.0
        ? new SolidColorBrush(Color.FromRgb(239, 68, 68)) // Red
        : (PacketLossPercent > 2.0 || CurrentLatency > 120.0
            ? new SolidColorBrush(Color.FromRgb(245, 158, 11)) // Amber
            : new SolidColorBrush(Color.FromRgb(16, 185, 129))); // Green

    public List<double> ChartValues
    {
        get => _chartValues;
        private set => SetProperty(ref _chartValues, value);
    }

    public void UpdateFromModel(TargetMetrics model)
    {
        _model = model;
        lock (model)
        {
            ChartValues = model.RecentHistory
                .Where(m => m.Success)
                .Select(m => m.RttMs)
                .TakeLast(40)
                .ToList();
        }

        OnPropertyChanged(nameof(CurrentLatency));
        OnPropertyChanged(nameof(RollingAvg));
        OnPropertyChanged(nameof(MinLatency));
        OnPropertyChanged(nameof(MaxLatency));
        OnPropertyChanged(nameof(MedianLatency));
        OnPropertyChanged(nameof(P95Latency));
        OnPropertyChanged(nameof(Jitter));
        OnPropertyChanged(nameof(PacketLossPercent));
        OnPropertyChanged(nameof(TotalProbes));
        OnPropertyChanged(nameof(IsReachable));
        OnPropertyChanged(nameof(LastError));
        OnPropertyChanged(nameof(HttpsBreakdown));
        OnPropertyChanged(nameof(LatencyDisplay));
        OnPropertyChanged(nameof(AvgDisplay));
        OnPropertyChanged(nameof(JitterDisplay));
        OnPropertyChanged(nameof(LossDisplay));
        OnPropertyChanged(nameof(StatusBrush));
    }
}

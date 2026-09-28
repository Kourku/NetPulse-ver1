using System.Collections.Generic;

namespace NetPulse.Core.Models;

public class AppSettings
{
    public int MonitoringIntervalMs { get; set; } = 1000;
    public int HistoryWindowMinutes { get; set; } = 15;
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = false;
    public bool DarkTheme { get; set; } = true;

    // Notifications
    public bool NotifyOnDisconnect { get; set; } = true;
    public bool NotifyOnPacketLoss { get; set; } = true;
    public double PacketLossNotifyThreshold { get; set; } = 5.0; // percent
    public bool NotifyOnLatencySpike { get; set; } = true;
    public double LatencySpikeNotifyThreshold { get; set; } = 100.0; // ms
    public bool NotifyOnRouteChange { get; set; } = true;

    // UI state
    public bool IsBeginnerMode { get; set; } = true;

    // Configured targets
    public List<TargetConfig> Targets { get; set; } = new();

    public static AppSettings CreateDefault()
    {
        return new AppSettings
        {
            MonitoringIntervalMs = 1000,
            HistoryWindowMinutes = 15,
            StartWithWindows = false,
            MinimizeToTray = true,
            CloseToTray = false,
            DarkTheme = true,
            NotifyOnDisconnect = true,
            NotifyOnPacketLoss = true,
            PacketLossNotifyThreshold = 5.0,
            NotifyOnLatencySpike = true,
            LatencySpikeNotifyThreshold = 100.0,
            NotifyOnRouteChange = true,
            IsBeginnerMode = true,
            Targets = new List<TargetConfig>
            {
                new TargetConfig
                {
                    Id = "gateway",
                    Name = "Local Gateway / Router",
                    Host = "auto",
                    Protocol = NetworkProtocol.Icmp,
                    IsEnabled = true,
                    IsGateway = true,
                    Description = "Primary default router hop connecting your PC to the Internet"
                },
                new TargetConfig
                {
                    Id = "cf-dns",
                    Name = "Cloudflare (1.1.1.1)",
                    Host = "1.1.1.1",
                    Protocol = NetworkProtocol.Icmp,
                    IsEnabled = true,
                    Description = "Global ultra-low latency anycast network"
                },
                new TargetConfig
                {
                    Id = "google-dns",
                    Name = "Google (8.8.8.8)",
                    Host = "8.8.8.8",
                    Protocol = NetworkProtocol.Icmp,
                    IsEnabled = true,
                    Description = "Global Tier-1 Google backbone resolver network"
                },
                new TargetConfig
                {
                    Id = "quad9-dns",
                    Name = "Quad9 (9.9.9.9)",
                    Host = "9.9.9.9",
                    Protocol = NetworkProtocol.Icmp,
                    IsEnabled = true,
                    Description = "Independent secure Swiss/global recursive resolver"
                },
                new TargetConfig
                {
                    Id = "dns-system",
                    Name = "DNS Resolution (System)",
                    Host = "cloudflare.com",
                    Protocol = NetworkProtocol.Dns,
                    IsEnabled = true,
                    IsDnsResolver = true,
                    Description = "Measures system DNS resolver latency resolving real domains"
                },
                new TargetConfig
                {
                    Id = "https-edge",
                    Name = "HTTPS Handshake (Cloudflare)",
                    Host = "cloudflare.com",
                    Port = 443,
                    Protocol = NetworkProtocol.Https,
                    IsEnabled = true,
                    Description = "Measures real-world TCP connect, TLS negotiation, and TTFB response latency"
                }
            }
        };
    }
}

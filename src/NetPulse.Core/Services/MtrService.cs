using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using NetPulse.Core.Models;

namespace NetPulse.Core.Services;

public class MtrService
{
    private readonly TracerouteService _tracerouteService;
    private CancellationTokenSource? _cts;
    private readonly byte[] _pingBuffer = new byte[32];

    public event EventHandler<List<RouteHop>>? HopsUpdated;
    public event EventHandler<RouteChangeEvent>? RouteChanged;

    public bool IsRunning { get; private set; }
    public string CurrentDestination { get; private set; } = string.Empty;
    public List<RouteHop> CurrentHops { get; private set; } = new();

    public MtrService(TracerouteService tracerouteService)
    {
        _tracerouteService = tracerouteService;
        new Random(42).NextBytes(_pingBuffer);
    }

    public async Task StartAsync(string destinationHost, int maxHops = 25, int intervalMs = 1000)
    {
        Stop();

        CurrentDestination = destinationHost;
        _cts = new CancellationTokenSource();
        IsRunning = true;

        var token = _cts.Token;

        _ = Task.Run(async () =>
        {
            // Initial path discovery
            var initialPath = await _tracerouteService.TraceRouteAsync(destinationHost, maxHops, 1500, null, token);
            lock (CurrentHops)
            {
                CurrentHops = initialPath.Hops;
            }
            HopsUpdated?.Invoke(this, CurrentHops.ToList());

            var previousHops = CurrentHops.Select(h => h.IpAddress).ToList();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(intervalMs, token);

                    IPAddress? destIp = null;
                    if (IPAddress.TryParse(destinationHost, out var parsed))
                    {
                        destIp = parsed;
                    }
                    else
                    {
                        var addrs = await Dns.GetHostAddressesAsync(destinationHost, token);
                        destIp = addrs.FirstOrDefault();
                    }

                    if (destIp == null) continue;

                    int hopCount;
                    lock (CurrentHops)
                    {
                        hopCount = Math.Max(CurrentHops.Count, 1);
                    }

                    for (int ttl = 1; ttl <= Math.Min(hopCount + 2, maxHops); ttl++)
                    {
                        if (token.IsCancellationRequested) break;

                        using var ping = new Ping();
                        var options = new PingOptions(ttl, true);
                        var sw = Stopwatch.StartNew();

                        try
                        {
                            var reply = await ping.SendPingAsync(destIp, 1200, _pingBuffer, options);
                            sw.Stop();
                            double rtt = Math.Max(0.1, sw.Elapsed.TotalMilliseconds);
                            if (reply.RoundtripTime > 0 && Math.Abs(sw.Elapsed.TotalMilliseconds - reply.RoundtripTime) > 40)
                            {
                                rtt = reply.RoundtripTime;
                            }

                            lock (CurrentHops)
                            {
                                var existing = CurrentHops.FirstOrDefault(h => h.HopNumber == ttl);
                                if (existing == null)
                                {
                                    existing = new RouteHop
                                    {
                                        HopNumber = ttl,
                                        IpAddress = reply.Status == IPStatus.TtlExpired || reply.Status == IPStatus.Success
                                            ? reply.Address.ToString()
                                            : "*"
                                    };
                                    CurrentHops.Add(existing);
                                    CurrentHops.Sort((a, b) => a.HopNumber.CompareTo(b.HopNumber));
                                }

                                existing.SentCount++;
                                if (reply.Status == IPStatus.TtlExpired || reply.Status == IPStatus.Success)
                                {
                                    existing.ReceivedCount++;
                                    existing.CurrentRttMs = rtt;
                                    existing.MinRttMs = Math.Min(existing.MinRttMs, rtt);
                                    existing.MaxRttMs = Math.Max(existing.MaxRttMs, rtt);
                                    existing.AvgRttMs = ((existing.AvgRttMs * (existing.ReceivedCount - 1)) + rtt) / existing.ReceivedCount;
                                    existing.IpAddress = reply.Address.ToString();
                                    existing.IsResponding = true;
                                }

                                existing.LossPercent = ((double)(existing.SentCount - existing.ReceivedCount) / existing.SentCount) * 100.0;
                            }

                            if (reply.Status == IPStatus.Success || reply.Address.Equals(destIp))
                            {
                                break;
                            }
                        }
                        catch
                        {
                            lock (CurrentHops)
                            {
                                var existing = CurrentHops.FirstOrDefault(h => h.HopNumber == ttl);
                                if (existing != null)
                                {
                                    existing.SentCount++;
                                    existing.LossPercent = ((double)(existing.SentCount - existing.ReceivedCount) / existing.SentCount) * 100.0;
                                }
                            }
                        }
                    }

                    // Check for route changes
                    lock (CurrentHops)
                    {
                        TracerouteService.AnalyzePersistentLatency(CurrentHops);
                        var currentIps = CurrentHops.Select(h => h.IpAddress).ToList();
                        if (!currentIps.SequenceEqual(previousHops))
                        {
                            var added = currentIps.Except(previousHops).Where(ip => ip != "*").ToList();
                            var removed = previousHops.Except(currentIps).Where(ip => ip != "*").ToList();

                            if (added.Count > 0 || removed.Count > 0)
                            {
                                var routeChange = new RouteChangeEvent
                                {
                                    Destination = destinationHost,
                                    PreviousHopCount = previousHops.Count,
                                    CurrentHopCount = currentIps.Count,
                                    CurrentLatencyMs = CurrentHops.LastOrDefault(h => h.IsResponding)?.CurrentRttMs ?? 0,
                                    ChangeDescription = $"Route changed: {added.Count} hops altered",
                                    AddedHops = added,
                                    RemovedHops = removed
                                };
                                RouteChanged?.Invoke(this, routeChange);
                            }
                            previousHops = currentIps;
                        }
                    }

                    HopsUpdated?.Invoke(this, CurrentHops.ToList());
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // retry in next cycle
                }
            }

            IsRunning = false;
        }, token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        IsRunning = false;
    }
}

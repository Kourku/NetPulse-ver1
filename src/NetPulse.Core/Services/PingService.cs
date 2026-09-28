using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using NetPulse.Core.Models;

namespace NetPulse.Core.Services;

public class PingService
{
    private static readonly byte[] Buffer = new byte[32];

    static PingService()
    {
        new Random(42).NextBytes(Buffer);
    }

    public async Task<Measurement> ProbeAsync(TargetConfig target, int timeoutMs = 2000, CancellationToken ct = default)
    {
        var measurement = new Measurement
        {
            Timestamp = DateTime.UtcNow,
            TargetId = target.Id,
            TargetName = target.Name,
            TargetHost = target.Host,
            Protocol = NetworkProtocol.Icmp
        };

        if (string.IsNullOrWhiteSpace(target.Host) || target.Host.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            measurement.Success = false;
            measurement.ErrorMessage = "Host not resolved";
            return measurement;
        }

        using var ping = new Ping();
        var sw = Stopwatch.StartNew();

        try
        {
            var options = new PingOptions(64, true);
            var replyTask = ping.SendPingAsync(target.Host, timeoutMs, Buffer, options);
            
            var completed = await Task.WhenAny(replyTask, Task.Delay(timeoutMs + 100, ct));
            sw.Stop();

            if (completed == replyTask)
            {
                var reply = await replyTask;
                if (reply.Status == IPStatus.Success)
                {
                    measurement.Success = true;
                    measurement.RttMs = Math.Max(0.1, sw.Elapsed.TotalMilliseconds);
                    if (reply.RoundtripTime > 0 && Math.Abs(sw.Elapsed.TotalMilliseconds - reply.RoundtripTime) > 50)
                    {
                        measurement.RttMs = reply.RoundtripTime;
                    }
                    measurement.ResolvedIp = reply.Address?.ToString();
                }
                else
                {
                    measurement.Success = false;
                    measurement.ErrorMessage = reply.Status.ToString();
                }
            }
            else
            {
                measurement.Success = false;
                measurement.ErrorMessage = "Timeout";
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            measurement.Success = false;
            measurement.ErrorMessage = ex.GetBaseException().Message;
        }

        return measurement;
    }
}

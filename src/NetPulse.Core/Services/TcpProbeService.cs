using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NetPulse.Core.Models;

namespace NetPulse.Core.Services;

public class TcpProbeService
{
    public async Task<Measurement> ProbeAsync(TargetConfig target, int timeoutMs = 2500, CancellationToken ct = default)
    {
        var measurement = new Measurement
        {
            Timestamp = DateTime.UtcNow,
            TargetId = target.Id,
            TargetName = target.Name,
            TargetHost = target.Host,
            Protocol = NetworkProtocol.Tcp
        };

        int port = target.Port > 0 ? target.Port : 443;
        using var socket = new Socket(SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        socket.NoDelay = true;

        var sw = Stopwatch.StartNew();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(target.Host, ct);
            if (addresses.Length == 0)
            {
                measurement.Success = false;
                measurement.ErrorMessage = "DNS resolution returned no addresses";
                return measurement;
            }

            var endpoint = new IPEndPoint(addresses[0], port);
            measurement.ResolvedIp = addresses[0].ToString();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            sw.Restart();
            await socket.ConnectAsync(endpoint, cts.Token);
            sw.Stop();

            measurement.Success = true;
            measurement.RttMs = Math.Max(0.1, sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            measurement.Success = false;
            measurement.ErrorMessage = "Connection timed out";
        }
        catch (Exception ex)
        {
            sw.Stop();
            measurement.Success = false;
            measurement.ErrorMessage = ex.GetBaseException().Message;
        }
        finally
        {
            try
            {
                if (socket.Connected) socket.Shutdown(SocketShutdown.Both);
            }
            catch { }
        }

        return measurement;
    }
}

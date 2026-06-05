using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace InfraWatch.Collectors.Imaging;

internal sealed record TftpResult(bool Ok, long Bytes, double LatencyMs, string Message);

/// <summary>
/// Minimal read-only TFTP client (RFC 1350, classic 512-byte blocks) — enough to confirm a
/// WDS/PXE server actually serves a boot file. Downloads up to a cap, then stops.
/// </summary>
internal static class TftpClient
{
    public static TftpResult Read(string server, string file, int maxBytes, int timeoutMs)
    {
        IPAddress ip;
        try
        {
            ip = Array.Find(Dns.GetHostAddresses(server), a => a.AddressFamily == AddressFamily.InterNetwork)
                 ?? throw new InvalidOperationException("no IPv4 address");
        }
        catch (Exception ex)
        {
            return new(false, 0, 0, $"resolve failed: {ex.Message}");
        }

        using var udp = new UdpClient();
        udp.Client.ReceiveTimeout = timeoutMs;

        var sw = Stopwatch.StartNew();
        var rrq = BuildRrq(file);
        udp.Send(rrq, rrq.Length, new IPEndPoint(ip, 69));

        long total = 0;
        ushort expected = 1;
        IPEndPoint? transfer = null; // server picks a new port for the transfer

        while (true)
        {
            byte[] data;
            IPEndPoint? from = null;
            try
            {
                data = udp.Receive(ref from);
            }
            catch (SocketException)
            {
                sw.Stop();
                return new(false, total, Round(sw), total > 0 ? "timed out mid-transfer" : "no response (timed out)");
            }

            if (data.Length < 4) continue;
            var opcode = (data[0] << 8) | data[1];

            if (opcode == 5) // ERROR
            {
                sw.Stop();
                var msg = data.Length > 5 ? Encoding.ASCII.GetString(data, 4, data.Length - 5) : "error";
                return new(false, total, Round(sw), $"TFTP error: {msg}");
            }
            if (opcode != 3) continue; // want DATA

            transfer ??= from; // lock onto the server's transfer port (TID)
            var block = (ushort)((data[2] << 8) | data[3]);
            var dataLen = data.Length - 4;

            if (block == expected)
            {
                total += dataLen;
                expected++;
            }
            // ACK the block we received (even if duplicate) to the transfer port
            var ack = new byte[] { 0, 4, data[2], data[3] };
            udp.Send(ack, ack.Length, transfer);

            if (dataLen < 512)
            {
                sw.Stop();
                return new(true, total, Round(sw), $"downloaded {total} bytes (complete)");
            }
            if (total >= maxBytes)
            {
                sw.Stop();
                return new(true, total, Round(sw), $"downloaded {total} bytes (capped at {maxBytes})");
            }
        }
    }

    private static double Round(Stopwatch sw) => Math.Round(sw.Elapsed.TotalMilliseconds, 1);

    private static byte[] BuildRrq(string file)
    {
        // opcode 1 (RRQ) | filename \0 | "octet" \0
        var name = Encoding.ASCII.GetBytes(file);
        var mode = Encoding.ASCII.GetBytes("octet");
        var p = new byte[2 + name.Length + 1 + mode.Length + 1];
        p[0] = 0; p[1] = 1;
        Array.Copy(name, 0, p, 2, name.Length);
        p[2 + name.Length] = 0;
        Array.Copy(mode, 0, p, 3 + name.Length, mode.Length);
        return p;
    }
}

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace InfraWatch.Collectors.Dhcp;

internal sealed record DhcpProbeResult(
    bool OfferReceived,
    string? OfferedAddress,
    string? ServerId,
    double LatencyMs,
    string Message,
    bool LeaseAcquired,
    bool LeaseReleased);

/// <summary>
/// Active DHCP test: sends a DHCP DISCOVER (acting as a relay via giaddr so the server
/// unicasts the reply back) and waits for an OFFER. Optionally completes a full lease
/// (REQUEST → ACK) and then RELEASEs it, so no address is left consumed.
///
/// Intrusive by nature — opt-in only. Uses a fixed locally-administered probe MAC so the
/// server hands out the same address each time (minimal pool impact).
/// </summary>
internal static class DhcpProbe
{
    // Locally-administered MAC ("02:49:57" = IW) reserved for the probe.
    private static readonly byte[] ProbeMac = [0x02, 0x49, 0x57, 0x00, 0x00, 0x01];

    public static DhcpProbeResult Run(IPAddress server, IPAddress relay, int timeoutMs, bool fullLease)
    {
        var xid = (uint)Random.Shared.Next();

        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(relay, 67)); // receive the relayed reply on giaddr:67

        var sw = Stopwatch.StartNew();
        var discover = Build(xid, relay, ciaddr: null, dhcpType: 1, requestedIp: null, serverId: null);
        udp.Send(discover, discover.Length, new IPEndPoint(server, 67));

        var offer = Receive(udp, xid, expectType: 2, timeoutMs);
        sw.Stop();
        if (offer is null)
            return new(false, null, null, Round(sw), "no OFFER (timed out)", false, false);

        var offered = new IPAddress(offer.Yiaddr);
        var serverId = offer.ServerId is null ? null : new IPAddress(offer.ServerId).ToString();

        if (!fullLease)
            return new(true, offered.ToString(), serverId, Round(sw), $"OFFER {offered}", false, false);

        // Full lease: REQUEST the offered address, expect ACK, then RELEASE it.
        var request = Build(xid, relay, ciaddr: null, dhcpType: 3, requestedIp: offer.Yiaddr, serverId: offer.ServerId);
        udp.Send(request, request.Length, new IPEndPoint(server, 67));
        var ack = Receive(udp, xid, expectType: 5, timeoutMs);
        if (ack is null)
            return new(true, offered.ToString(), serverId, Round(sw), $"OFFER {offered}, no ACK", false, false);

        var released = false;
        if (offer.ServerId is not null)
        {
            var release = Build((uint)Random.Shared.Next(), relay, ciaddr: ack.Yiaddr, dhcpType: 7,
                requestedIp: null, serverId: offer.ServerId);
            udp.Send(release, release.Length, new IPEndPoint(server, 67));
            released = true;
        }
        return new(true, offered.ToString(), serverId, Round(sw),
            $"leased {new IPAddress(ack.Yiaddr)}{(released ? ", released" : "")}", true, released);
    }

    private static double Round(Stopwatch sw) => Math.Round(sw.Elapsed.TotalMilliseconds, 1);

    private sealed record Reply(byte[] Yiaddr, byte[]? ServerId);

    private static Reply? Receive(UdpClient udp, uint xid, byte expectType, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            var remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remaining <= 0) return null;
            udp.Client.ReceiveTimeout = remaining;

            byte[] data;
            try
            {
                IPEndPoint? ep = null;
                data = udp.Receive(ref ep);
            }
            catch (SocketException) { return null; } // timeout

            if (data.Length < 240 || data[0] != 2) continue;          // not a BOOTREPLY
            if (BitConverter.ToUInt32(data, 4) != xid) continue;       // not our transaction

            var (type, serverId) = ParseOptions(data);
            if (type != expectType) continue;

            var yiaddr = new byte[4];
            Array.Copy(data, 16, yiaddr, 0, 4);
            return new Reply(yiaddr, serverId);
        }
    }

    private static (byte Type, byte[]? ServerId) ParseOptions(byte[] data)
    {
        byte type = 0;
        byte[]? serverId = null;
        var i = 240; // after the magic cookie
        while (i < data.Length)
        {
            var code = data[i++];
            if (code == 255) break; // end
            if (code == 0) continue; // pad
            if (i >= data.Length) break;
            var len = data[i++];
            if (i + len > data.Length) break;
            switch (code)
            {
                case 53: type = data[i]; break;                         // DHCP message type
                case 54: serverId = data[i..(i + len)]; break;          // server identifier
            }
            i += len;
        }
        return (type, serverId);
    }

    private static byte[] Build(uint xid, IPAddress giaddr, byte[]? ciaddr, byte dhcpType, byte[]? requestedIp, byte[]? serverId)
    {
        var p = new byte[300]; // BOOTP minimum
        p[0] = 1;  // op = BOOTREQUEST
        p[1] = 1;  // htype = Ethernet
        p[2] = 6;  // hlen
        p[3] = 0;  // hops
        BitConverter.GetBytes(xid).CopyTo(p, 4);
        // secs (8-9), flags (10-11) = 0 -> unicast reply to giaddr
        if (ciaddr is not null) ciaddr.CopyTo(p, 12);                   // ciaddr (for RELEASE)
        giaddr.GetAddressBytes().CopyTo(p, 24);                         // giaddr (relay)
        ProbeMac.CopyTo(p, 28);                                         // chaddr
        // magic cookie
        p[236] = 0x63; p[237] = 0x82; p[238] = 0x53; p[239] = 0x63;

        var o = 240;
        p[o++] = 53; p[o++] = 1; p[o++] = dhcpType;                     // DHCP message type
        p[o++] = 61; p[o++] = 7; p[o++] = 1; ProbeMac.CopyTo(p, o); o += 6; // client id
        if (requestedIp is not null) { p[o++] = 50; p[o++] = 4; requestedIp.CopyTo(p, o); o += 4; }
        if (serverId is not null) { p[o++] = 54; p[o++] = 4; serverId.CopyTo(p, o); o += 4; }
        p[o++] = 255;                                                   // end
        return p;
    }
}

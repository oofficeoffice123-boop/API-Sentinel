using System.Net;
using System.Net.Sockets;
using System.IO;

namespace ApiSentinel.Api.Security;

public static class OutboundTargetPolicy
{
    public static readonly HttpRequestOptionsKey<IPAddress[]> PinnedAddresses = new("ApiSentinel.PinnedAddresses");

    public static async Task<IPAddress[]> ResolveAllowedAsync(Uri uri, CancellationToken cancellationToken, int? testingLaboratoryPort = null)
    {
        if (uri.Scheme is not ("http" or "https")) throw new InvalidOperationException("Only HTTP and HTTPS targets are allowed.");
        var addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
        var allowedLocalLab = testingLaboratoryPort is > 0 && uri.Port == testingLaboratoryPort &&
            (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(uri.Host, out var literal) && IPAddress.IsLoopback(literal)) &&
            addresses.Length > 0 && addresses.All(IPAddress.IsLoopback);
        if (addresses.Length == 0 || addresses.Any(IsBlocked) && !allowedLocalLab) throw new InvalidOperationException("This URL resolves to a restricted network address.");
        return addresses;
    }

    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = context.InitialRequestMessage.Options.TryGetValue(PinnedAddresses, out var pinned)
            ? pinned
            : await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken); return new NetworkStream(socket, ownsSocket: true); }
            catch (Exception e) when (e is SocketException or OperationCanceledException) { socket.Dispose(); lastError = e; if (e is OperationCanceledException) throw; }
        }
        throw new HttpRequestException("Could not connect to the resolved target address.", lastError);
    }

    public static bool IsBlocked(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6Multicast) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b6 = ip.GetAddressBytes();
            var documentation = b6.AsSpan(0, 4).SequenceEqual(new byte[] { 0x20, 0x01, 0x0d, 0xb8 });
            var documentationV6 = b6[0] == 0x3f && b6[1] == 0xff && (b6[2] & 0xf0) == 0;
            var protocolAssignment = b6[0] == 0x20 && b6[1] == 0x01 && b6[2] <= 0x01;
            return (b6[0] & 0xe0) != 0x20 || protocolAssignment || documentation || documentationV6 || b6[0] == 0x20 && b6[1] == 0x02 || b6.AsSpan(0, 12).SequenceEqual(new byte[] { 0x00, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0 });
        }
        if (ip.AddressFamily != AddressFamily.InterNetwork) return true;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || b[0] == 127 || b[0] == 0 || b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] == 100 && b[1] is >= 64 and <= 127 || b[0] == 192 && b[1] == 0 && b[2] == 0 || b[0] == 192 && b[1] == 88 && b[2] == 99 || b[0] == 192 && b[1] == 0 && b[2] == 2 || b[0] == 198 && b[1] is 18 or 19 || b[0] == 198 && b[1] == 51 && b[2] == 100 || b[0] == 203 && b[1] == 0 && b[2] == 113 || b[0] >= 224;
    }
}
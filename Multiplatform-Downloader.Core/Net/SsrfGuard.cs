using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Multiplatform_Downloader.Core.Net;

/// <summary>SSRF 가드에 의해 차단된 URL(사설 IP·loopback·비 http 스킴 등).</summary>
public sealed class SsrfBlockedException : Exception
{
    public SsrfBlockedException(string message) : base(message) { }
}

/// <summary>
/// URL이 내부망(SSRF) 대상을 가리키지 않는지 검증한다(NFR-10).
/// 단축 URL 선해석의 매 리다이렉트 단계·최종 URL에 스킴/호스트를 검사하고,
/// 실제 연결 시점의 해석된 IP는 <see cref="ShortUrlResolver"/>의 ConnectCallback이 <see cref="IsBlockedAddress"/>로 재검증한다
/// (DNS 리바인딩·IP 대체 표기 우회 차단).
/// </summary>
public static class SsrfGuard
{
    /// <summary>스킴/호스트를 검사한다. 안전하지 않으면 <see cref="SsrfBlockedException"/>. (실제 IP 검증은 ConnectCallback이 수행)</summary>
    public static void EnsureSafe(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new SsrfBlockedException($"허용되지 않은 스킴: {uri.Scheme}");
        if (uri.IsFile || uri.IsUnc)
            throw new SsrfBlockedException("로컬/UNC 경로 차단");

        var host = uri.Host;
        if (string.IsNullOrEmpty(host))
            throw new SsrfBlockedException("호스트 없음");
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            throw new SsrfBlockedException("localhost 차단");

        // IP 리터럴 호스트는 여기서 즉시 차단. 도메인 호스트는 ConnectCallback에서
        // 실제 해석 IP를 검사하므로 대체 표기(16진/8진/정수) 우회도 그 지점에서 막힌다.
        if (IPAddress.TryParse(host, out var ip) && IsBlockedAddress(ip))
            throw new SsrfBlockedException($"사설/예약 IP 차단: {ip}");
    }

    /// <summary>사설·loopback·link-local·예약·멀티캐스트 대역인지 판정(IPv4/IPv6, 임베딩 형식 포함).</summary>
    public static bool IsBlockedAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))                       // 127.0.0.0/8, ::1
            return true;
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) // 0.0.0.0, ::
            return true;
        if (ip.IsIPv6Multicast)                             // ff00::/8
            return true;

        // ::ffff:x.x.x.x (IPv4-mapped)
        if (ip.IsIPv4MappedToIPv6)
            return IsBlockedAddress(ip.MapToIPv4());

        var bytes = ip.GetAddressBytes();

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // ::x.x.x.x (deprecated IPv4-compatible) — 상위 96비트 0, 마지막 4바이트를 IPv4로 재검사
            if (IsIPv4CompatibleV6(bytes))
                return IsBlockedAddress(new IPAddress(bytes.AsSpan(12, 4).ToArray()));
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                return true;
            if ((bytes[0] & 0xFE) == 0xFC)                  // ULA fc00::/7
                return true;
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] switch
            {
                0 => true,                                          // 0.0.0.0/8
                10 => true,                                         // 10.0.0.0/8
                100 when bytes[1] is >= 64 and <= 127 => true,      // CGNAT 100.64.0.0/10
                127 => true,                                        // loopback (이중 안전)
                169 when bytes[1] == 254 => true,                   // link-local 169.254.0.0/16
                172 when bytes[1] is >= 16 and <= 31 => true,       // 172.16.0.0/12
                192 when bytes[1] == 0 && bytes[2] == 0 => true,    // 192.0.0.0/24 (IETF)
                192 when bytes[1] == 168 => true,                   // 192.168.0.0/16
                198 when bytes[1] is 18 or 19 => true,              // 198.18.0.0/15 (벤치마킹)
                >= 224 => true,                                     // 멀티캐스트/예약 224.0.0.0/4 이상
                _ => false,
            };
        }

        return false;
    }

    private static bool IsIPv4CompatibleV6(byte[] bytes)
    {
        for (var i = 0; i < 12; i++)
            if (bytes[i] != 0) return false;
        // 마지막 4바이트 중 하나라도 0이 아니면 IPv4-compatible (전부 0이면 unspecified — 위에서 처리)
        return bytes[12] != 0 || bytes[13] != 0 || bytes[14] != 0 || bytes[15] != 0;
    }

    /// <summary>
    /// 실제 연결 시점에 해석된 모든 IP를 <see cref="IsBlockedAddress"/>로 검증하고 통과한 IP로만 연결하는
    /// 핸들러를 만든다(DNS 리바인딩·IP 대체 표기 우회 차단). 외부 URL을 직접 페치하는 모든 곳에서 사용한다.
    /// </summary>
    public static SocketsHttpHandler CreateGuardedHandler(bool allowAutoRedirect = false)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = allowAutoRedirect,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var host = context.DnsEndPoint.Host;
                var port = context.DnsEndPoint.Port;

                var addresses = IPAddress.TryParse(host, out var literal)
                    ? [literal]
                    : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

                var safe = Array.FindAll(addresses, address => !IsBlockedAddress(address));
                if (safe.Length == 0)
                    throw new SsrfBlockedException($"해석된 IP가 모두 차단 대상입니다: {host}");

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(safe, port, cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
    }
}

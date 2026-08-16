using System.Net;
using Multiplatform_Downloader.Core.Net;

namespace Multiplatform_Downloader.Tests.Net;

public class SsrfGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]        // 클라우드 메타데이터 서비스
    [InlineData("100.64.0.1")]             // CGNAT
    [InlineData("192.0.0.1")]              // IETF 프로토콜 할당
    [InlineData("198.18.0.1")]             // 벤치마킹
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]              // 멀티캐스트
    [InlineData("::1")]                    // IPv6 loopback
    [InlineData("::")]                     // unspecified
    [InlineData("fe80::1")]                // link-local
    [InlineData("fc00::1")]                // ULA
    [InlineData("ff02::1")]                // IPv6 멀티캐스트
    [InlineData("::ffff:127.0.0.1")]       // IPv4-mapped
    [InlineData("::ffff:192.168.0.1")]
    [InlineData("::127.0.0.1")]            // IPv4-compatible (레거시)
    [InlineData("::10.0.0.1")]
    public void should_block_when_private_or_reserved(string ip)
    {
        Assert.True(SsrfGuard.IsBlockedAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("142.250.72.14")]                  // 공인 IPv4
    [InlineData("2606:4700:4700::1111")]           // 공인 IPv6 (Cloudflare)
    public void should_allow_when_public_unicast(string ip)
    {
        Assert.False(SsrfGuard.IsBlockedAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://192.168.0.1/")]
    [InlineData("http://localhost/")]
    [InlineData("ftp://example.com/")]
    [InlineData("file:///c:/windows/system32")]
    public void should_throw_when_unsafe_uri(string url)
    {
        Assert.Throws<SsrfBlockedException>(() => SsrfGuard.EnsureSafe(new Uri(url)));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc")]
    [InlineData("http://xhslink.com/a/abc")]
    public void should_pass_when_safe_public_uri(string url)
    {
        // 예외를 던지지 않으면 통과
        SsrfGuard.EnsureSafe(new Uri(url));
    }
}

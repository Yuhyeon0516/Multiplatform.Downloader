using System.Net;
using System.Net.Http;
using Multiplatform_Downloader.Core.Net;
using Multiplatform_Downloader.Core.Platforms;

namespace Multiplatform_Downloader.Tests.Platforms;

public class ShortUrlResolverTests
{
    [Fact]
    public async Task should_resolve_final_url_when_redirect_chain()
    {
        // Arrange
        var handler = new FakeHttpHandler(new()
        {
            ["http://xhslink.com/a/abc"] = (HttpStatusCode.Found, "https://www.xiaohongshu.com/explore/xyz"),
            ["https://www.xiaohongshu.com/explore/xyz"] = (HttpStatusCode.OK, null),
        });
        using var sut = new ShortUrlResolver(handler);

        // Act
        var result = await sut.ResolveAsync("http://xhslink.com/a/abc");

        // Assert
        Assert.Equal("https://www.xiaohongshu.com/explore/xyz", result);
    }

    [Fact]
    public async Task should_keep_url_when_no_redirect()
    {
        // Arrange
        var handler = new FakeHttpHandler(new()
        {
            ["https://www.youtube.com/watch?v=abc"] = (HttpStatusCode.OK, null),
        });
        using var sut = new ShortUrlResolver(handler);

        // Act
        var result = await sut.ResolveAsync("https://www.youtube.com/watch?v=abc");

        // Assert
        Assert.Equal("https://www.youtube.com/watch?v=abc", result);
    }

    [Fact]
    public async Task should_reject_when_redirect_to_private_ip()
    {
        // Arrange — 단축 URL이 사설 IP로 리다이렉트 (SSRF 시도)
        var handler = new FakeHttpHandler(new()
        {
            ["https://short.example/x"] = (HttpStatusCode.Found, "http://192.168.0.1/admin"),
        });
        using var sut = new ShortUrlResolver(handler);

        // Act + Assert
        await Assert.ThrowsAsync<SsrfBlockedException>(
            () => sut.ResolveAsync("https://short.example/x"));
    }

    [Fact]
    public async Task should_reject_when_initial_url_is_loopback()
    {
        using var sut = new ShortUrlResolver(new FakeHttpHandler(new()));

        await Assert.ThrowsAsync<SsrfBlockedException>(
            () => sut.ResolveAsync("http://127.0.0.1:8080/x"));
    }

    [Fact]
    public async Task should_reject_when_https_downgrades_to_http()
    {
        // Arrange — 중간자가 https→http로 다운그레이드 유도
        var handler = new FakeHttpHandler(new()
        {
            ["https://secure.example/x"] = (HttpStatusCode.Found, "http://secure.example/y"),
        });
        using var sut = new ShortUrlResolver(handler);

        // Act + Assert
        await Assert.ThrowsAsync<SsrfBlockedException>(
            () => sut.ResolveAsync("https://secure.example/x"));
    }

    [Fact]
    public async Task should_throw_when_too_many_redirects()
    {
        // Arrange — 무한 리다이렉트 루프
        var handler = new FakeHttpHandler(new()
        {
            ["https://loop.example/a"] = (HttpStatusCode.Found, "https://loop.example/b"),
            ["https://loop.example/b"] = (HttpStatusCode.Found, "https://loop.example/a"),
        });
        using var sut = new ShortUrlResolver(handler, maxRedirects: 3);

        // Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ResolveAsync("https://loop.example/a"));
    }

    [Fact]
    public async Task should_throw_when_url_is_not_absolute()
    {
        using var sut = new ShortUrlResolver(new FakeHttpHandler(new()));

        // "/relative/path"는 Unix에서 절대 file:// URI로 파싱되므로 모든 OS에서 상대인 입력을 쓴다
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.ResolveAsync("relative/path"));
    }

    /// <summary>URL별 (상태코드, Location) 응답을 반환하는 테스트 핸들러. 미등록 URL은 200.</summary>
    private sealed class FakeHttpHandler(Dictionary<string, (HttpStatusCode Code, string? Location)> map)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (!map.TryGetValue(url, out var entry))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request });

            var response = new HttpResponseMessage(entry.Code) { RequestMessage = request };
            if (entry.Location is not null)
                response.Headers.Location = new Uri(entry.Location);
            return Task.FromResult(response);
        }
    }
}

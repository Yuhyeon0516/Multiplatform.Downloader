using System.IO;
using System.Net;
using System.Net.Http;
using Multiplatform_Downloader.Core.Media;

namespace Multiplatform_Downloader.Tests.Media;

public class ThumbnailCacheTests : IDisposable
{
    private readonly string _tempDir;

    public ThumbnailCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mpdl-thumb-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 무시 */ }
    }

    [Fact]
    public async Task should_download_and_cache_thumbnail()
    {
        var handler = new CountingHandler("image-bytes"u8.ToArray());
        using var cache = new ThumbnailCache(_tempDir, handler);

        var path = await cache.GetOrDownloadAsync("https://img.example/t.jpg");

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task should_reuse_cache_when_same_url()
    {
        var handler = new CountingHandler("image-bytes"u8.ToArray());
        using var cache = new ThumbnailCache(_tempDir, handler);

        var first = await cache.GetOrDownloadAsync("https://img.example/t.jpg");
        var second = await cache.GetOrDownloadAsync("https://img.example/t.jpg");

        Assert.Equal(first, second);
        Assert.Equal(1, handler.RequestCount); // 두 번째는 캐시 사용
    }

    [Fact]
    public async Task should_return_null_when_download_fails()
    {
        var handler = new FailingHandler();
        using var cache = new ThumbnailCache(_tempDir, handler);

        var path = await cache.GetOrDownloadAsync("https://img.example/missing.jpg");

        Assert.Null(path);
    }

    private sealed class CountingHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

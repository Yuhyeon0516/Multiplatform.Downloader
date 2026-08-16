using Multiplatform_Downloader.Core.Engine;
using Multiplatform_Downloader.Core.Models;
using Multiplatform_Downloader.Core.Queue;

namespace Multiplatform_Downloader.Tests.Engine;

public class XhsResolutionStrategyTests
{
    private const string FallbackHtml = """
    <script>window.__INITIAL_STATE__={"note":{"title":"폴백 제목","video":{"masterUrl":"https://cdn.xhscdn.com/f.mp4"}}}</script>
    """;

    [Fact]
    public async Task should_use_ytdlp_when_it_succeeds()
    {
        var ytdlp = new FakeMetadata(withFormats: true);
        var strategy = new XhsResolutionStrategy(ytdlp, new XhsFallbackExtractor(), (_, _) => Task.FromResult(FallbackHtml));

        var result = await strategy.ResolveAsync("https://www.xiaohongshu.com/explore/abc");

        Assert.Equal(ExtractionRoute.YtDlp, result.Route);
        Assert.Null(result.DirectStreamUrl);
    }

    [Fact]
    public async Task should_fallback_to_extractor_when_ytdlp_fails()
    {
        var ytdlp = new FakeMetadata(throws: true);
        var strategy = new XhsResolutionStrategy(ytdlp, new XhsFallbackExtractor(), (_, _) => Task.FromResult(FallbackHtml));

        var result = await strategy.ResolveAsync("https://www.xiaohongshu.com/explore/abc");

        Assert.Equal(ExtractionRoute.XhsFallback, result.Route);
        Assert.Equal("https://cdn.xhscdn.com/f.mp4", result.DirectStreamUrl);
        Assert.Equal("폴백 제목", result.Info.Title);
    }

    [Fact]
    public async Task should_fallback_when_ytdlp_returns_no_formats()
    {
        var ytdlp = new FakeMetadata(withFormats: false);
        var strategy = new XhsResolutionStrategy(ytdlp, new XhsFallbackExtractor(), (_, _) => Task.FromResult(FallbackHtml));

        var result = await strategy.ResolveAsync("https://www.xiaohongshu.com/explore/abc");

        Assert.Equal(ExtractionRoute.XhsFallback, result.Route);
    }

    private sealed class FakeMetadata(bool withFormats = false, bool throws = false) : IMediaMetadataService
    {
        public Task<MediaInfo> FetchAsync(string url, CancellationToken cancellationToken = default)
        {
            if (throws)
                throw new MetadataFetchException("yt-dlp 실패");
            return Task.FromResult(new MediaInfo
            {
                Title = "yt-dlp 제목",
                Formats = withFormats ? [new MediaFormat { FormatId = "0", Height = 1080 }] : [],
            });
        }
    }
}

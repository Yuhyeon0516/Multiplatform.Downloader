using Multiplatform_Downloader.Core.Platforms;

namespace Multiplatform_Downloader.Tests.Platforms;

/// <summary>FR-01·FR-N1.3: 분석 전 URL 정규화 검증(실측 이슈 기반).</summary>
public class UrlNormalizerTests
{
    [Fact]
    public void should_rewrite_rednote_to_xiaohongshu()
    {
        var result = UrlNormalizer.Normalize("https://www.rednote.com/explore/6a548dcf");
        Assert.Equal("https://www.xiaohongshu.com/explore/6a548dcf", result);
    }

    [Theory]
    [InlineData("https://www.facebook.com/radiokicksfm/videos/3676516585958356/",
                "https://www.facebook.com/watch/?v=3676516585958356")]
    [InlineData("https://m.facebook.com/cnn/videos/set/10155529876156509",
                "https://www.facebook.com/watch/?v=10155529876156509")]
    public void should_normalize_facebook_videos_path_to_watch(string input, string expected)
    {
        // 실측: /<page>/videos/<id>는 yt-dlp 'Cannot parse data' → watch?v= 로 우회(FR-N1.3)
        Assert.Equal(expected, UrlNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("https://www.douyin.com/jingxuan?modal_id=7663054684723149730",
                "https://www.douyin.com/video/7663054684723149730")]
    [InlineData("https://www.douyin.com/discover?modal_id=7663054684723149730",
                "https://www.douyin.com/video/7663054684723149730")]
    [InlineData("https://www.douyin.com/?modal_id=7663054684723149730",
                "https://www.douyin.com/video/7663054684723149730")]
    public void should_normalize_douyin_modal_to_video(string input, string expected)
    {
        // 실측: 도우인 피드/모달 URL은 'Unsupported URL' → /video/<id> 로 정규화(FR-N1)
        Assert.Equal(expected, UrlNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("https://www.facebook.com/watch/?v=10155529876156509")]
    [InlineData("https://www.facebook.com/reel/1195289147628387")]
    [InlineData("https://x.com/SpaceX/status/1927509601001341037")]
    [InlineData("https://www.youtube.com/watch?v=abc")]
    public void should_leave_non_facebook_videos_urls_unchanged(string url)
    {
        Assert.Equal(url, UrlNormalizer.Normalize(url));
    }

    [Fact]
    public void should_handle_empty_input()
    {
        Assert.Equal("", UrlNormalizer.Normalize(""));
    }
}

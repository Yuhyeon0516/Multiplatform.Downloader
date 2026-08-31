using Multiplatform_Downloader.Core.Engine;

namespace Multiplatform_Downloader.Tests.Engine;

/// <summary>
/// FR-N1.8: Threads video_versions 파싱 검증. 픽스처는 2026-08-02 실측 응답 구조
/// (threads.com/@nba/post/DVE1KAgEdgF, type 101/102/103 progressive mp4)를 축약 재현.
/// </summary>
public class ThreadsFallbackExtractorTests
{
    // 실측 구조(2026-08-02, threads.com/@nba/post/DVE1KAgEdgF): 썸네일은 image_versions2.candidates[].url
    private const string VideoHtml =
        "<html><script>{\"caption\":\"NBA postgame\"," +
        "\"image_versions2\":{\"candidates\":[" +
        "{\"height\":800,\"url\":\"https:\\/\\/scontent.cdninstagram.com\\/poster.jpg?stp=x&oe=1\"}," +
        "{\"height\":400,\"url\":\"https:\\/\\/scontent.cdninstagram.com\\/poster_small.jpg\"}]}," +
        "\"video_versions\":[" +
        "{\"type\":103,\"width\":480,\"height\":600,\"url\":\"https:\\/\\/scontent.cdninstagram.com\\/low.mp4?oh=a&oe=b\"}," +
        "{\"type\":101,\"width\":720,\"height\":900,\"url\":\"https:\\/\\/scontent.cdninstagram.com\\/high.mp4?oh=c&oe=d\"}," +
        "{\"type\":102,\"width\":640,\"height\":800,\"url\":\"https:\\/\\/scontent.cdninstagram.com\\/mid.mp4?oh=e&oe=f\"}" +
        "]}</script></html>";

    [Fact]
    public void should_extract_type_101_stream_and_poster_thumbnail()
    {
        var result = new ThreadsFallbackExtractor().Extract(VideoHtml);

        // type 101(최고 프로그레시브) 우선 선택 + JSON 슬래시 디코드
        Assert.Equal("https://scontent.cdninstagram.com/high.mp4?oh=c&oe=d", result.StreamUrl);
        Assert.Equal("NBA postgame", result.Title);
        // image_versions2 첫 후보(최대 해상도) 썸네일
        Assert.Equal("https://scontent.cdninstagram.com/poster.jpg?stp=x&oe=1", result.ThumbnailUrl);
    }

    [Fact]
    public void should_fall_back_to_first_url_when_no_type_field()
    {
        const string html =
            "<script>{\"video_versions\":[{\"url\":\"https:\\/\\/cdn\\/only.mp4\"}]}</script>";
        var result = new ThreadsFallbackExtractor().Extract(html);
        Assert.Equal("https://cdn/only.mp4", result.StreamUrl);
    }

    [Fact]
    public void should_throw_when_no_video_versions()
    {
        // 영상 없는 게시물(텍스트/이미지) 또는 JS 셸(완전 헤더 미전송) — 실측 상황
        const string shell = "<html><body>Threads app shell, no media</body></html>";
        var ex = Assert.Throws<ThreadsExtractionException>(() => new ThreadsFallbackExtractor().Extract(shell));
        Assert.Contains("video_versions", ex.Message);
    }

    [Fact]
    public void should_throw_when_video_versions_empty()
    {
        const string html = "<script>{\"video_versions\":[]}</script>";
        Assert.Throws<ThreadsExtractionException>(() => new ThreadsFallbackExtractor().Extract(html));
    }
}

using Multiplatform_Downloader.Core.Engine;

namespace Multiplatform_Downloader.Tests.Engine;

public class YtDlpJsonParserTests
{
    private const string ValidJson = """
    {
      "id": "abc123",
      "title": "테스트 영상",
      "thumbnail": "https://img.example/thumb.jpg",
      "duration": 212,
      "formats": [
        { "format_id": "137", "ext": "mp4", "height": 1080, "width": 1920, "fps": 30, "vcodec": "avc1", "acodec": "none", "filesize": 12345678 },
        { "format_id": "140", "ext": "m4a", "vcodec": "none", "acodec": "mp4a", "filesize": 3456789 },
        { "format_id": "18",  "ext": "mp4", "height": 360, "vcodec": "avc1", "acodec": "mp4a", "filesize": 5000000 }
      ]
    }
    """;

    [Fact]
    public void should_parse_title_thumbnail_duration_when_valid_json()
    {
        var info = YtDlpOutputParser.ParseInfo(ValidJson);

        Assert.Equal("abc123", info.Id);
        Assert.Equal("테스트 영상", info.Title);
        Assert.Equal("https://img.example/thumb.jpg", info.ThumbnailUrl);
        Assert.Equal(TimeSpan.FromSeconds(212), info.Duration);
    }

    [Fact]
    public void should_parse_all_formats_when_valid_json()
    {
        var info = YtDlpOutputParser.ParseInfo(ValidJson);

        Assert.Equal(3, info.Formats.Count);
        var f137 = info.Formats.Single(f => f.FormatId == "137");
        Assert.Equal(1080, f137.Height);
        Assert.Equal(30, f137.Fps);
        Assert.True(f137.IsVideoOnly);
        Assert.False(f137.IsAudioOnly);
    }

    [Fact]
    public void should_mark_audio_only_when_vcodec_none()
    {
        var info = YtDlpOutputParser.ParseInfo(ValidJson);

        var audio = info.Formats.Single(f => f.FormatId == "140");
        Assert.True(audio.IsAudioOnly);
        Assert.False(audio.IsVideoOnly);
        Assert.Null(audio.VideoCodec);
    }

    [Fact]
    public void should_treat_progressive_as_neither_audio_nor_video_only()
    {
        var info = YtDlpOutputParser.ParseInfo(ValidJson);

        var progressive = info.Formats.Single(f => f.FormatId == "18");
        Assert.False(progressive.IsAudioOnly);
        Assert.False(progressive.IsVideoOnly);
    }

    [Fact]
    public void should_return_empty_formats_when_missing()
    {
        var info = YtDlpOutputParser.ParseInfo("""{ "id": "x", "title": "t" }""");
        Assert.Empty(info.Formats);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[1,2,3]")] // 최상위가 객체 아님
    public void should_throw_format_exception_when_malformed(string json)
    {
        Assert.Throws<FormatException>(() => YtDlpOutputParser.ParseInfo(json));
    }

    [Fact]
    public void should_prefer_jpg_over_webp_top_thumbnail_when_thumbnails_present()
    {
        // YouTube 최상위 thumbnail은 흔히 webp — WPF 디코딩 불가. 배열의 jpg를 골라야 한다.
        const string json = """
        {
          "id": "x", "title": "t",
          "thumbnail": "https://img.example/maxres.webp",
          "thumbnails": [
            { "url": "https://img.example/hq.jpg", "width": 480, "height": 360 },
            { "url": "https://img.example/maxres.webp", "width": 1280, "height": 720 }
          ]
        }
        """;

        var info = YtDlpOutputParser.ParseInfo(json);

        Assert.Equal("https://img.example/hq.jpg", info.ThumbnailUrl);
    }

    [Fact]
    public void should_pick_largest_decodable_thumbnail_when_multiple_jpg()
    {
        const string json = """
        {
          "id": "x", "title": "t",
          "thumbnails": [
            { "url": "https://img.example/small.jpg", "width": 320, "height": 180 },
            { "url": "https://img.example/large.jpg", "width": 1920, "height": 1080 },
            { "url": "https://img.example/mid.png",   "width": 640, "height": 360 }
          ]
        }
        """;

        var info = YtDlpOutputParser.ParseInfo(json);

        Assert.Equal("https://img.example/large.jpg", info.ThumbnailUrl);
    }

    [Fact]
    public void should_include_extensionless_candidates_instead_of_dropping()
    {
        // FR-D1.1: 확장자 없는 XHS형 URL도 후보에 포함(화이트리스트 폐지) — 수용은 매직바이트가 판정
        const string json = """
        {
          "id": "x", "title": "t",
          "thumbnail": "http://sns-webpic-qc.xhscdn.com/abc!nd_prv_wlteh_webp_3",
          "thumbnails": [ { "url": "http://sns-webpic-qc.xhscdn.com/abc!nd_dft_wlteh_webp_3", "width": 1280, "height": 720 } ]
        }
        """;

        var info = YtDlpOutputParser.ParseInfo(json);

        Assert.NotEmpty(info.ThumbnailUrls); // 실측 XHS 케이스 — 이전 구현은 여기서 후보 0개였다
        Assert.Contains("http://sns-webpic-qc.xhscdn.com/abc!nd_dft_wlteh_webp_3", info.ThumbnailUrls);
        Assert.Contains("http://sns-webpic-qc.xhscdn.com/abc!nd_prv_wlteh_webp_3", info.ThumbnailUrls); // 최상위 폴백 포함
    }

    [Fact]
    public void should_order_wpf_friendly_extension_before_extensionless()
    {
        // jpg는 변환 없이 디코드 가능 → 같은 이미지의 webp형보다 우선
        const string json = """
        {
          "id": "x", "title": "t",
          "thumbnails": [
            { "url": "https://img.example/a!webp_variant", "width": 1920, "height": 1080 },
            { "url": "https://img.example/small.jpg", "width": 100, "height": 100 }
          ]
        }
        """;

        var info = YtDlpOutputParser.ParseInfo(json);

        Assert.Equal("https://img.example/small.jpg", info.ThumbnailUrls[0]);
        Assert.Equal("https://img.example/a!webp_variant", info.ThumbnailUrls[1]); // 버려지지 않고 후순위
    }

    [Fact]
    public void should_ignore_query_string_when_detecting_jpg_extension()
    {
        const string json = """
        {
          "id": "x", "title": "t",
          "thumbnails": [ { "url": "https://img.example/hq.jpg?sqp=abc&rs=def", "width": 480, "height": 360 } ]
        }
        """;

        var info = YtDlpOutputParser.ParseInfo(json);

        Assert.Equal("https://img.example/hq.jpg?sqp=abc&rs=def", info.ThumbnailUrl);
    }
}

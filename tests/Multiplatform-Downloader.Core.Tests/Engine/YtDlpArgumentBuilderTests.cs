using Multiplatform_Downloader.Core.Engine;

namespace Multiplatform_Downloader.Tests.Engine;

public class YtDlpArgumentBuilderTests
{
    [Fact]
    public void should_include_ignore_config_in_metadata_args()
    {
        var args = YtDlpArgumentBuilder.BuildMetadata("https://youtu.be/abc");
        Assert.Contains("--ignore-config", args);
        Assert.Contains("-J", args);
    }

    [Fact]
    public void should_include_impersonate_for_platform_blocking()
    {
        var args = YtDlpArgumentBuilder.BuildMetadata("https://vm.tiktok.com/abc");
        Assert.Contains("--impersonate", args);
    }

    [Fact]
    public void should_add_mac_user_agent_when_tiktok_url()
    {
        // 실측(2026-08-17): 틱톡 신규 봇 감지가 Windows UA를 전부 차단(Unexpected response),
        // Mac UA만 통과. 메타데이터·다운로드 두 경로 모두에 적용돼야 한다.
        var meta = YtDlpArgumentBuilder.BuildMetadata("https://www.tiktok.com/@user/video/123").ToList();
        var dl = YtDlpArgumentBuilder.BuildDownload(
            new DownloadRequest("https://www.tiktok.com/@user/video/123", "best", "out")).ToList();

        foreach (var args in new[] { meta, dl })
        {
            var idx = args.IndexOf("--user-agent");
            Assert.True(idx >= 0);
            Assert.Contains("Macintosh", args[idx + 1]);
        }
    }

    [Fact]
    public void should_not_add_user_agent_when_other_platform()
    {
        // 다른 플랫폼은 검증된 기존 동작(impersonate 기본 UA)을 유지한다
        var args = YtDlpArgumentBuilder.BuildMetadata("https://youtu.be/abc");
        Assert.DoesNotContain("--user-agent", args);
    }

    [Fact]
    public void should_place_separator_immediately_before_url()
    {
        var args = YtDlpArgumentBuilder.BuildMetadata("https://youtu.be/abc").ToList();

        var urlIndex = args.IndexOf("https://youtu.be/abc");
        Assert.True(urlIndex > 0);
        Assert.Equal("--", args[urlIndex - 1]);
    }

    [Fact]
    public void should_reject_url_starting_with_dash()
    {
        Assert.Throws<ArgumentException>(() => YtDlpArgumentBuilder.BuildMetadata("--exec=calc"));
    }

    [Fact]
    public void should_include_format_and_continue_when_downloading()
    {
        var request = new DownloadRequest("https://youtu.be/abc", "137+140", "%(title)s.%(ext)s", Continue: true);

        var args = YtDlpArgumentBuilder.BuildDownload(request).ToList();

        Assert.Contains("-f", args);
        Assert.Contains("137+140", args);
        Assert.Contains("-c", args);
        Assert.Contains("--newline", args);
        Assert.Contains("--ignore-config", args);
    }

    [Fact]
    public void should_force_progress_output_despite_print_implying_quiet()
    {
        // 실측: --print 는 quiet 를 암시해 [download] 진행률 라인이 0개가 된다 — --progress 로 강제
        var request = new DownloadRequest("https://youtu.be/abc", "best", "out.%(ext)s");
        var args = YtDlpArgumentBuilder.BuildDownload(request);
        Assert.Contains("--progress", args);
        Assert.Contains("--print", args);
    }

    [Fact]
    public void should_not_include_continue_when_not_resuming()
    {
        var request = new DownloadRequest("https://youtu.be/abc", "best", "out.%(ext)s");
        var args = YtDlpArgumentBuilder.BuildDownload(request);
        Assert.DoesNotContain("-c", args);
    }

    [Fact]
    public void should_include_proxy_and_cookies_when_provided()
    {
        var request = new DownloadRequest("https://youtu.be/abc", "best", "out", ProxyUrl: "http://p:80", CookieFile: @"C:\c.txt");

        var args = YtDlpArgumentBuilder.BuildDownload(request).ToList();

        Assert.Contains("--proxy", args);
        Assert.Contains("http://p:80", args);
        Assert.Contains("--cookies", args);
        Assert.Contains(@"C:\c.txt", args);
    }

    [Fact]
    public void should_include_cookies_from_browser_when_specified()
    {
        var request = new DownloadRequest("https://youtu.be/abc", "best", "out", CookieFromBrowser: "edge");

        var args = YtDlpArgumentBuilder.BuildDownload(request).ToList();

        var idx = args.IndexOf("--cookies-from-browser");
        Assert.True(idx >= 0);
        Assert.Equal("edge", args[idx + 1]);
        Assert.DoesNotContain("--cookies", args); // 파일 옵션은 붙지 않는다
    }

    [Fact]
    public void should_prefer_browser_cookies_over_file_when_both_provided()
    {
        var request = new DownloadRequest("https://youtu.be/abc", "best", "out",
            CookieFile: @"C:\c.txt", CookieFromBrowser: "chrome");

        var args = YtDlpArgumentBuilder.BuildDownload(request).ToList();

        Assert.Contains("--cookies-from-browser", args);
        Assert.Contains("chrome", args);
        Assert.DoesNotContain("--cookies", args);
        Assert.DoesNotContain(@"C:\c.txt", args);
    }
}

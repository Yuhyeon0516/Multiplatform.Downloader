using Multiplatform_Downloader.Core.Net;

namespace Multiplatform_Downloader.Tests.Net;

/// <summary>NFR-L3: Netscape cookies.txt 직렬화 형식 검증(yt-dlp 파서 호환).</summary>
public class CookieFileWriterTests
{
    [Fact]
    public void should_emit_netscape_header_when_serializing()
    {
        var text = CookieFileWriter.Serialize([]);
        Assert.StartsWith("# Netscape HTTP Cookie File", text);
    }

    [Fact]
    public void should_write_seven_tab_separated_fields_when_cookie_given()
    {
        var text = CookieFileWriter.Serialize(
            [new CookieRecord(".youtube.com", "/", true, 1790000000, "SID", "abc123")]);

        var line = text.Split('\n')[1];
        Assert.Equal(".youtube.com\tTRUE\t/\tTRUE\t1790000000\tSID\tabc123", line);
    }

    [Fact]
    public void should_mark_include_subdomains_false_when_domain_has_no_leading_dot()
    {
        var text = CookieFileWriter.Serialize(
            [new CookieRecord("www.tiktok.com", "/", false, 0, "n", "v")]);

        Assert.Contains("www.tiktok.com\tFALSE\t/\tFALSE\t0\tn\tv", text);
    }

    [Fact]
    public void should_write_zero_expiry_when_session_cookie()
    {
        // 세션 쿠키(만료 없음) = 0 — 음수 만료도 0으로 클램프
        var text = CookieFileWriter.Serialize(
            [new CookieRecord(".x.com", "/", true, -5, "s", "v")]);
        Assert.Contains("\t0\t", text);
    }

    [Fact]
    public void should_skip_records_with_empty_domain_or_name()
    {
        var text = CookieFileWriter.Serialize(
        [
            new CookieRecord("", "/", false, 0, "n", "v"),
            new CookieRecord(".ok.com", "/", false, 0, "", "v"),
            new CookieRecord(".ok.com", "/", false, 0, "keep", "v"),
        ]);

        var lines = text.TrimEnd('\n').Split('\n');
        Assert.Equal(2, lines.Length); // 헤더 + 유효 1건
        Assert.Contains("keep", lines[1]);
    }

    [Fact]
    public void should_default_path_to_root_when_empty()
    {
        var text = CookieFileWriter.Serialize(
            [new CookieRecord(".ok.com", "", false, 0, "n", "v")]);
        Assert.Contains(".ok.com\tTRUE\t/\t", text);
    }
}

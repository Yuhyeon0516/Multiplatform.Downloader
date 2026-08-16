using Multiplatform_Downloader.Tests.Fixtures;

namespace Multiplatform_Downloader.Tests.Fixtures;

public class TestProxyLoaderTests
{
    private static Dictionary<string, string?> Values(params (string, string?)[] pairs)
    {
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs)
            d[k] = v;
        return d;
    }

    [Fact]
    public void should_return_null_when_no_values()
    {
        Assert.Null(TestProxyLoader.Assemble(Values()));
    }

    [Fact]
    public void should_prefer_direct_proxy_url_when_present()
    {
        var values = Values(
            ("MPDL_TEST_PROXY", "http://direct:pw@example:3128"),
            ("WEBSHARE_USER_BASE", "abc"),
            ("WEBSHARE_PASS", "xyz"));

        Assert.Equal("http://direct:pw@example:3128", TestProxyLoader.Assemble(values));
    }

    [Fact]
    public void should_assemble_from_fragments_with_default_port()
    {
        var values = Values(("WEBSHARE_USER_BASE", "abc"), ("WEBSHARE_PASS", "secret"));

        Assert.Equal("http://abc:secret@p.webshare.io:80", TestProxyLoader.Assemble(values));
    }

    [Fact]
    public void should_append_rotate_suffix_when_rotate_true()
    {
        var values = Values(
            ("WEBSHARE_USER_BASE", "abc"),
            ("WEBSHARE_PASS", "secret"),
            ("WEBSHARE_PORT", "1080"),
            ("WEBSHARE_ROTATE", "true"));

        Assert.Equal("http://abc-rotate:secret@p.webshare.io:1080", TestProxyLoader.Assemble(values));
    }

    [Fact]
    public void should_return_null_when_user_or_pass_missing()
    {
        Assert.Null(TestProxyLoader.Assemble(Values(("WEBSHARE_USER_BASE", "abc"))));
        Assert.Null(TestProxyLoader.Assemble(Values(("WEBSHARE_PASS", "secret"))));
    }

    [Fact]
    public void should_parse_env_ignoring_comments_and_blank_lines()
    {
        const string content = "# comment\n\nWEBSHARE_USER_BASE=abc\nWEBSHARE_PASS=\"quoted\"\n  # indented comment\nWEBSHARE_PORT=1080\n";

        var parsed = TestProxyLoader.ParseEnv(content);

        Assert.Equal("abc", parsed["WEBSHARE_USER_BASE"]);
        Assert.Equal("quoted", parsed["WEBSHARE_PASS"]); // 따옴표 제거
        Assert.Equal("1080", parsed["WEBSHARE_PORT"]);
        Assert.DoesNotContain("# comment", parsed.Keys);
    }

    [Fact]
    public void should_mask_credentials_in_proxy_url()
    {
        var masked = TestProxyLoader.Mask("http://user123:passABC@p.webshare.io:80");

        Assert.Equal("http://***:***@p.webshare.io:80", masked);
        Assert.DoesNotContain("user123", masked);
        Assert.DoesNotContain("passABC", masked);
    }

    [Fact]
    public void should_mask_report_direct_connection_when_null()
    {
        Assert.Contains("직접 연결", TestProxyLoader.Mask(null));
    }
}

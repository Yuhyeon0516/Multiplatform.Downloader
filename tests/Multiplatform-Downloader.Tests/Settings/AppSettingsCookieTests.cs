using Multiplatform_Downloader.Core.Settings;

namespace Multiplatform_Downloader.Tests.Settings;

public class AppSettingsCookieTests
{
    [Fact]
    public void should_resolve_none_when_source_none()
    {
        var settings = new AppSettings { CookieSource = CookieSource.None };
        var cookies = settings.ResolveCookies();
        Assert.False(cookies.HasCookies);
        Assert.Null(cookies.FromBrowser);
        Assert.Null(cookies.CookieFile);
    }

    [Theory]
    [InlineData(CookieSource.ChromeBrowser, "chrome")]
    [InlineData(CookieSource.EdgeBrowser, "edge")]
    [InlineData(CookieSource.FirefoxBrowser, "firefox")]
    public void should_map_browser_source_to_yt_dlp_browser_name(CookieSource source, string expected)
    {
        var settings = new AppSettings { CookieSource = source };

        var cookies = settings.ResolveCookies();

        Assert.Equal(expected, cookies.FromBrowser);
        Assert.Null(cookies.CookieFile);
        Assert.True(cookies.HasCookies);
    }

    [Fact]
    public void should_resolve_file_path_when_source_cookie_file()
    {
        var settings = new AppSettings { CookieSource = CookieSource.CookieFile, CookieFilePath = @"C:\cookies.txt" };

        var cookies = settings.ResolveCookies();

        Assert.Equal(@"C:\cookies.txt", cookies.CookieFile);
        Assert.Null(cookies.FromBrowser);
    }

    [Fact]
    public void should_resolve_none_when_cookie_file_selected_but_path_empty()
    {
        var settings = new AppSettings { CookieSource = CookieSource.CookieFile, CookieFilePath = null };

        var cookies = settings.ResolveCookies();

        Assert.False(cookies.HasCookies);
    }
}

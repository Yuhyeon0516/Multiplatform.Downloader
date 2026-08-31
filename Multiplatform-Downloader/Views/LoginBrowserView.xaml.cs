using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Multiplatform_Downloader.Core.Net;
using Multiplatform_Downloader.ViewModels;

namespace Multiplatform_Downloader.Views;

/// <summary>
/// WebView2 호스트(FR-L3). 프로필은 %APPDATA%\Multiplatform-Downloader\webview2-profile 에
/// 영속 — 재사용 시 재로그인이 필요 없다. 초기화 실패(런타임 부재)는 안내 문구로 알린다.
/// </summary>
public partial class LoginBrowserView : Window
{
    public LoginBrowserView()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Multiplatform_Downloader.Services.ThemeService.ApplyTitleBarTo(this);
        Loaded += OnLoadedAsync;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedAsync;
        if (DataContext is not LoginBrowserViewModel vm)
            return;
        try
        {
            var profileDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Multiplatform-Downloader", "webview2-profile");
            var environment = await CoreWebView2Environment.CreateAsync(null, profileDir);
            await Browser.EnsureCoreWebView2Async(environment);

            vm.AttachCookieSource(async () =>
            {
                // uri에 null → 프로필의 모든 쿠키(플랫폼 로그인은 연관 도메인 쿠키가 함께 필요 — 예: 유튜브=google.com)
                var cookies = await Browser.CoreWebView2.CookieManager.GetCookiesAsync(null!);
                var records = new List<CookieRecord>(cookies.Count);
                foreach (var c in cookies)
                {
                    var expires = c.IsSession
                        ? 0
                        : (long)Math.Max(0, (c.Expires.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds);
                    records.Add(new CookieRecord(c.Domain, c.Path, c.IsSecure, expires, c.Name, c.Value));
                }
                return records;
            });

            Browser.CoreWebView2.Navigate(vm.StartUrl);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or FileNotFoundException or DllNotFoundException)
        {
            vm.ReportBrowserUnavailable(
                "WebView2 런타임이 설치되어 있지 않습니다 — Microsoft Edge WebView2 Runtime 설치 후 다시 시도하세요.");
        }
        catch (Exception ex)
        {
            vm.ReportBrowserUnavailable($"브라우저 초기화 실패: {ex.Message}");
        }
    }
}

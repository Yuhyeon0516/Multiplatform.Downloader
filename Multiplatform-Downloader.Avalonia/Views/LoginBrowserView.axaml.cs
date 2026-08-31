using Avalonia.Controls;
using Avalonia.Interactivity;
using Multiplatform_Downloader.Avalonia.ViewModels;

namespace Multiplatform_Downloader.Avalonia.Views;

/// <summary>
/// 로그인 창(FR-L3) — WKWebView 호스트. VM에 쿠키 열람 함수만 연결한다(WPF WebView2 뷰와 동일 시임).
/// 기본 WKWebsiteDataStore가 영속이라 로그인 세션이 앱 재시작 후에도 유지된다.
/// </summary>
public partial class LoginBrowserView : Window
{
    public LoginBrowserView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LoginBrowserViewModel vm)
            return;

        if (Browser.LastError is { } error)
        {
            vm.ReportBrowserUnavailable($"브라우저를 초기화할 수 없습니다 — {error}");
            return;
        }

        vm.AttachCookieSource(Browser.GetAllCookiesAsync);
        Browser.Navigate(vm.StartUrl);
    }
}

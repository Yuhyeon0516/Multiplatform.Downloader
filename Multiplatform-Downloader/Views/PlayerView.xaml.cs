using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Multiplatform_Downloader.ViewModels;

namespace Multiplatform_Downloader.Views;

/// <summary>
/// PlayerView 코드비하인드 — WebView2 초기화·가상 호스트 매핑·재생 HTML 내비게이션(뷰 전용 로직).
/// 재생 컨트롤은 Chromium 내장 UI. 창 닫기 = WebView2 Dispose = 완전 정지(§9).
/// </summary>
public partial class PlayerView : Window
{
    private const string MediaHost = "media.local";
    private PlayerViewModel? _vm;
    private WindowState _stateBeforeFullscreen = WindowState.Normal;

    public PlayerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PlayerViewModel vm)
            return;
        _vm = vm;
        _vm.MediaChanged += Navigate;
        try
        {
            // WebView2 사용자 데이터 폴더를 쓰기 가능한 %APPDATA%로 명시한다. 미지정 시 기본값은 exe 옆
            // (<exe>.WebView2)이라 설치본(C:\Program Files, 비관리자 쓰기 불가)에서 폴더 생성이
            // UnauthorizedAccessException으로 실패한다(실사용 보고 2026-08-03). 재생 전용 프로필.
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Multiplatform-Downloader", "webview2-player");
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await Browser.EnsureCoreWebView2Async(environment);
            var settings = Browser.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            Browser.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                var msg = args.TryGetWebMessageAsString();
                if (msg is not null && msg.StartsWith("media-error:", StringComparison.Ordinal))
                    _vm?.OnMediaError(msg["media-error:".Length..]);
            };
            // HTML 전체화면 요청은 호스트가 창을 전환해야 실제 전체화면이 된다
            // (미처리 시 전체화면 버튼이 무반응처럼 보임 — 사용자 스크린샷 2026-08-03 012149)
            Browser.CoreWebView2.ContainsFullScreenElementChanged += (_, _) =>
                SetFullscreen(Browser.CoreWebView2.ContainsFullScreenElement);
            Navigate();
        }
        catch (Exception ex)
        {
            // WebView2 런타임 미설치 등 — 폴백 안내(§9 명세)
            _vm.OnEngineFailure(ex.GetType().Name);
        }
    }

    /// <summary>현재 항목의 폴더만 읽기 전용 가상 호스트로 매핑하고 재생 페이지를 연다(file:// 미노출).</summary>
    private void Navigate()
    {
        if (_vm is null || Browser.CoreWebView2 is null)
            return;
        var directory = Path.GetDirectoryName(_vm.CurrentPath);
        var fileName = Path.GetFileName(_vm.CurrentPath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
        {
            _vm.OnMediaError();
            return;
        }
        Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
            MediaHost, directory, CoreWebView2HostResourceAccessKind.Allow);
        var src = $"https://{MediaHost}/{Uri.EscapeDataString(fileName)}";
        _vm.LogNavigate(src);
        // 리스너를 src 지정 "이전"에 등록 — 빠른 실패가 리스너보다 먼저 발화하는 경합 방지
        Browser.NavigateToString($$"""
            <!doctype html>
            <html style="height:100%"><head><meta charset="utf-8"></head>
            <body style="margin:0;height:100%;background:#000;overflow:hidden">
            <video id="v" controls autoplay style="width:100%;height:100%;outline:none"></video>
            <script>
              const v = document.getElementById('v');
              v.addEventListener('error', () => {
                const e = v.error;
                window.chrome.webview.postMessage(
                  'media-error:' + (e ? e.code : '?') + (e && e.message ? ' ' + e.message : ''));
              });
              v.src = "{{src}}";
            </script>
            </body></html>
            """);
    }

    /// <summary>Chromium 전체화면 진입/해제에 맞춰 창 크롬·정보 바를 전환한다.</summary>
    private void SetFullscreen(bool on)
    {
        if (on)
        {
            _stateBeforeFullscreen = WindowState;
            // 순서 중요: Maximized 상태에서 스타일만 바꾸면 작업표시줄이 남는다
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            TopBar.Visibility = Visibility.Collapsed;
            BottomBar.Visibility = Visibility.Collapsed;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _stateBeforeFullscreen;
            TopBar.Visibility = Visibility.Visible;
            BottomBar.Visibility = Visibility.Visible;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.MediaChanged -= Navigate;
        Browser.Dispose(); // 완전 정지 — 미디어·Chromium 프로세스 해제
    }
}

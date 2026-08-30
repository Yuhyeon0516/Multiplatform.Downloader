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
        // 핸들 생성 직후 타이틀바를 앱 테마로 명시 적용 — Loaded 클래스 핸들러가 WebView2 호스트 창에서
        // 타이밍상 놓쳐 타이틀바가 라이트로 남던 문제 보강(사용자 보고 2026-08-30).
        SourceInitialized += (_, _) => Multiplatform_Downloader.Services.ThemeService.ApplyTitleBarTo(this);
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
            // HEVC(H.265) 활성화 — 틱톡 1080p 등은 bytevc1(HEVC)이라 기본 Chromium이 디코드하지 못해
            // 소리만 나고 화면이 검게 나온다(실측 2026-08-30). OS에 HEVC 코덱이 있으면 이 플래그로 재생된다.
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--enable-features=PlatformHEVCDecoderSupport",
            };
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
            await Browser.EnsureCoreWebView2Async(environment);
            var settings = Browser.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            // 내장 재생 컨트롤(하단 바)을 앱 테마에 맞춤 — color-scheme CSS만으로는 네이티브 컨트롤이
            // 라이트로 남는다(실측 2026-08-30). Profile.PreferredColorScheme로 강제한다.
            var dark = (DataContext as PlayerViewModel)?.IsDarkTheme ?? true;
            Browser.CoreWebView2.Profile.PreferredColorScheme = dark
                ? CoreWebView2PreferredColorScheme.Dark
                : CoreWebView2PreferredColorScheme.Light;
            Browser.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                var msg = args.TryGetWebMessageAsString();
                if (msg is not null && msg.StartsWith("media-error:", StringComparison.Ordinal))
                    _vm?.OnMediaError(msg["media-error:".Length..]);
                else if (msg is not null && msg.StartsWith("media-info:", StringComparison.Ordinal))
                {
                    // videoWidth=0 = 영상 트랙 디코드 실패(HEVC 등) → 소리만 나고 화면 검음. 기본 플레이어 폴백.
                    var info = msg["media-info:".Length..];
                    var w = info.Split('x', 2) is [var ws, ..] && int.TryParse(ws, out var vw) ? vw : -1;
                    if (w == 0)
                        _vm?.OnUnplayableVideoTrack();
                }
            };
            // HTML 전체화면 요청은 호스트가 창을 전환해야 실제 전체화면이 된다
            // (미처리 시 전체화면 버튼이 무반응처럼 보임 — 사용자 스크린샷 2026-08-03 012149)
            Browser.CoreWebView2.ContainsFullScreenElementChanged += (_, _) =>
                SetFullscreen(Browser.CoreWebView2.ContainsFullScreenElement);
            // 내장 컨트롤(하단 바)이 color-scheme를 첫 페인트에 반영하지 못하고 창 포커스 전환 시에야
            // 다크로 갱신되는 문제(실측 2026-08-30) — 로드 완료 후 강제 리페인트로 즉시 반영한다.
            Browser.CoreWebView2.NavigationCompleted += (_, _) => ForceRepaint();
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
        // color-scheme를 앱 테마에 맞춰 Chromium 내장 재생 컨트롤(하단 바)이 다크/라이트로 렌더되게 한다
        // (미지정 시 항상 라이트로 나와 다크 테마와 어긋난다 — 사용자 보고 2026-08-30).
        var scheme = _vm.IsDarkTheme ? "dark" : "light";
        // 리스너를 src 지정 "이전"에 등록 — 빠른 실패가 리스너보다 먼저 발화하는 경합 방지
        Browser.NavigateToString($$"""
            <!doctype html>
            <html style="height:100%;color-scheme:{{scheme}}"><head><meta charset="utf-8"></head>
            <body style="margin:0;height:100%;background:#000;overflow:hidden">
            <video id="v" controls autoplay style="width:100%;height:100%;outline:none"></video>
            <script>
              const v = document.getElementById('v');
              v.addEventListener('error', () => {
                const e = v.error;
                window.chrome.webview.postMessage(
                  'media-error:' + (e ? e.code : '?') + (e && e.message ? ' ' + e.message : ''));
              });
              v.addEventListener('loadeddata', () => {
                window.chrome.webview.postMessage(
                  'media-info:' + v.videoWidth + 'x' + v.videoHeight + ' ready=' + v.readyState);
              });
              v.src = "{{src}}";
            </script>
            </body></html>
            """);
    }

    /// <summary>WebView2 내장 컨트롤의 color-scheme 초기 페인트 누락을 강제 리페인트로 해소한다.
    /// 창 포커스 전환이 유발하던 재합성을 1px 크기 토글로 대신 트리거한다(HWND 리사이즈 → Chromium 재페인트).</summary>
    private void ForceRepaint()
    {
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            var m = Browser.Margin;
            Browser.Margin = new Thickness(m.Left, m.Top, m.Right, m.Bottom + 1);
            Browser.UpdateLayout();
            Browser.Margin = m;
            Browser.UpdateLayout();
        }), System.Windows.Threading.DispatcherPriority.Background);
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

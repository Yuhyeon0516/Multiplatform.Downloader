using System.Web;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Multiplatform_Downloader.Avalonia.ViewModels;

namespace Multiplatform_Downloader.Avalonia.Views;

/// <summary>
/// 인앱 플레이어(§9) — WKWebView의 HTML5 &lt;video&gt;로 재생. macOS는 HEVC를 네이티브 디코드하므로
/// WPF WebView2의 코덱 우회·가상 호스트 매핑·강제 리페인트 워크어라운드는 필요 없다.
/// </summary>
public partial class PlayerView : Window
{
    private PlayerViewModel? _vm;

    public PlayerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += (_, _) => Rewire();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => Rewire();

    private void Rewire()
    {
        if (_vm is not null)
            _vm.MediaChanged -= LoadCurrent;
        _vm = DataContext as PlayerViewModel;
        if (_vm is null)
            return;
        _vm.MediaChanged += LoadCurrent;
        if (IsLoaded)
            LoadCurrent();
    }

    private int _loadVersion;

    private void LoadCurrent() => _ = LoadCurrentAsync();

    private async Task LoadCurrentAsync()
    {
        if (_vm is null)
            return;
        if (Player.LastError is { } error)
        {
            _vm.OnEngineFailure(error);
            return;
        }

        var version = ++_loadVersion; // 변환 중 이전/다음 이동 시 이전 로드 무시
        var sourcePath = _vm.CurrentPath;

        // WKWebView는 MKV/WebM을 재생하지 못한다 — 비호환 컨테이너는 임시 mp4로 변환(§9 macOS 분기)
        if (!Services.PlaybackTranscoder.IsDirectlyPlayable(sourcePath))
            _vm.SetStatus("재생 형식 변환 중… (비디오 무손실 복사)");
        var playable = await Services.PlaybackTranscoder.EnsurePlayableAsync(sourcePath);
        if (version != _loadVersion || _vm is null)
            return;
        if (playable is null)
        {
            _vm.OnMediaError($"{System.IO.Path.GetExtension(sourcePath).TrimStart('.').ToUpperInvariant()} 변환 실패");
            return;
        }
        _vm.SetStatus(string.Empty);

        var fileUrl = new Uri(playable).AbsoluteUri; // file:// 이스케이프 처리
        _vm.LogNavigate(fileUrl);
        var bg = _vm.IsDarkTheme ? "#101215" : "#F4F5F7";
        var html = $$"""
            <!doctype html>
            <html style="color-scheme:{{(_vm.IsDarkTheme ? "dark" : "light")}}">
            <head><meta charset="utf-8"></head>
            <body style="margin:0;background:{{bg}};display:flex;align-items:center;justify-content:center;height:100vh">
              <video src="{{HttpUtility.HtmlAttributeEncode(fileUrl)}}" controls autoplay
                     style="max-width:100%;max-height:100%;outline:none"></video>
            </body></html>
            """;
        Player.LoadHtmlFile(html, "/");
    }
}

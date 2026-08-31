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

    private void LoadCurrent()
    {
        if (_vm is null)
            return;
        if (Player.LastError is { } error)
        {
            _vm.OnEngineFailure(error);
            return;
        }

        var fileUrl = new Uri(_vm.CurrentPath).AbsoluteUri; // file:// 이스케이프 처리
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

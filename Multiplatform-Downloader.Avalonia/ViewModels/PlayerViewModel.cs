using Multiplatform_Downloader.Avalonia.Mvvm;
using Multiplatform_Downloader.Avalonia.Services;
using Multiplatform_Downloader.Core.Diagnostics;

namespace Multiplatform_Downloader.Avalonia.ViewModels;

/// <summary>플레이어의 재생 대상 1건 — 완료 항목의 제목·파일 경로.</summary>
internal sealed record PlayerItem(string Title, string Path);

/// <summary>
/// 인앱 플레이어(§9) — WPF 헤드 이식. macOS에서는 WKWebView가 HEVC를 네이티브 디코드하므로
/// WPF의 HEVC 폴백(OnUnplayableVideoTrack)류 워크어라운드는 이식하지 않는다.
/// </summary>
internal sealed class PlayerViewModel : Screen
{
    private readonly IAppLogger _logger;
    private readonly IReadOnlyList<PlayerItem> _playlist;
    private int _index;

    /// <summary>이전/다음 이동 시 뷰가 다시 내비게이트하도록 알린다.</summary>
    public event Action? MediaChanged;

    public PlayerViewModel(IReadOnlyList<PlayerItem> playlist, int startIndex, IAppLogger? logger = null, bool isDarkTheme = true)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        if (playlist.Count == 0)
            throw new ArgumentException("재생 목록이 비어 있습니다.", nameof(playlist));
        _playlist = playlist;
        _index = Math.Clamp(startIndex, 0, playlist.Count - 1);
        _logger = logger ?? NullAppLogger.Instance;
        IsDarkTheme = isDarkTheme;
        DisplayName = WindowTitle;
    }

    public bool IsDarkTheme { get; }

    public PlayerItem Current => _playlist[_index];
    public string WindowTitle => $"재생 — {Current.Title}";
    public string CurrentTitle => Current.Title;
    public string CurrentPath => Current.Path;

    public string MetaInfo
    {
        get
        {
            var ext = System.IO.Path.GetExtension(CurrentPath).TrimStart('.').ToUpperInvariant();
            try
            {
                var size = new FileInfo(CurrentPath).Length / 1048576.0;
                return $"{ext} · {size:F1} MB · {CurrentPath}";
            }
            catch (Exception)
            {
                return $"{ext} · {CurrentPath}";
            }
        }
    }

    public string StatusText { get; private set; } = string.Empty;
    public bool HasStatus => StatusText.Length > 0;

    public bool CanPrevItem => _index > 0;
    public bool CanNextItem => _index < _playlist.Count - 1;

    public void PrevItem() => MoveTo(_index - 1);
    public void NextItem() => MoveTo(_index + 1);

    private void MoveTo(int index)
    {
        if (index < 0 || index >= _playlist.Count)
            return;
        _index = index;
        StatusText = string.Empty;
        NotifyOfPropertyChange(string.Empty);
        DisplayName = WindowTitle;
        MediaChanged?.Invoke();
    }

    /// <summary>미지원 코덱 폴백 — 연결 프로그램(QuickTime 등)으로 실행.</summary>
    public void OpenInDefaultPlayer() => Finder.OpenWithDefaultApp(CurrentPath);

    public void OpenFolder()
    {
        if (File.Exists(CurrentPath))
            Finder.Reveal(CurrentPath);
    }

    /// <summary>뷰의 재생 실패 수신(§9 폴백).</summary>
    public void OnMediaError(string detail = "")
    {
        StatusText = $"인앱 재생 실패({(detail.Length > 0 ? detail : "?")}) — [기본 플레이어]로 여세요";
        NotifyOfPropertyChange(nameof(StatusText));
        NotifyOfPropertyChange(nameof(HasStatus));
        _logger.Warning("Player", $"인앱 재생 실패[{detail}]: {System.IO.Path.GetFileName(CurrentPath)}");
    }

    public void LogNavigate(string src) =>
        _logger.Info("Player", $"재생 시도: {src}");

    /// <summary>웹뷰 초기화 실패 — 폴백 안내.</summary>
    public void OnEngineFailure(string reason)
    {
        StatusText = $"인앱 플레이어를 열 수 없습니다({reason}) — [기본 플레이어]로 여세요";
        NotifyOfPropertyChange(nameof(StatusText));
        NotifyOfPropertyChange(nameof(HasStatus));
        _logger.Warning("Player", $"웹뷰 초기화 실패: {reason}");
    }
}

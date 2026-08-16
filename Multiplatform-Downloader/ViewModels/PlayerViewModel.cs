using Caliburn.Micro;
using Multiplatform_Downloader.Core.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Multiplatform_Downloader.ViewModels;

/// <summary>플레이어의 재생 대상 1건 — 완료 항목의 제목·파일 경로.</summary>
internal sealed record PlayerItem(string Title, string Path);

/// <summary>
/// 인앱 플레이어(스토리보드 §9). WebView2 <c>&lt;video&gt;</c>로 재생하고
/// 컨트롤(재생/일시정지/탐색/음량/전체화면)은 Chromium 내장 UI를 그대로 쓴다.
/// 완전 정지 = 창 닫기(뷰가 WebView2를 Dispose). 미지원 코덱은 [기본 플레이어] 폴백.
/// </summary>
internal sealed class PlayerViewModel : Screen
{
    private readonly IAppLogger _logger;
    private readonly IReadOnlyList<PlayerItem> _playlist;
    private int _index;

    /// <summary>이전/다음 이동 시 뷰가 다시 내비게이트하도록 알린다.</summary>
    public event System.Action? MediaChanged; // Caliburn.Micro.Action과의 모호성 방지

    public PlayerViewModel(IReadOnlyList<PlayerItem> playlist, int startIndex, IAppLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        if (playlist.Count == 0)
            throw new ArgumentException("재생 목록이 비어 있습니다.", nameof(playlist));
        _playlist = playlist;
        _index = Math.Clamp(startIndex, 0, playlist.Count - 1);
        _logger = logger ?? NullAppLogger.Instance;
        DisplayName = WindowTitle;
    }

    public PlayerItem Current => _playlist[_index];
    public string WindowTitle => $"재생 — {Current.Title}";
    public string CurrentTitle => Current.Title;
    public string CurrentPath => Current.Path;

    /// <summary>확장자 · 크기 · 경로 요약(정보 바). 파일 조회 실패 시 경로만.</summary>
    public string MetaInfo
    {
        get
        {
            var ext = Path.GetExtension(CurrentPath).TrimStart('.').ToUpperInvariant();
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

    /// <summary>재생 실패·엔진 실패 안내(폴백 유도). 비어 있으면 숨김.</summary>
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
        MediaChanged?.Invoke();
    }

    /// <summary>AV1 등 미지원 코덱 폴백 — 연결 프로그램으로 실행.</summary>
    public void OpenInDefaultPlayer()
    {
        try
        {
            Process.Start(new ProcessStartInfo(CurrentPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Warning("Player", $"기본 플레이어 실행 실패: {ex.GetType().Name}");
        }
    }

    public void OpenFolder()
    {
        try
        {
            if (File.Exists(CurrentPath))
                Process.Start("explorer.exe", $"/select,\"{CurrentPath}\"");
        }
        catch (Exception)
        {
            // 탐색기 실행 실패는 무시 (카드 폴더 열기와 동일 정책)
        }
    }

    /// <summary>뷰의 video onerror 수신(§9 폴백). detail = "code message" —
    /// 2=NETWORK(서빙) · 3=DECODE(코덱) · 4=SRC_NOT_SUPPORTED(경로/MIME).</summary>
    public void OnMediaError(string detail = "")
    {
        StatusText = $"인앱 재생 실패({(detail.Length > 0 ? detail : "?")}) — [기본 플레이어]로 여세요";
        NotifyOfPropertyChange(nameof(StatusText));
        NotifyOfPropertyChange(nameof(HasStatus));
        _logger.Warning("Player", $"인앱 재생 실패[{detail}]: {Path.GetFileName(CurrentPath)}");
    }

    /// <summary>내비게이트 대상 로깅 — URL 이스케이프 문제 진단용.</summary>
    public void LogNavigate(string src) =>
        _logger.Info("Player", $"재생 시도: {src}");

    /// <summary>WebView2 초기화 실패(런타임 미설치 등) — 폴백 안내.</summary>
    public void OnEngineFailure(string reason)
    {
        StatusText = $"인앱 플레이어를 열 수 없습니다({reason}) — [기본 플레이어]로 여세요";
        NotifyOfPropertyChange(nameof(StatusText));
        NotifyOfPropertyChange(nameof(HasStatus));
        _logger.Warning("Player", $"WebView2 초기화 실패: {reason}");
    }
}

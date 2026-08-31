using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Multiplatform_Downloader.Avalonia.Mvvm;
using Multiplatform_Downloader.Avalonia.Services;
using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Engine;
using Multiplatform_Downloader.Core.Media;
using Multiplatform_Downloader.Core.Platforms;
using Multiplatform_Downloader.Core.Queue;
using System.Text.RegularExpressions;

namespace Multiplatform_Downloader.Avalonia.ViewModels;

/// <summary>다운로드 큐 카드의 뷰모델(FR-07) — WPF 헤드 이식.
/// 변경점: WPF ImageSource/Brush → Avalonia 타입, WebP는 Skia가 네이티브 디코드(ffmpeg 변환 삭제),
/// explorer.exe → Finder(open -R).</summary>
internal sealed class DownloadItemViewModel : PropertyChangedBase
{
    private static readonly MediaFormatSelector _formatSelector = new();

    private readonly IDownloadQueueService _queue;
    private readonly IAppLogger _logger;
    private readonly int _thumbRetryCount;
    private string? _outputPath;
    private IReadOnlyList<string>? _attemptedCandidates;
    private bool _localThumbTried;
    private IReadOnlyList<Core.Models.MediaFormat>? _optionsSourceFormats;

    public DownloadItemViewModel(DownloadItem item, IDownloadQueueService queue, int thumbRetryCount = 2, IAppLogger? logger = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _logger = logger ?? NullAppLogger.Instance;
        _thumbRetryCount = Math.Clamp(thumbRetryCount, 0, 5);
        Id = item.Id;
        PlatformBadge = BadgeText(item.Platform);
        PlatformBrush = BadgeBrush(item.Platform);
        Refresh(item);
    }

    public Guid Id { get; }
    public string PlatformBadge { get; }
    public IBrush PlatformBrush { get; }

    public string Title { get; private set; } = string.Empty;
    public DownloadStatus Status { get; private set; }
    public string TitleFull { get; private set; } = string.Empty;
    public double Progress { get; private set; }
    public string SubText { get; private set; } = string.Empty;
    public string StatusText { get; private set; } = string.Empty;
    public IBrush StatusBrush { get; private set; } = Brushes.Gray;
    public IBrush ProgressBrush { get; private set; } = Brushes.SteelBlue;
    public string SelectedResolution { get; private set; } = "—";
    public Bitmap? Thumbnail { get; private set; }
    public bool HasThumbnail { get; private set; }

    public bool ThumbFailed { get; private set; }
    public string ThumbFailReason { get; private set; } = string.Empty;

    public IReadOnlyList<ResolutionOption> ResolutionOptions { get; private set; } = [];
    private string? _selectedFormatId;

    public ResolutionOption? SelectedOption
    {
        get => ResolutionOptions.FirstOrDefault(o => o.FormatId == _selectedFormatId);
        set
        {
            if (value is null || value.FormatId == _selectedFormatId)
                return;
            _queue.ChangeFormat(Id, value.FormatId);
        }
    }

    public bool CanChangeResolution { get; private set; }

    private bool _isChecked = true;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
                return;
            _isChecked = value;
            NotifyOfPropertyChange();
        }
    }

    public bool ShowResolutionBadge { get; private set; }

    public bool CanStartItem { get; private set; }
    public bool CanPauseItem { get; private set; }
    public bool CanResumeItem { get; private set; }
    public bool CanCancelItem { get; private set; }
    public bool CanRetryItem { get; private set; }
    public bool CanRemoveItem { get; private set; }
    public bool CanOpenFolderItem { get; private set; }
    public bool HasResolution { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsIndeterminate { get; private set; }
    public bool CanLoginFixItem { get; private set; }
    public string OriginalUrl { get; private set; } = string.Empty;

    public Action<DownloadItemViewModel>? LoginFixRequested { get; set; }

    public bool CanPlayItem { get; private set; }
    internal string? OutputPath => _outputPath;
    public Action<DownloadItemViewModel>? PlayRequested { get; set; }

    public void PlayItem() => PlayRequested?.Invoke(this);

    public bool CanDragItem { get; private set; }

    internal string? GetDraggablePath() =>
        _outputPath is null ? null : ShellViewModel.ResolveMediaPath(_outputPath);

    // ── 주요 액션(FR-U3.1) ──

    public string PrimaryActionKind { get; private set; } = "none";
    public string PrimaryActionLabel { get; private set; } = string.Empty;
    public bool PrimaryIsAccent => PrimaryActionKind == "accent";
    public bool PrimaryIsNeutral => PrimaryActionKind == "neutral";
    public bool PrimaryIsDanger => PrimaryActionKind == "danger";
    public bool CanPrimaryAction => PrimaryActionKind != "none";

    public string OverflowHint => CanPrimaryAction ? "항목 작업" : "합치는 중에는 중단할 수 없습니다";

    public void PrimaryAction()
    {
        if (CanLoginFixItem) LoginFixItem();
        else if (CanStartItem) StartItem();
        else if (CanResumeItem) ResumeItem();
        else if (CanPauseItem) PauseItem();
        else if (CanRetryItem) RetryItem();
        else if (CanPlayItem) PlayItem();
        else if (CanOpenFolderItem) OpenFolderItem();
        else if (CanCancelItem) CancelItem();
        else if (CanRemoveItem) RemoveItem();
    }

    private (string Kind, string Label) ComputePrimary()
    {
        if (CanLoginFixItem) return ("accent", "로그인");
        if (CanStartItem) return ("accent", "받기");
        if (CanResumeItem) return ("accent", "재개");
        if (CanPauseItem) return ("neutral", "일시정지");
        if (CanRetryItem) return ("accent", "재시도");
        if (CanPlayItem) return ("accent", "재생");
        if (CanOpenFolderItem) return ("neutral", "폴더 열기");
        if (CanCancelItem) return ("danger", "취소");
        if (CanRemoveItem) return ("danger", "삭제");
        return ("none", string.Empty);
    }

    public void LoginFixItem() => LoginFixRequested?.Invoke(this);

    public Func<DownloadItemViewModel, Task<bool>>? ConfirmRemove { get; set; }

    public void Refresh(DownloadItem item)
    {
        _outputPath = item.OutputFilePath;
        Status = item.Status;
        TryLoadThumbnail(item);
        var rawTitle = string.IsNullOrWhiteSpace(item.Title) ? item.OriginalUrl : item.Title!;
        TitleFull = rawTitle;
        Title = CollapseToSingleLine(rawTitle);
        Progress = item.ProgressPercent;
        IsIndeterminate = item.Status is DownloadStatus.Analyzing or DownloadStatus.Merging
            || (item.Status == DownloadStatus.Downloading && item.ProgressPercent <= 0);
        StatusText = TranslateStatus(item.Status);
        StatusBrush = StatusColor(item.Status);
        ProgressBrush = ProgressColor(item.Status);
        SubText = BuildSubText(item);
        SelectedResolution = ResolutionLabel(item);
        HasResolution = item.SelectedFormatId is not null;

        if (!ReferenceEquals(_optionsSourceFormats, item.Formats))
        {
            _optionsSourceFormats = item.Formats;
            ResolutionOptions = _formatSelector.BuildOptions(item.Formats);
        }
        _selectedFormatId = item.SelectedFormatId;
        CanChangeResolution = ResolutionOptions.Count > 0
            && item.Status is DownloadStatus.Ready or DownloadStatus.Failed or DownloadStatus.Canceled;
        ShowResolutionBadge = !CanChangeResolution && HasResolution;

        CanStartItem = item.Status == DownloadStatus.Ready;
        CanPauseItem = item.Status == DownloadStatus.Downloading;
        CanResumeItem = item.Status == DownloadStatus.Paused;
        CanCancelItem = item.Status is DownloadStatus.Queued or DownloadStatus.Analyzing
            or DownloadStatus.Ready or DownloadStatus.Downloading or DownloadStatus.Paused;
        CanRetryItem = item.Status is DownloadStatus.Failed or DownloadStatus.Canceled or DownloadStatus.Unavailable;
        CanRemoveItem = item.Status is DownloadStatus.Queued or DownloadStatus.Ready
            or DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Canceled or DownloadStatus.Unavailable;
        IsActive = item.Status is DownloadStatus.Analyzing or DownloadStatus.Downloading
            or DownloadStatus.Merging or DownloadStatus.Paused;
        OriginalUrl = item.OriginalUrl;
        CanLoginFixItem = item.Status == DownloadStatus.Unavailable
            && item.LastErrorCategory == ErrorCategory.LoginRequired;
        CanOpenFolderItem = item.Status == DownloadStatus.Completed && !string.IsNullOrEmpty(item.OutputFilePath);
        CanPlayItem = CanOpenFolderItem;
        CanDragItem = CanOpenFolderItem;

        (PrimaryActionKind, PrimaryActionLabel) = ComputePrimary();

        NotifyOfPropertyChange(string.Empty); // 모든 바인딩 갱신
    }

    // ── 카드 액션 ──
    public void StartItem() => _queue.Start(Id);
    public void PauseItem() => _queue.Pause(Id);
    public void ResumeItem() => _queue.Resume(Id);
    public void CancelItem() => _queue.Cancel(Id);
    public void RetryItem() => _queue.Retry(Id);

    public void RemoveItem() => _ = RemoveItemAsync();

    internal async Task RemoveItemAsync()
    {
        if (ConfirmRemove is not null && !await ConfirmRemove(this))
            return;
        _queue.Remove(Id);
    }

    public void OpenFolderItem()
    {
        if (string.IsNullOrEmpty(_outputPath))
            return;
        if (File.Exists(_outputPath))
            Finder.Reveal(_outputPath);
        else
        {
            var dir = Path.GetDirectoryName(_outputPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Finder.OpenFolder(dir);
        }
    }

    private static readonly HttpClient _http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // 일부 CDN(인스타 scontent 등)은 축약 UA·비브라우저 Accept 를 거부한다 — 완전한 브라우저 헤더 사용
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/apng,image/*");
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*; q=0.8");
        return client;
    }

    private void TryLoadThumbnail(DownloadItem item)
    {
        var candidates = item.ThumbnailCandidates.Count > 0
            ? item.ThumbnailCandidates
            : (string.IsNullOrWhiteSpace(item.ThumbnailPath) ? [] : new[] { item.ThumbnailPath! });

        var localVideo = item.Status == DownloadStatus.Completed ? item.OutputFilePath : null;

        var sameCandidates = candidates.Count > 0 && ReferenceEquals(candidates, _attemptedCandidates);
        if ((candidates.Count == 0 || sameCandidates) && (string.IsNullOrEmpty(localVideo) || _localThumbTried))
            return;

        if (candidates.Count > 0)
            _attemptedCandidates = candidates;
        _ = LoadThumbnailAsync(candidates, localVideo);
    }

    private async Task LoadThumbnailAsync(IReadOnlyList<string> candidates, string? localVideo = null)
    {
        var lastReason = "후보 없음";

        foreach (var url in candidates)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                lastReason = "잘못된 URL";
                continue;
            }

            byte[]? bytes = null;
            for (var attempt = 0; attempt <= _thumbRetryCount; attempt++)
            {
                try
                {
                    bytes = await _http.GetByteArrayAsync(uri).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    lastReason = ex is HttpRequestException { StatusCode: not null } hre
                        ? $"다운로드 실패(HTTP {(int)hre.StatusCode!})"
                        : $"다운로드 실패({ex.GetType().Name})";
                    if (attempt < _thumbRetryCount)
                        await Task.Delay(TimeSpan.FromSeconds(1 << attempt)).ConfigureAwait(false);
                }
            }
            if (bytes is null)
                continue;

            var kind = ImageSniffer.Sniff(bytes);
            if (kind == SniffedImageKind.NotImage)
            {
                lastReason = "이미지가 아닌 응답(차단 페이지 가능)";
                continue;
            }

            // Skia는 WebP 포함 대부분을 디코드한다 — ffmpeg 변환 폴백 불필요
            var bitmap = TryDecode(bytes);
            if (bitmap is null)
            {
                lastReason = $"디코드 실패({kind})";
                continue;
            }

            OnUi(() =>
            {
                Thumbnail = bitmap;
                HasThumbnail = true;
                ThumbFailed = false;
                NotifyOfPropertyChange(nameof(Thumbnail));
                NotifyOfPropertyChange(nameof(HasThumbnail));
                NotifyOfPropertyChange(nameof(ThumbFailed));
            });
            return;
        }

        // 원격 후보 전부 실패·부재 → 완료된 로컬 파일에서 프레임 추출 폴백
        if (!string.IsNullOrEmpty(localVideo) && !_localThumbTried)
        {
            _localThumbTried = true;
            var resolved = ShellViewModel.ResolveMediaPath(localVideo);
            var frame = await VideoFrameExtractor.ExtractVideoFrameAsync(resolved).ConfigureAwait(false);
            var frameBitmap = frame is not null ? TryDecode(frame) : null;
            if (frameBitmap is not null)
            {
                OnUi(() =>
                {
                    Thumbnail = frameBitmap;
                    HasThumbnail = true;
                    ThumbFailed = false;
                    NotifyOfPropertyChange(nameof(Thumbnail));
                    NotifyOfPropertyChange(nameof(HasThumbnail));
                    NotifyOfPropertyChange(nameof(ThumbFailed));
                });
                return;
            }
        }

        var reason = lastReason;
        _logger.Warning("Thumb", $"[{Id.ToString("N")[..8]}] 썸네일 실패 — {reason} (후보 {candidates.Count}개 소진)");
        OnUi(() =>
        {
            ThumbFailed = true;
            ThumbFailReason = $"썸네일을 불러오지 못했습니다 — {reason}";
            NotifyOfPropertyChange(nameof(ThumbFailed));
            NotifyOfPropertyChange(nameof(ThumbFailReason));
        });
    }

    /// <summary>바이트를 Avalonia Bitmap으로 디코드(96px 다운샘플). 실패 시 null.</summary>
    private static Bitmap? TryDecode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(stream, 96);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void OnUi(Action action)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            Dispatcher.UIThread.Post(action);
        else
            action();
    }

    // ── 표시 헬퍼 ──
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    private static string CollapseToSingleLine(string text)
        => string.IsNullOrEmpty(text) ? text : WhitespaceRun.Replace(text, " ").Trim();

    private static string BuildSubText(DownloadItem item) => item.Status switch
    {
        DownloadStatus.Analyzing => "메타데이터 조회 중 (최대 30초)…",
        DownloadStatus.Queued => "분석 대기 중…",
        DownloadStatus.Ready => "받을 준비가 되었습니다",
        DownloadStatus.Downloading => FormatDownloading(item),
        DownloadStatus.Paused => $"일시정지 · {item.ProgressPercent:F1}%",
        DownloadStatus.Merging => "합치는 중…",
        DownloadStatus.Completed => $"완료 · {item.OutputFilePath ?? "(경로 미확인)"}"
            + (item.ExtractionRoute == ExtractionRoute.XhsFallback ? " · 폴백 경로" : string.Empty),
        DownloadStatus.Failed => item.ErrorMessage ?? "실패",
        DownloadStatus.Canceled => "취소됨",
        DownloadStatus.Unavailable => item.ErrorMessage ?? "다운로드할 수 없는 링크입니다",
        _ => item.OriginalUrl,
    };

    private static string FormatDownloading(DownloadItem item)
    {
        var parts = $"{item.ProgressPercent:F1}%";
        if (item.SpeedBytesPerSec is { } speed and > 0)
            parts += $" · {speed / 1024.0 / 1024.0:F1} MiB/s";
        if (item.Eta is { } eta)
            parts += $" · 남은 시간 {eta:mm\\:ss}";
        return parts;
    }

    private static string ResolutionLabel(DownloadItem item)
    {
        if (item.SelectedFormatId is null)
            return "—";
        var fmt = item.Formats.FirstOrDefault(f => f.FormatId == item.SelectedFormatId);
        return fmt?.Height is { } h ? $"{h}p" : item.SelectedFormatId;
    }

    private static string TranslateStatus(DownloadStatus status) => status switch
    {
        DownloadStatus.Queued => "대기열",
        DownloadStatus.Analyzing => "분석 중",
        DownloadStatus.Ready => "받기 준비됨",
        DownloadStatus.Downloading => "다운로드 중",
        DownloadStatus.Paused => "일시정지",
        DownloadStatus.Merging => "합치는 중",
        DownloadStatus.Completed => "완료",
        DownloadStatus.Failed => "실패",
        DownloadStatus.Canceled => "취소됨",
        DownloadStatus.Unavailable => "받을 수 없음",
        _ => status.ToString(),
    };

    private static string BadgeText(PlatformType platform) => platform switch
    {
        PlatformType.YouTube => "YT",
        PlatformType.Instagram => "IG",
        PlatformType.TikTok => "TT",
        PlatformType.Xiaohongshu => "XHS",
        PlatformType.Threads => "TH",
        PlatformType.Facebook => "FB",
        PlatformType.X => "X",
        PlatformType.Douyin => "DY",
        PlatformType.Reddit => "RD",
        PlatformType.Pinterest => "PT",
        _ => "?",
    };

    private static IBrush BadgeBrush(PlatformType platform) => new SolidColorBrush(platform switch
    {
        PlatformType.YouTube => Color.FromRgb(0xEF, 0x44, 0x44),
        PlatformType.Instagram => Color.FromRgb(0xD9, 0x46, 0xEF),
        PlatformType.TikTok => Color.FromRgb(0x11, 0x18, 0x27),
        PlatformType.Xiaohongshu => Color.FromRgb(0xF4, 0x3F, 0x5E),
        PlatformType.Threads => Color.FromRgb(0x00, 0x00, 0x00),
        PlatformType.Facebook => Color.FromRgb(0x18, 0x77, 0xF2),
        PlatformType.X => Color.FromRgb(0x00, 0x00, 0x00),
        PlatformType.Douyin => Color.FromRgb(0x00, 0x00, 0x00),
        PlatformType.Reddit => Color.FromRgb(0xFF, 0x45, 0x00),
        PlatformType.Pinterest => Color.FromRgb(0xE6, 0x00, 0x23),
        _ => Colors.Gray,
    });

    private static IBrush StatusColor(DownloadStatus status) => new SolidColorBrush(status switch
    {
        DownloadStatus.Downloading or DownloadStatus.Merging => Color.FromRgb(0x60, 0xA5, 0xFA),
        DownloadStatus.Completed => Color.FromRgb(0x16, 0xA3, 0x4A),
        DownloadStatus.Failed => Color.FromRgb(0xDC, 0x26, 0x26),
        DownloadStatus.Unavailable => Color.FromRgb(0x8B, 0x93, 0xA1),
        DownloadStatus.Analyzing => Color.FromRgb(0xD9, 0x77, 0x06),
        _ => Color.FromRgb(0x9A, 0xA3, 0xB2),
    });

    private static IBrush ProgressColor(DownloadStatus status) => new SolidColorBrush(status switch
    {
        DownloadStatus.Completed => Color.FromRgb(0x16, 0xA3, 0x4A),
        DownloadStatus.Failed => Color.FromRgb(0xDC, 0x26, 0x26),
        _ => Color.FromRgb(0x25, 0x63, 0xEB),
    });
}

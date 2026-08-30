using Caliburn.Micro;
using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Engine;
using Multiplatform_Downloader.Core.Media;
using Multiplatform_Downloader.Core.Platforms;
using Multiplatform_Downloader.Core.Queue;
using Multiplatform_Downloader.Services;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Multiplatform_Downloader.ViewModels;

/// <summary>다운로드 큐 카드의 뷰모델(FR-07). 와이어프레임 WF-01의 카드 요소를 그대로 노출한다.</summary>
internal sealed class DownloadItemViewModel : PropertyChangedBase
{
    private static readonly MediaFormatSelector _formatSelector = new();

    private readonly IDownloadQueueService _queue;
    private readonly IAppLogger _logger;
    private readonly int _thumbRetryCount;
    private string? _outputPath;
    private IReadOnlyList<string>? _attemptedCandidates; // 후보 목록 참조 — 같은 목록 재시도 방지
    private bool _localThumbTried; // 로컬 프레임 썸네일 폴백 1회 시도 가드
    private IReadOnlyList<Core.Models.MediaFormat>? _optionsSourceFormats; // 옵션 캐시 무효화 기준(참조 비교)

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
    public Brush PlatformBrush { get; }

    /// <summary>카드 표시용 제목 — 한 줄로 접음(줄바꿈·연속 공백 제거). 넘치면 XAML이 …로 생략.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>현재 상태 — 셸의 필터 버킷 판정(FR-U2.2)용. additive(기존 플래그 계약 불변).</summary>
    public DownloadStatus Status { get; private set; }
    /// <summary>전체 제목(원문, 줄바꿈 포함) — 마우스 오버 툴팁용.</summary>
    public string TitleFull { get; private set; } = string.Empty;
    public double Progress { get; private set; }
    public string SubText { get; private set; } = string.Empty;
    public string StatusText { get; private set; } = string.Empty;
    public Brush StatusBrush { get; private set; } = Brushes.Gray;
    public Brush ProgressBrush { get; private set; } = Brushes.SteelBlue;
    public string SelectedResolution { get; private set; } = "—";
    public ImageSource? Thumbnail { get; private set; }
    public bool HasThumbnail { get; private set; }

    /// <summary>썸네일 최종 실패(FR-D1.6) — 플레이스홀더에 ⚠ 표시 + 사유 툴팁.</summary>
    public bool ThumbFailed { get; private set; }
    public string ThumbFailReason { get; private set; } = string.Empty;

    /// <summary>분석으로 얻은 해상도/품질 옵션(FR-03). Ready·실패 상태에서 카드 콤보로 선택한다.</summary>
    public IReadOnlyList<ResolutionOption> ResolutionOptions { get; private set; } = [];
    private string? _selectedFormatId;

    public ResolutionOption? SelectedOption
    {
        get => ResolutionOptions.FirstOrDefault(o => o.FormatId == _selectedFormatId);
        set
        {
            // ItemsSource 교체 중 WPF가 null을 밀어넣는 경우는 무시
            if (value is null || value.FormatId == _selectedFormatId)
                return;
            _queue.ChangeFormat(Id, value.FormatId); // 큐가 검증 후 ItemChanged → Refresh로 반영
        }
    }

    /// <summary>해상도 변경 가능 상태(다운로드 전/실패/취소)이며 옵션이 있을 때 콤보를 노출한다.</summary>
    public bool CanChangeResolution { get; private set; }

    private bool _isChecked = true;

    /// <summary>선택 일괄 다운로드 대상 여부(기본 선택됨). 셸의 "선택 받기 (N)" 버튼이 사용한다.</summary>
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

    /// <summary>콤보 대신 정적 배지를 보여줄 때(진행/완료 등).</summary>
    public bool ShowResolutionBadge { get; private set; }

    public bool CanStartItem { get; private set; }
    public bool CanPauseItem { get; private set; }
    public bool CanResumeItem { get; private set; }
    public bool CanCancelItem { get; private set; }
    public bool CanRetryItem { get; private set; }
    public bool CanRemoveItem { get; private set; }
    public bool CanOpenFolderItem { get; private set; }
    public bool HasResolution { get; private set; }

    /// <summary>진행 중(분석/다운로드/병합/일시정지) 여부 — 선택 삭제 확인 대화상자 판단용(FR-D3.4).</summary>
    public bool IsActive { get; private set; }

    /// <summary>진행률 미상 동작 구간(분석/병합/첫 진행 이벤트 전) — 마퀴 바 + 회전 스피너 표시.</summary>
    public bool IsIndeterminate { get; private set; }

    /// <summary>로그인/봇 확인으로 해결 가능한 불가 상태(FR-L2) — [로그인] 버튼 노출.</summary>
    public bool CanLoginFixItem { get; private set; }

    /// <summary>항목 원본 URL — 로그인 창 시작 주소로 사용.</summary>
    public string OriginalUrl { get; private set; } = string.Empty;

    /// <summary>[로그인] 클릭 시 Shell이 로그인 창을 열도록 하는 콜백(FR-L2) — Shell이 주입.</summary>
    public Action<DownloadItemViewModel>? LoginFixRequested { get; set; }

    /// <summary>인앱 재생 가능(완료+경로, §9). 주요 액션 [재생] 및 오버플로·더블클릭에 사용.</summary>
    public bool CanPlayItem { get; private set; }

    /// <summary>재생할 파일 경로 — 플레이어 창이 사용(완료 전 null).</summary>
    internal string? OutputPath => _outputPath;

    /// <summary>[재생] 클릭 시 Shell이 플레이어 창을 열도록 하는 콜백(§9) — Shell이 주입.</summary>
    public Action<DownloadItemViewModel>? PlayRequested { get; set; }

    public void PlayItem() => PlayRequested?.Invoke(this);

    /// <summary>외부 앱 드래그 아웃 가능(FR-DG3) — 경로 있는 완료 항목만. 파일 실재는 시작 직전 재검증.</summary>
    public bool CanDragItem { get; private set; }

    /// <summary>드래그 페이로드용 실제 파일 경로(FR-DG3) — 기록 경로가 실파일과 다르면
    /// [id] 토큰 재해석(인앱 재생과 동일 경로), 못 찾으면 null.</summary>
    internal string? GetDraggablePath() =>
        _outputPath is null ? null : ShellViewModel.ResolveMediaPath(_outputPath);

    // ── 주요 액션(FR-U3.1) — 카드에는 이 버튼 1개 + 오버플로만 노출한다 ──

    /// <summary>accent(받기/재개/재시도/로그인) · neutral(일시정지/폴더) · danger(취소/삭제) · none(Merging).</summary>
    public string PrimaryActionKind { get; private set; } = "none";

    public string PrimaryActionLabel { get; private set; } = string.Empty;
    public bool PrimaryIsAccent => PrimaryActionKind == "accent";
    public bool PrimaryIsNeutral => PrimaryActionKind == "neutral";
    public bool PrimaryIsDanger => PrimaryActionKind == "danger";
    public bool CanPrimaryAction => PrimaryActionKind != "none";

    /// <summary>오버플로 토글 툴팁 — Merging은 액션 0개(FR-U3.5)라 사유를 안내한다.</summary>
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
        if (CanOpenFolderItem) return ("neutral", "폴더 열기"); // CanPlay와 동일 조건 — 방어적 유지
        if (CanCancelItem) return ("danger", "취소");
        if (CanRemoveItem) return ("danger", "삭제");
        return ("none", string.Empty); // Merging — 중단 불가 설계(FR-U3.5)
    }

    public void LoginFixItem() => LoginFixRequested?.Invoke(this);

    /// <summary>삭제 확인 대화상자(테마 일치) — Shell이 주입. null이면 즉시 삭제.</summary>
    public Func<DownloadItemViewModel, Task<bool>>? ConfirmRemove { get; set; }

    public void Refresh(DownloadItem item)
    {
        _outputPath = item.OutputFilePath;
        Status = item.Status;
        TryLoadThumbnail(item);
        var rawTitle = string.IsNullOrWhiteSpace(item.Title) ? item.OriginalUrl : item.Title!;
        TitleFull = rawTitle;
        Title = CollapseToSingleLine(rawTitle); // 줄바꿈·연속 공백 → 단일 공백(카드 높이 기형 방지)
        Progress = item.ProgressPercent;
        // 진행률을 알 수 없는 동작 구간(분석/병합/첫 진행 이벤트 전)은 마퀴+스피너로 표시 —
        // 멈춘 것처럼 보이는 UX 방지(실사용 보고)
        IsIndeterminate = item.Status is DownloadStatus.Analyzing or DownloadStatus.Merging
            || (item.Status == DownloadStatus.Downloading && item.ProgressPercent <= 0);
        StatusText = TranslateStatus(item.Status);
        StatusBrush = StatusColor(item.Status);
        ProgressBrush = ProgressColor(item.Status);
        SubText = BuildSubText(item);
        SelectedResolution = ResolutionLabel(item);
        HasResolution = item.SelectedFormatId is not null;

        // 해상도 옵션 — Formats 참조가 바뀔 때만 재계산(진행률 Refresh 시 재생성 방지)
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
        CanPlayItem = CanOpenFolderItem; // 인앱 재생(§9) — 경로 있는 완료 항목만
        CanDragItem = CanOpenFolderItem; // 외부 앱 드래그 아웃(FR-DG3) — 동일 조건

        // 주요 액션 1개 도출(FR-U3.1) — 우선순위 체인은 시뮬 CA 116건이 검증한 매핑과 동일해야 한다
        (PrimaryActionKind, PrimaryActionLabel) = ComputePrimary();

        NotifyOfPropertyChange(string.Empty); // 모든 바인딩 갱신
    }

    // ── Caliburn 액션(카드 버튼) ──
    public void StartItem() => _queue.Start(Id);
    public void PauseItem() => _queue.Pause(Id);
    public void ResumeItem() => _queue.Resume(Id);
    public void CancelItem() => _queue.Cancel(Id);
    public void RetryItem() => _queue.Retry(Id);

    // Caliburn 코루틴은 async 예외를 삼키므로 fire-and-forget + 내부 처리(기존 다이얼로그 패턴)
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
        try
        {
            if (File.Exists(_outputPath))
                Process.Start("explorer.exe", $"/select,\"{_outputPath}\"");
            else
            {
                var dir = Path.GetDirectoryName(_outputPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    Process.Start("explorer.exe", dir);
            }
        }
        catch (Exception)
        {
            // 탐색기 실행 실패는 무시
        }
    }

    private static readonly HttpClient _http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // 일부 CDN(인스타 scontent 등)은 축약 UA·비브라우저 Accept 를 거부한다 — 완전한 브라우저 헤더 사용
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/apng,image/*");
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*; q=0.8");
        return client;
    }

    /// <summary>
    /// 썸네일 후보들을 우선순위 순으로 시도한다(FR-D1). 후보당 네트워크 재시도 N회(백오프),
    /// 매직바이트로 비이미지(HTML 차단 페이지) 거부, WebP는 WPF 디코드 시도 후 ffmpeg 변환 폴백.
    /// 전부 실패하면 ⚠ 표시 + 사유 로그(FR-D1.6).
    /// </summary>
    private void TryLoadThumbnail(DownloadItem item)
    {
        var candidates = item.ThumbnailCandidates.Count > 0
            ? item.ThumbnailCandidates
            : (string.IsNullOrWhiteSpace(item.ThumbnailPath) ? [] : new[] { item.ThumbnailPath! });

        // 완료 항목은 원격 썸네일이 없거나(복원 목록) 만료(403)돼도 로컬 파일에서 프레임을 뽑아 미리보기를 만든다.
        var localVideo = item.Status == DownloadStatus.Completed ? item.OutputFilePath : null;

        var sameCandidates = candidates.Count > 0 && ReferenceEquals(candidates, _attemptedCandidates);
        if ((candidates.Count == 0 || sameCandidates) && (string.IsNullOrEmpty(localVideo) || _localThumbTried))
            return; // 시도할 새 후보도, 미시도 로컬 폴백도 없음

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

            // 후보당 네트워크 재시도(FR-D1.4): 1 + N회, 백오프 1s→2s→4s
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
                    // 상태코드가 있으면 노출(403=차단/서명 만료, 404=삭제 — 원격 진단 근거)
                    lastReason = ex is HttpRequestException { StatusCode: not null } hre
                        ? $"다운로드 실패(HTTP {(int)hre.StatusCode!})"
                        : $"다운로드 실패({ex.GetType().Name})";
                    if (attempt < _thumbRetryCount)
                        await Task.Delay(TimeSpan.FromSeconds(1 << attempt)).ConfigureAwait(false);
                }
            }
            if (bytes is null)
                continue; // 다음 후보

            // 매직바이트 판정(FR-D1.3) — rednote 403 HTML 등 비이미지 거부
            var kind = ImageSniffer.Sniff(bytes);
            if (kind == SniffedImageKind.NotImage)
            {
                lastReason = "이미지가 아닌 응답(차단 페이지 가능)";
                continue;
            }

            // WPF 디코드 시도 → WebP는 실패 시 ffmpeg 변환 폴백(FR-D1.2)
            var bitmap = TryDecode(bytes);
            if (bitmap is null && kind == SniffedImageKind.WebP)
            {
                var png = await WebpImageConverter.ConvertToPngAsync(bytes).ConfigureAwait(false);
                if (png is not null)
                    bitmap = TryDecode(png);
                lastReason = bitmap is null ? "WebP 변환 실패" : lastReason;
            }
            if (bitmap is null)
            {
                if (kind != SniffedImageKind.WebP)
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

        // 원격 후보 전부 실패·부재 → 완료된 로컬 파일에서 프레임 추출 폴백(썸네일 만료/미상 대응)
        if (!string.IsNullOrEmpty(localVideo) && !_localThumbTried)
        {
            _localThumbTried = true;
            var resolved = ShellViewModel.ResolveMediaPath(localVideo);
            var frame = await Services.WebpImageConverter.ExtractVideoFrameAsync(resolved).ConfigureAwait(false);
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

        // 전 후보 실패(FR-D1.6) — 가시화 + 로그 1줄(NFR-D1)
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

    /// <summary>바이트를 WPF BitmapImage로 디코드(96px 다운샘플·Freeze). 실패 시 null.</summary>
    private static BitmapImage? TryDecode(byte[] bytes)
    {
        try
        {
            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(bytes))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                // 주의: StreamSource + IgnoreImageCache 조합은 EndInit에서 ArgumentNullException — 설정 금지
                bitmap.DecodePixelWidth = 96;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }
            bitmap.Freeze(); // 백그라운드 스레드 생성분을 UI 스레드에서 쓰기 위해 동결
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void OnUi(System.Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(action);
        else
            action();
    }

    // ── 표시 헬퍼 ──
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    /// <summary>제목의 줄바꿈·탭·연속 공백을 단일 공백으로 접어 한 줄로 만든다(카드 높이 기형 방지).</summary>
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

    // 라틴 이니셜 배지(FR-U1.3) — 이모지/한자(书·抖)는 CJK 폰트 미설치 환경에서 두부 문자 발생
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

    private static Brush BadgeBrush(PlatformType platform) => Frozen(platform switch
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

    private static Brush StatusColor(DownloadStatus status) => Frozen(status switch
    {
        DownloadStatus.Downloading or DownloadStatus.Merging => Color.FromRgb(0x60, 0xA5, 0xFA),
        DownloadStatus.Completed => Color.FromRgb(0x16, 0xA3, 0x4A),
        DownloadStatus.Failed => Color.FromRgb(0xDC, 0x26, 0x26),
        DownloadStatus.Unavailable => Color.FromRgb(0x8B, 0x93, 0xA1), // 회색 — 실패(빨강)와 시각 구분(FR-D2.4)
        DownloadStatus.Analyzing => Color.FromRgb(0xD9, 0x77, 0x06),
        _ => Color.FromRgb(0x9A, 0xA3, 0xB2),
    });

    private static Brush ProgressColor(DownloadStatus status) => Frozen(status switch
    {
        DownloadStatus.Completed => Color.FromRgb(0x16, 0xA3, 0x4A),
        DownloadStatus.Failed => Color.FromRgb(0xDC, 0x26, 0x26),
        _ => Color.FromRgb(0x25, 0x63, 0xEB),
    });

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

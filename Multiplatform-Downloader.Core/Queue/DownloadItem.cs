using Multiplatform_Downloader.Core.Models;
using Multiplatform_Downloader.Core.Platforms;

namespace Multiplatform_Downloader.Core.Queue;

/// <summary>
/// 다운로드 큐의 단일 항목이자 상태머신(FR-04).
/// 전이: Queued → Analyzing → Ready → Downloading ⇄ Paused → Merging → Completed/Failed/Canceled.
/// 잘못된 전이는 <see cref="InvalidOperationException"/>. 병합 중에는 일시정지할 수 없다.
/// </summary>
public sealed class DownloadItem
{
    private readonly object _gate = new();

    public DownloadItem(string originalUrl, PlatformType platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalUrl);
        OriginalUrl = originalUrl;
        Platform = platform;
    }

    public Guid Id { get; init; } = Guid.NewGuid();
    public string OriginalUrl { get; }
    public PlatformType Platform { get; }
    public string? ResolvedUrl { get; set; }
    public string? Title { get; set; }
    public string? ThumbnailPath { get; set; }
    /// <summary>썸네일 후보 URL 목록(우선순위순, FR-D1.1). VM이 순서대로 시도한다.</summary>
    public IReadOnlyList<string> ThumbnailCandidates { get; set; } = [];
    public TimeSpan? Duration { get; set; }
    public IReadOnlyList<MediaFormat> Formats { get; set; } = [];
    public string? SelectedFormatId { get; set; }
    /// <summary>yt-dlp 추출기 영상 ID(<c>%(id)s</c>). 출력 파일명의 <c>[id]</c>와 조각 파일 정리에 쓴다.</summary>
    public string? SourceId { get; set; }
    public ExtractionRoute ExtractionRoute { get; set; }
    /// <summary>샤오홍슈 폴백 경로에서 직접 받을 스트림 URL(FR-13). yt-dlp 경로에서는 null.</summary>
    public string? DirectStreamUrl { get; set; }
    public string? TemporaryOutputPath { get; set; }
    public string? OutputFilePath { get; private set; }

    public DownloadStatus Status { get; private set; } = DownloadStatus.Queued;
    public double ProgressPercent { get; private set; }
    public long? SpeedBytesPerSec { get; private set; }
    public TimeSpan? Eta { get; private set; }
    public string? ErrorMessage { get; private set; }
    public ErrorCategory? LastErrorCategory { get; private set; }
    public int RetryCount { get; private set; }

    // Ready 허용: 재시도(PrepareRetry→Ready) 후 재분석 경로(FR-D1.5)
    public void MarkAnalyzing() { lock (_gate) Require(DownloadStatus.Analyzing, DownloadStatus.Queued, DownloadStatus.Ready); }

    /// <summary>분석 자동 재시도(FR-D2.1)를 위해 Analyzing → Queued로 되돌린다.</summary>
    public void ReturnToQueued() { lock (_gate) Require(DownloadStatus.Queued, DownloadStatus.Analyzing); }

    public void MarkReady() { lock (_gate) Require(DownloadStatus.Ready, DownloadStatus.Analyzing); }

    public void Start() { lock (_gate) Require(DownloadStatus.Downloading, DownloadStatus.Ready, DownloadStatus.Paused); }

    public void Pause()
    {
        // 병합 단계는 중단할 수 없다(완료까지 진행) — PRD FR-04
        lock (_gate) Require(DownloadStatus.Paused, DownloadStatus.Downloading);
    }

    public void Resume() { lock (_gate) Require(DownloadStatus.Downloading, DownloadStatus.Paused); }

    public void MarkMerging() { lock (_gate) Require(DownloadStatus.Merging, DownloadStatus.Downloading); }

    public void UpdateProgress(DownloadProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        lock (_gate)
        {
            if (Status != DownloadStatus.Downloading)
                return; // 다운로드 중이 아닐 때의 진행 이벤트는 무시
            if (!progress.IsIndeterminate)
                ProgressPercent = progress.Percent;
            SpeedBytesPerSec = progress.SpeedBytesPerSec;
            Eta = progress.Eta;
        }
    }

    /// <summary>완료 처리. 최종 경로를 못 얻었어도(null/빈 문자열) 성공은 성공으로 기록한다(H3 방지).</summary>
    public void Complete(string? outputFilePath)
    {
        lock (_gate)
        {
            if (Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Canceled or DownloadStatus.Unavailable)
                throw new InvalidOperationException($"이미 종료된 항목은 완료 처리할 수 없습니다 (현재 {Status}).");
            Status = DownloadStatus.Completed;
            OutputFilePath = string.IsNullOrWhiteSpace(outputFilePath) ? null : outputFilePath;
            ProgressPercent = 100;
            SpeedBytesPerSec = null;
            Eta = null;
        }
    }

    public void Fail(string message, ErrorCategory category = ErrorCategory.Unknown)
    {
        lock (_gate)
        {
            // 이미 종료된(완료/취소/실패/불가) 항목은 상태를 덮어쓰지 않는다 (H5)
            if (Status is DownloadStatus.Completed or DownloadStatus.Canceled or DownloadStatus.Failed or DownloadStatus.Unavailable)
                throw new InvalidOperationException($"종료된 항목은 실패 처리할 수 없습니다 (현재 {Status}).");
            Status = DownloadStatus.Failed;
            ErrorMessage = message;
            LastErrorCategory = category;
        }
    }

    /// <summary>다운로드 불가 확정(FR-D2.4) — 재시도 소진·확정 실패. 사유는 분류기 문구.
    /// category는 실패 종류(FR-L1) — LoginRequired면 카드에 [로그인] 해결 버튼이 노출된다.</summary>
    public void MarkUnavailable(string reason, ErrorCategory? category = null)
    {
        lock (_gate)
        {
            if (Status is DownloadStatus.Completed or DownloadStatus.Canceled or DownloadStatus.Unavailable)
                throw new InvalidOperationException($"종료된 항목은 불가 처리할 수 없습니다 (현재 {Status}).");
            Status = DownloadStatus.Unavailable;
            ErrorMessage = reason;
            LastErrorCategory = category;
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Canceled or DownloadStatus.Unavailable)
                throw new InvalidOperationException($"종료된 항목은 취소할 수 없습니다 (현재 {Status}).");
            Status = DownloadStatus.Canceled;
        }
    }

    /// <summary>실패·취소·불가 항목을 재시도 대기(Ready)로 되돌린다. 재시도 횟수 증가.</summary>
    public void PrepareRetry()
    {
        lock (_gate)
        {
            if (Status is not (DownloadStatus.Failed or DownloadStatus.Canceled or DownloadStatus.Unavailable))
                throw new InvalidOperationException($"재시도는 실패/취소/불가 항목만 가능합니다 (현재 {Status}).");
            RetryCount++;
            ErrorMessage = null;
            LastErrorCategory = null;
            ProgressPercent = 0;
            Status = DownloadStatus.Ready;
        }
    }

    // _gate 안에서만 호출한다.
    private void Require(DownloadStatus target, params DownloadStatus[] allowedFrom)
    {
        if (Array.IndexOf(allowedFrom, Status) < 0)
            throw new InvalidOperationException(
                $"{target}(으)로 전이할 수 없습니다. 허용된 이전 상태: {string.Join("/", allowedFrom)}, 현재: {Status}.");
        Status = target;
    }
}

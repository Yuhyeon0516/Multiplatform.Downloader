namespace Multiplatform_Downloader.Core.Queue;

/// <summary>다운로드 항목 상태머신(FR-04). 전이 규칙은 <see cref="DownloadItem"/>이 강제한다.</summary>
public enum DownloadStatus
{
    Queued,
    Analyzing,
    Ready,
    Downloading,
    Paused,
    Merging,
    Completed,
    Failed,
    Canceled,
    /// <summary>다운로드 불가 확정(FR-D2.4) — 재시도 소진 또는 확정 실패(링크 만료·삭제·미지원 등).</summary>
    Unavailable,
}

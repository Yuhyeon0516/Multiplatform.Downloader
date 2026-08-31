namespace Multiplatform_Downloader.Avalonia.ViewModels;

/// <summary>큐 상태 필터(FR-U2.2). 버킷 매핑은 WPF 헤드와 동일(PRD FR-U2.3).</summary>
public enum QueueFilter
{
    All,

    /// <summary>진행 — Downloading + Merging</summary>
    Active,

    /// <summary>대기 — Queued + Analyzing + Ready + Paused</summary>
    Waiting,

    /// <summary>완료 — Completed</summary>
    Done,

    /// <summary>실패 — Failed + Canceled + Unavailable</summary>
    Failed,
}

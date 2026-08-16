using Multiplatform_Downloader.Core.Platforms;

namespace Multiplatform_Downloader.Core.Queue;

/// <summary>재시작 복원용 항목 스냅샷(FR-11). v2: 완료 항목도 저장 — <see cref="OutputFilePath"/>로
/// 복원 시 다운로드 폴더의 파일 존재를 대조한다(받음/안받음 구분, 사용자 요청).</summary>
public sealed record QueueItemSnapshot(
    Guid Id,
    string OriginalUrl,
    PlatformType Platform,
    string? ResolvedUrl,
    string? Title,
    string? ThumbnailPath,
    string? SelectedFormatId,
    DownloadStatus Status,
    ExtractionRoute ExtractionRoute,
    string? OutputFilePath = null);

/// <summary>queue-state.json 최상위. <see cref="SchemaVersion"/>으로 손상·마이그레이션을 판별한다(NFR-16).</summary>
public sealed record QueueSnapshot(int SchemaVersion, IReadOnlyList<QueueItemSnapshot> Items);

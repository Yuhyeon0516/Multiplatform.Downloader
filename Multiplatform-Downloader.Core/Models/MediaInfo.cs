namespace Multiplatform_Downloader.Core.Models;

/// <summary>yt-dlp <c>-J</c> 메타데이터 조회 결과. (FR-02)</summary>
public sealed record MediaInfo
{
    public string? Id { get; init; }
    public string? Title { get; init; }
    /// <summary>대표 썸네일(= <see cref="ThumbnailUrls"/>의 첫 항목). 하위 호환용.</summary>
    public string? ThumbnailUrl { get; init; }
    /// <summary>썸네일 후보 URL(우선순위 내림차순, FR-D1.1) — 확장자와 무관하게 크기·선호도 순.</summary>
    public IReadOnlyList<string> ThumbnailUrls { get; init; } = [];
    public TimeSpan? Duration { get; init; }
    public IReadOnlyList<MediaFormat> Formats { get; init; } = [];
}

namespace Multiplatform_Downloader.Core.Models;

/// <summary>yt-dlp가 보고하는 개별 스트림 포맷. (FR-02)</summary>
public sealed record MediaFormat
{
    public required string FormatId { get; init; }
    public string? Ext { get; init; }
    public int? Height { get; init; }
    public int? Width { get; init; }
    public double? Fps { get; init; }
    public string? VideoCodec { get; init; }
    public string? AudioCodec { get; init; }
    public long? ApproxSize { get; init; }

    /// <summary>영상 없이 오디오만 있는 포맷.</summary>
    public bool IsAudioOnly { get; init; }

    /// <summary>오디오 없이 영상만 있는 포맷(병합 필요).</summary>
    public bool IsVideoOnly { get; init; }
}

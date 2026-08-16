using Multiplatform_Downloader.Core.Models;

namespace Multiplatform_Downloader.Core.Engine;

/// <summary>URL에서 제목·썸네일·해상도 목록 등 메타데이터를 조회한다(FR-02).</summary>
public interface IMediaMetadataService
{
    /// <exception cref="MetadataFetchException">엔진 실패·타임아웃·파싱 오류.</exception>
    /// <exception cref="OperationCanceledException">호출자가 취소했을 때.</exception>
    Task<MediaInfo> FetchAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>메타데이터 조회 실패(엔진 오류·타임아웃·파싱). <see cref="Failure"/>에 분류 결과(FR-D2.2)를 싣는다.</summary>
public sealed class MetadataFetchException : Exception
{
    public MetadataFetchException(string message) : base(message) { }
    public MetadataFetchException(string message, Exception inner) : base(message, inner) { }

    public MetadataFetchException(ExtractionFailure failure) : base(failure.UserMessage)
    {
        Failure = failure;
    }

    /// <summary>stderr 분류 결과. 타임아웃·파싱 실패 등 분류 불가 경로에서는 null.</summary>
    public ExtractionFailure? Failure { get; }
}

using Multiplatform_Downloader.Core.Models;
using Multiplatform_Downloader.Core.Queue;

namespace Multiplatform_Downloader.Core.Engine;

/// <summary>추출 결과. <see cref="DirectStreamUrl"/>이 있으면 폴백 경로로 스트림을 직접 받아야 한다.</summary>
public sealed record XhsResolution(ExtractionRoute Route, MediaInfo Info, string? DirectStreamUrl);

/// <summary>샤오홍슈 해상도 결정 전략(FR-13). 큐가 XHS 항목 분석에 사용한다.</summary>
public interface IXhsResolutionStrategy
{
    /// <exception cref="XhsExtractionException">yt-dlp·자체 추출 모두 실패할 때.</exception>
    Task<XhsResolution> ResolveAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>
/// 샤오홍슈 다층 폴백(FR-13): ① yt-dlp → ② 자체 추출기.
/// yt-dlp가 실패하거나 포맷이 없으면 페이지 HTML을 받아 <see cref="XhsFallbackExtractor"/>로 직접 파싱한다.
/// ⚠ 두 경로가 같은 페이지 JSON에 의존하므로 구조 변경·차단 시 동시 실패할 수 있다(PRD P0-02, VER-08에서 검증).
/// </summary>
public sealed class XhsResolutionStrategy : IXhsResolutionStrategy
{
    private readonly IMediaMetadataService _ytDlpMetadata;
    private readonly XhsFallbackExtractor _fallbackExtractor;
    private readonly Func<string, CancellationToken, Task<string>> _fetchHtml;

    public XhsResolutionStrategy(
        IMediaMetadataService ytDlpMetadata,
        XhsFallbackExtractor fallbackExtractor,
        Func<string, CancellationToken, Task<string>> fetchHtml)
    {
        _ytDlpMetadata = ytDlpMetadata ?? throw new ArgumentNullException(nameof(ytDlpMetadata));
        _fallbackExtractor = fallbackExtractor ?? throw new ArgumentNullException(nameof(fallbackExtractor));
        _fetchHtml = fetchHtml ?? throw new ArgumentNullException(nameof(fetchHtml));
    }

    /// <exception cref="XhsExtractionException">yt-dlp·자체 추출 모두 실패할 때.</exception>
    public async Task<XhsResolution> ResolveAsync(string url, CancellationToken cancellationToken = default)
    {
        // 1차: yt-dlp
        try
        {
            var info = await _ytDlpMetadata.FetchAsync(url, cancellationToken).ConfigureAwait(false);
            if (info.Formats.Count > 0)
                return new XhsResolution(ExtractionRoute.YtDlp, info, DirectStreamUrl: null);
        }
        catch (MetadataFetchException)
        {
            // 폴백으로 진행
        }

        // 2차: 자체 추출기 (다른 실패 모드를 구제하는지 VER-08에서 입증)
        string html;
        try
        {
            html = await _fetchHtml(url, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 문서화된 계약대로 XhsExtractionException으로 정규화(M4)
            throw new XhsExtractionException($"샤오홍슈 페이지 로드 실패: {ex.Message}");
        }

        var extract = _fallbackExtractor.Extract(html);
        var fallbackInfo = new MediaInfo
        {
            Title = extract.Title,
            ThumbnailUrl = extract.ThumbnailUrl,
            ThumbnailUrls = extract.ThumbnailUrl is null ? [] : [extract.ThumbnailUrl],
            Formats = [],
        };
        return new XhsResolution(ExtractionRoute.XhsFallback, fallbackInfo, extract.StreamUrl);
    }
}

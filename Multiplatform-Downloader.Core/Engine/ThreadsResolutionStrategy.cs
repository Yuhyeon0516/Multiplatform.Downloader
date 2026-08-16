using Multiplatform_Downloader.Core.Models;
using Multiplatform_Downloader.Core.Queue;

namespace Multiplatform_Downloader.Core.Engine;

/// <summary>Threads 해상도 결정 전략(FR-N1.8). 큐가 Threads 항목 분석에 사용한다.</summary>
public interface IThreadsResolutionStrategy
{
    /// <exception cref="ThreadsExtractionException">HTML 로드·추출 실패 시.</exception>
    Task<XhsResolution> ResolveAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>
/// Threads 폴백 추출(FR-N1.8): yt-dlp 익스트랙터가 없으므로 곧바로 자체 추출한다.
/// 게시물 HTML(완전한 브라우저 헤더 필요 — 주입된 fetchHtml이 헤더를 세팅)을 받아
/// <see cref="ThreadsFallbackExtractor"/>로 video_versions 스트림 URL을 파싱하고
/// 직접 다운로드 경로(<see cref="ExtractionRoute.XhsFallback"/> = 직접 스트림)로 반환한다.
/// </summary>
public sealed class ThreadsResolutionStrategy : IThreadsResolutionStrategy
{
    private readonly ThreadsFallbackExtractor _extractor;
    private readonly Func<string, CancellationToken, Task<string>> _fetchHtml;

    public ThreadsResolutionStrategy(
        ThreadsFallbackExtractor extractor,
        Func<string, CancellationToken, Task<string>> fetchHtml)
    {
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _fetchHtml = fetchHtml ?? throw new ArgumentNullException(nameof(fetchHtml));
    }

    public async Task<XhsResolution> ResolveAsync(string url, CancellationToken cancellationToken = default)
    {
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
            throw new ThreadsExtractionException($"Threads 페이지 로드 실패: {ex.Message}");
        }

        var extract = _extractor.Extract(html);
        var info = new MediaInfo
        {
            Title = extract.Title,
            ThumbnailUrl = extract.ThumbnailUrl,
            ThumbnailUrls = extract.ThumbnailUrl is null ? [] : [extract.ThumbnailUrl],
            Formats = [],
        };
        return new XhsResolution(ExtractionRoute.XhsFallback, info, extract.StreamUrl);
    }
}

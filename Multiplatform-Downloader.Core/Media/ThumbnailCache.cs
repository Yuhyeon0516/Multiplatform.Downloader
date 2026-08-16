using System.Security.Cryptography;
using System.Text;

namespace Multiplatform_Downloader.Core.Media;

/// <summary>
/// 썸네일을 로컬에 캐시한다(FR-07). 같은 URL은 재다운로드하지 않으며, 실패 시 null(플레이스홀더)을 반환한다.
/// 캐시 키는 SHA-256(보안 목적 아님, 파일명 안전용)으로 생성한다.
/// </summary>
public sealed class ThumbnailCache : IDisposable
{
    private readonly string _cacheDirectory;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHandler;

    public ThumbnailCache(string cacheDirectory, HttpMessageHandler? handler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        _cacheDirectory = cacheDirectory;
        Directory.CreateDirectory(cacheDirectory);
        _ownsHandler = handler is null;
        _httpClient = new HttpClient(handler ?? new SocketsHttpHandler(), disposeHandler: _ownsHandler);
    }

    /// <summary>캐시에 있으면 즉시 경로를, 없으면 다운로드해 저장 후 경로를 반환한다. 실패 시 null.</summary>
    public async Task<string?> GetOrDownloadAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var path = Path.Combine(_cacheDirectory, CacheKey(url) + ".img");
        if (File.Exists(path))
            return path;

        try
        {
            var bytes = await _httpClient.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
            var tempPath = path + ".tmp";
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return null; // 실패 시 플레이스홀더 사용
        }
    }

    private static string CacheKey(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose() => _httpClient.Dispose();
}

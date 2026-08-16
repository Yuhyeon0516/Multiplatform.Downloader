using Multiplatform_Downloader.Core.Models;
using Multiplatform_Downloader.Core.Net;

namespace Multiplatform_Downloader.Core.Engine;

/// <summary>폴백 경로에서 스트림 URL을 직접 파일로 다운로드한다(FR-13).</summary>
public interface IDirectStreamDownloader
{
    Task DownloadAsync(string streamUrl, string outputPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>폴백 경로에서 스트림 URL을 직접 다운로드한다(FR-13). SSRF 방어(NFR-10)·진행률·리소스 정리(NFR-07).</summary>
public sealed class DirectStreamDownloader : IDirectStreamDownloader, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHandler;

    public DirectStreamDownloader(HttpMessageHandler? handler = null)
    {
        _ownsHandler = handler is null;
        // 리다이렉트를 허용하되 각 연결 IP는 ConnectCallback이 검증한다
        _httpClient = new HttpClient(handler ?? SsrfGuard.CreateGuardedHandler(allowAutoRedirect: true), disposeHandler: _ownsHandler);
    }

    public async Task DownloadAsync(
        string streamUrl,
        string outputPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        // 제3자 페이지에서 파싱한 스트림 URL이므로 스킴·호스트를 먼저 검증(M5)
        if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out var uri))
            throw new ArgumentException("유효하지 않은 스트림 URL입니다.", nameof(streamUrl));
        SsrfGuard.EnsureSafe(uri);

        using var response = await _httpClient
            .GetAsync(streamUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(outputPath);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            downloaded += read;
            if (total is > 0)
                progress?.Report(new DownloadProgress(downloaded * 100.0 / total.Value, null, null));
        }
    }

    public void Dispose() => _httpClient.Dispose();
}

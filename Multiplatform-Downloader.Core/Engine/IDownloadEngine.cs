using Multiplatform_Downloader.Core.Models;

namespace Multiplatform_Downloader.Core.Engine;

/// <summary>선택 포맷으로 다운로드를 실행한다(FR-04). 취소 시 프로세스가 종료된다(일시정지·취소에 사용).</summary>
public interface IDownloadEngine
{
    Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <param name="Url">정규화 완료된 URL.</param>
/// <param name="FormatId">yt-dlp 포맷 선택자(예: "137+140" 또는 "best").</param>
/// <param name="OutputTemplate">yt-dlp 출력 템플릿(-o).</param>
/// <param name="Continue">부분 파일 이어받기(-c). 재개 시 true.</param>
/// <param name="CookieFile">cookies.txt 경로(<c>--cookies</c>). <paramref name="CookieFromBrowser"/>이 있으면 무시.</param>
/// <param name="CookieFromBrowser">브라우저 쿠키 이름(chrome/edge/firefox, <c>--cookies-from-browser</c>).</param>
public sealed record DownloadRequest(
    string Url,
    string FormatId,
    string OutputTemplate,
    bool Continue = false,
    string? ProxyUrl = null,
    string? CookieFile = null,
    string? CookieFromBrowser = null);

public sealed record DownloadResult(bool Success, string? OutputFilePath, int ExitCode, string? ErrorLine = null);

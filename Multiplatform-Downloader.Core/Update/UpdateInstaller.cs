using System.Diagnostics;
using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Models;
using Multiplatform_Downloader.Core.Net;

namespace Multiplatform_Downloader.Core.Update;

/// <summary>인스톨러 다운로드·검증 결과.</summary>
public sealed record UpdateDownloadResult(bool Success, string? InstallerPath, string? Error)
{
    public static UpdateDownloadResult Ok(string path) => new(true, path, null);
    public static UpdateDownloadResult Fail(string error) => new(false, null, error);
}

/// <summary>인스톨러를 내려받아 검증한다(FR-U3). SSRF·호스트 allowlist·HTTPS·크기·버전 검증.</summary>
public interface IUpdateInstaller
{
    Task<UpdateDownloadResult> DownloadAsync(UpdateInfo info, Version currentVersion, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// GitHub 릴리스 자산(인스톨러)을 스트리밍 다운로드하고 무결성·authenticity 대체 검증을 수행한다(FR-U3).
/// 코드 서명 부재를 보완하는 방어: 호스트 allowlist(리다이렉트 hop 포함) + HTTPS 강제 + 크기 대조 +
/// 다운로드 exe의 FileVersion 교차 검증(광고 버전 일치 + 현재보다 상위) + 디스크 사전 검사.
/// </summary>
public sealed class UpdateInstaller : IUpdateInstaller, IDisposable
{
    // 호스트 화이트리스트 (FR-U3.2). 적용 범위: 시작 URL + 리다이렉트 최종 URL의 호스트명.
    // 중간 hop의 IP(사설/예약망)는 SsrfGuard.ConnectCallback이 매 hop 검증하나, 중간 hop의
    // '호스트명'까지 allowlist로 강제하려면 커스텀 리다이렉트 핸들러가 필요(현재 미구현, M2).
    private static readonly string[] AllowedHosts =
    [
        "github.com",
        "release-assets.githubusercontent.com",
        "objects.githubusercontent.com",
    ];

    private const long DiskMarginBytes = 512L * 1024 * 1024; // 인스톨러 압축해제분 여유 (FR-U3.5)

    private readonly HttpClient _http;
    private readonly bool _ownsHandler;
    private readonly IAppLogger _logger;
    private readonly string _updatesFolder;

    public UpdateInstaller(IAppLogger? logger = null, string? updatesFolder = null, HttpMessageHandler? handler = null)
    {
        _logger = logger ?? NullAppLogger.Instance;
        _updatesFolder = updatesFolder ?? DefaultUpdatesFolder;
        _ownsHandler = handler is null;
        // 가드 핸들러(사설 IP 차단) + 자동 리다이렉트. hop 호스트는 아래에서 별도 검증한다.
        _http = new HttpClient(handler ?? SsrfGuard.CreateGuardedHandler(allowAutoRedirect: true), disposeHandler: _ownsHandler);
    }

    public static string DefaultUpdatesFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Multiplatform-Downloader",
        "Updates");

    public async Task<UpdateDownloadResult> DownloadAsync(
        UpdateInfo info,
        Version currentVersion,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 1) URL 검증 — HTTPS 강제 + 호스트 allowlist (FR-U3.2)
        if (!Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out var uri))
            return UpdateDownloadResult.Fail("유효하지 않은 다운로드 URL");
        if (!IsAllowed(uri))
            return UpdateDownloadResult.Fail("허용되지 않은 다운로드 호스트");

        // 2) 파일명은 파싱된 버전으로 고정 템플릿 생성 — 원시 자산명을 경로에 결합하지 않는다 (FR-U3.3)
        Directory.CreateDirectory(_updatesFolder);
        var fileName = $"ShyshyroongDownloader_Setup_v{info.Version}.exe";
        var finalPath = Path.Combine(_updatesFolder, fileName);
        var tempPath = finalPath + ".tmp";

        // 3) 디스크 사전 검사 (FR-U3.5)
        if (!HasEnoughDisk(_updatesFolder, info.AssetSize, out var avail))
            return UpdateDownloadResult.Fail(
                $"디스크 공간 부족 — 필요 {Mb(info.AssetSize + DiskMarginBytes)}MB, 가용 {Mb(avail)}MB");

        try
        {
            using var response = await _http
                .GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            // 리다이렉트가 자동 추종된 뒤 최종 응답 URL의 호스트도 재검증 (FR-U3.2)
            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is not null && !IsAllowed(finalUri))
                return UpdateDownloadResult.Fail("리다이렉트 대상 호스트가 허용 목록 밖");

            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? info.AssetSize;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var dest = File.Create(tempPath))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await dest.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    downloaded += read;
                    if (total > 0)
                        progress?.Report(new DownloadProgress(downloaded * 100.0 / total, null, null));
                }
            }

            // 4) 크기 대조 — 절단 검출(무결성). authenticity 보증 아님(FR-U3, 주석 명시)
            var actual = new FileInfo(tempPath).Length;
            if (actual != info.AssetSize)
            {
                SafeDelete(tempPath);
                return UpdateDownloadResult.Fail($"파일 크기 불일치(수신 {actual} / 기대 {info.AssetSize})");
            }

            // 5) 원자적 rename
            File.Move(tempPath, finalPath, overwrite: true);

            // 6) 실행 직전 버전 교차 검증 — 광고 버전 일치 + 현재보다 상위 (FR-U3.4, 롤백 방지)
            var verify = VerifyInstaller(finalPath, info.Version, currentVersion);
            if (!verify.Success)
            {
                SafeDelete(finalPath);
                return verify;
            }

            _logger.Info("Update", $"다운로드·검증 완료: {finalPath}");
            return UpdateDownloadResult.Ok(finalPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SafeDelete(tempPath); // 취소는 정상 흐름 — 오류 로깅 안 함 (FR-U3.6)
            throw;
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) == 0x70) // ERROR_DISK_FULL
        {
            SafeDelete(tempPath);
            return UpdateDownloadResult.Fail("다운로드 중 디스크 공간 부족");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or SsrfBlockedException)
        {
            SafeDelete(tempPath);
            _logger.Warning("Update", $"다운로드 실패: {ex.Message}");
            return UpdateDownloadResult.Fail("다운로드에 실패했습니다");
        }
    }

    /// <summary>다운로드된 인스톨러의 FileVersion을 검증한다(FR-U3.4). public — 캐시 히트 시에도 실행 직전 재검증.</summary>
    public UpdateDownloadResult VerifyInstaller(string path, Version advertised, Version currentVersion)
    {
        if (!File.Exists(path))
            return UpdateDownloadResult.Fail("설치 파일이 없습니다");
        FileVersionInfo fvi;
        try { fvi = FileVersionInfo.GetVersionInfo(path); }
        catch (Exception ex) when (ex is IOException or FileNotFoundException)
        { return UpdateDownloadResult.Fail("버전 정보를 읽을 수 없습니다"); }

        if (!Version.TryParse(fvi.FileVersion, out var fileVer))
            return UpdateDownloadResult.Fail("설치 파일 버전 형식 오류");

        // 광고 버전과 일치(정규화)
        if (VersionComparer.Compare(fileVer, advertised) != 0)
            return UpdateDownloadResult.Fail(
                $"버전 불일치(파일 {VersionComparer.Normalize(fileVer)} / 광고 {advertised}) — 실행 거부");
        // 현재보다 엄격히 상위(롤백 방지)
        if (!VersionComparer.IsNewer(fileVer, currentVersion))
            return UpdateDownloadResult.Fail(
                $"현재보다 상위 버전이 아님(파일 {VersionComparer.Normalize(fileVer)} / 현재 {VersionComparer.Normalize(currentVersion)}) — 실행 거부");

        return UpdateDownloadResult.Ok(path);
    }

    /// <summary>이미 받아둔 유효한 설치본 경로(캐시). 실행 직전 검증은 호출부가 VerifyInstaller로 재수행.</summary>
    public string? FindCached(Version version)
    {
        var path = Path.Combine(_updatesFolder, $"ShyshyroongDownloader_Setup_v{version}.exe");
        return File.Exists(path) ? path : null;
    }

    /// <summary>고아 .tmp·구버전 설치본을 정리한다(기동 시 호출).</summary>
    public void SweepStaleDownloads(Version currentVersion)
    {
        try
        {
            if (!Directory.Exists(_updatesFolder))
                return;
            foreach (var f in Directory.EnumerateFiles(_updatesFolder))
            {
                var name = Path.GetFileName(f);
                if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                {
                    SafeDelete(f);
                    continue;
                }
                // 현재 버전 이하 설치본은 필요 없음
                if (TryExtractVersion(name, out var v) && VersionComparer.Compare(v, currentVersion) <= 0)
                    SafeDelete(f);
            }
        }
        catch { /* best-effort */ }
    }

    private static bool TryExtractVersion(string fileName, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        var start = fileName.IndexOf("_v", StringComparison.Ordinal);
        if (start < 0) return false;
        var tag = fileName[(start + 2)..].Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        return VersionComparer.TryParseTag(tag, out version);
    }

    private static bool IsAllowed(Uri uri)
        => uri.Scheme == Uri.UriSchemeHttps
            && Array.Exists(AllowedHosts, h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase));

    private static bool HasEnoughDisk(string folder, long assetSize, out long available)
    {
        available = 0;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (string.IsNullOrEmpty(root)) return true; // 판정 불가 시 시도 허용
            var drive = new DriveInfo(root);
            available = drive.AvailableFreeSpace;
            return available >= assetSize + DiskMarginBytes;
        }
        catch { return true; } // DriveInfo 실패 시 다운로드가 실제 실패로 걸러짐
    }

    private static long Mb(long bytes) => bytes / (1024 * 1024);

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    public void Dispose() => _http.Dispose();
}

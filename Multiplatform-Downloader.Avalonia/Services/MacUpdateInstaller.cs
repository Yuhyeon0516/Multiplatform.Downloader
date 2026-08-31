using System.Diagnostics;
using System.Security.Cryptography;
using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Models;
using Multiplatform_Downloader.Core.Update;

namespace Multiplatform_Downloader.Avalonia.Services;

/// <summary>업데이트 패키지 다운로드·검증 추상화 — UpdateViewModel이 사용.
/// Windows(Core UpdateInstaller: PE FileVersion 검증)와 macOS(tar.gz + SHA256)를 갈아끼운다.</summary>
public interface IUpdatePackageProvider
{
    /// <summary>캐시된 유효 패키지 경로(검증 통과) — 없으면 null.</summary>
    string? FindCachedVerified(UpdateInfo info, Version currentVersion);

    Task<UpdateDownloadResult> DownloadAsync(UpdateInfo info, Version currentVersion,
        IProgress<DownloadProgress> progress, CancellationToken cancellationToken);
}

/// <summary>
/// macOS 자동 업데이트 설치기(FR-U5의 macOS 분기).
/// tar.gz 다운로드 → 같은 릴리스의 .sha256 자산으로 무결성 검증 → .app 번들 교체 → open 재실행.
/// 앱이 직접 받으므로 quarantine이 붙지 않아 Gatekeeper 경고 없이 갱신된다.
/// </summary>
public sealed class MacUpdateInstaller : IUpdatePackageProvider, IDisposable
{
    // Core UpdateInstaller와 동일한 호스트 허용 목록(HTTPS 전용)
    private static readonly string[] AllowedHosts =
        ["github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com"];

    private readonly IAppLogger _logger;
    private readonly HttpClient _http;

    public MacUpdateInstaller(IAppLogger logger)
    {
        _logger = logger;
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Shyshyroong-Downloader-Updater");
        _http.Timeout = TimeSpan.FromMinutes(10);
    }

    private static string UpdatesDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Multiplatform-Downloader", "Updates");

    private static string LocalPath(UpdateInfo info) =>
        Path.Combine(UpdatesDir, $"v{info.Version}-{info.AssetName}");

    public string? FindCachedVerified(UpdateInfo info, Version currentVersion)
    {
        var path = LocalPath(info);
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length != info.AssetSize)
                return null;
            // 캐시 재검증은 크기 대조까지만 — SHA는 다운로드 직후 1회 검증되며 파일명에 버전이 박혀 있다
            return path;
        }
        catch
        {
            return null;
        }
    }

    public async Task<UpdateDownloadResult> DownloadAsync(UpdateInfo info, Version currentVersion,
        IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        try
        {
            if (!VersionComparer.IsNewer(info.Version, currentVersion))
                return UpdateDownloadResult.Fail("이미 최신 버전입니다.");
            if (!IsAllowedUrl(info.DownloadUrl) || info.ChecksumUrl is null || !IsAllowedUrl(info.ChecksumUrl))
                return UpdateDownloadResult.Fail("허용되지 않은 다운로드 주소입니다.");

            Directory.CreateDirectory(UpdatesDir);
            var target = LocalPath(info);
            var tmp = target + ".part";

            // 1) 체크섬 먼저(작음) — "<64자리 hex>  <파일명>" 또는 hex 단독 형식 허용
            var checksumText = await _http.GetStringAsync(info.ChecksumUrl, cancellationToken).ConfigureAwait(false);
            var expectedHash = checksumText.Trim().Split(' ', '\t')[0].Trim().ToLowerInvariant();
            if (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
                return UpdateDownloadResult.Fail("체크섬 형식이 올바르지 않습니다.");

            // 2) 본체 스트리밍 다운로드 + 진행률
            using (var response = await _http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri is { } finalUri && !IsAllowedUri(finalUri))
                    return UpdateDownloadResult.Fail("리다이렉트가 허용되지 않은 주소로 이동했습니다.");

                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var file = File.Create(tmp);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    total += read;
                    if (info.AssetSize > 0)
                        progress.Report(new DownloadProgress(Math.Min(100.0, total * 100.0 / info.AssetSize), null, null));
                }
            }

            // 3) 크기 + SHA256 검증
            if (info.AssetSize > 0 && new FileInfo(tmp).Length != info.AssetSize)
            {
                File.Delete(tmp);
                return UpdateDownloadResult.Fail("다운로드 크기가 릴리스 정보와 다릅니다.");
            }
            var actualHash = await ComputeSha256Async(tmp, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tmp);
                _logger.Warning("Update", $"SHA256 불일치 — 기대 {expectedHash[..12]}… 실제 {actualHash[..12]}…");
                return UpdateDownloadResult.Fail("무결성 검증(SHA256)에 실패했습니다.");
            }

            File.Move(tmp, target, overwrite: true);
            _logger.Info("Update", $"업데이트 패키지 검증 완료: {target}");
            return UpdateDownloadResult.Ok(target);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning("Update", $"다운로드 실패: {ex.GetType().Name} {ex.Message}");
            return UpdateDownloadResult.Fail("다운로드에 실패했습니다.");
        }
    }

    /// <summary>검증된 tar.gz를 풀어 실행 중인 .app 번들을 교체하고 재실행을 예약한다.
    /// 반환: 교체·재실행 예약 성공 여부(true면 호출측이 앱을 종료해야 한다).</summary>
    public bool InstallAndScheduleRelaunch(string archivePath)
    {
        try
        {
            var bundle = FindCurrentBundle();
            if (bundle is null)
            {
                _logger.Warning("Update", ".app 번들 밖에서 실행 중 — 자동 설치 불가(dotnet run 등)");
                return false;
            }

            var extractDir = Path.Combine(Path.GetTempPath(), $"mpdl-update-{Guid.NewGuid():N}");
            Directory.CreateDirectory(extractDir);
            if (RunSync("/usr/bin/tar", "xzf", archivePath, "-C", extractDir) != 0)
            {
                _logger.Warning("Update", "압축 해제 실패");
                return false;
            }

            var newApp = Directory.EnumerateDirectories(extractDir, "*.app").FirstOrDefault();
            if (newApp is null)
            {
                _logger.Warning("Update", "패키지에 .app 번들이 없습니다");
                return false;
            }

            // 교체: 기존 번들을 옆으로 치우고 새 번들을 자리에 — 실행 중 파일은 inode로 열려 있어 안전
            var old = bundle + $".old-{Environment.ProcessId}";
            Directory.Move(bundle, old);
            try
            {
                Directory.Move(newApp, bundle);
            }
            catch
            {
                Directory.Move(old, bundle); // 롤백
                throw;
            }
            try { Directory.Delete(old, recursive: true); } catch { /* 다음 실행에서 정리 */ }

            // 재실행 — -n 새 인스턴스, 현재 프로세스 종료 후 뜨도록 open은 즉시 반환된다
            RunSync("/usr/bin/open", "-n", bundle);
            _logger.Info("Update", $"번들 교체 완료 — 재실행: {bundle}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning("Update", $"설치 실패: {ex.GetType().Name} {ex.Message}");
            return false;
        }
    }

    private static string? FindCurrentBundle()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath);
        while (!string.IsNullOrEmpty(dir))
        {
            if (dir.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static int RunSync(string cmd, params string[] args)
    {
        var psi = new ProcessStartInfo(cmd) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        if (p is null)
            return -1;
        p.WaitForExit();
        return p.ExitCode;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static bool IsAllowedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsAllowedUri(uri);

    private static bool IsAllowedUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

    public void Dispose() => _http.Dispose();
}

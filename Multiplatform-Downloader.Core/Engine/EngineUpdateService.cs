using System.Security.Cryptography;

namespace Multiplatform_Downloader.Core.Engine;

/// <param name="DownloadUrl">새 바이너리 URL.</param>
/// <param name="ExpectedSha256">기대 체크섬(hex).</param>
/// <param name="TargetPath">교체 대상 파일 경로.</param>
public sealed record EngineUpdateRequest(string DownloadUrl, string ExpectedSha256, string TargetPath);

public sealed record EngineUpdateResult(bool Success, string? Error)
{
    public static EngineUpdateResult Ok() => new(true, null);
    public static EngineUpdateResult Failed(string error) => new(false, error);
}

/// <summary>
/// 엔진 바이너리를 안전하게 교체한다(P0-04·NFR-16): 다운로드 → 체크섬 검증 → 원자적 교체 → 실패 시 롤백.
/// 단일 실행 잠금으로 동시 업데이트를 직렬화한다. 실제 다운로드는 주입된 fetcher로 수행(테스트 가능).
/// </summary>
public sealed class EngineUpdateService : IDisposable
{
    private readonly Func<string, CancellationToken, Task<byte[]>> _fetch;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public EngineUpdateService(Func<string, CancellationToken, Task<byte[]>> fetch)
    {
        _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
    }

    public async Task<EngineUpdateResult> UpdateAsync(EngineUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _updateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var tempPath = request.TargetPath + ".new";
        var backupPath = request.TargetPath + ".bak";
        try
        {
            byte[] bytes;
            try
            {
                bytes = await _fetch(request.DownloadUrl, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return EngineUpdateResult.Failed($"다운로드 실패: {ex.Message}");
            }

            var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!actualHash.Equals(request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                return EngineUpdateResult.Failed("체크섬 불일치 — 교체를 중단했습니다.");

            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);

            var hadExisting = File.Exists(request.TargetPath);
            try
            {
                if (hadExisting)
                    File.Move(request.TargetPath, backupPath, overwrite: true);
                File.Move(tempPath, request.TargetPath, overwrite: true);
            }
            catch (Exception ex)
            {
                // 롤백 — 백업이 있고 대상이 비어있으면 복원. 예외를 던지지 않고 Failed 반환(M2/M3)
                if (hadExisting && File.Exists(backupPath) && !File.Exists(request.TargetPath))
                {
                    try { File.Move(backupPath, request.TargetPath, overwrite: true); } catch { /* 롤백 실패 무시 */ }
                }
                return EngineUpdateResult.Failed($"교체 실패(롤백 시도됨): {ex.Message}");
            }

            TryDelete(backupPath);
            return EngineUpdateResult.Ok();
        }
        catch (OperationCanceledException)
        {
            throw; // 취소는 그대로 전파
        }
        catch (Exception ex)
        {
            return EngineUpdateResult.Failed(ex.Message);
        }
        finally
        {
            TryDelete(tempPath); // 성공 시 이미 이동됨(없음), 실패 시 임시 파일 정리(M2/M3)
            _updateLock.Release();
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 무시 */ }
    }

    public void Dispose() => _updateLock.Dispose();
}

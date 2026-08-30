using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Multiplatform_Downloader.Services;

/// <summary>
/// WebP 바이트를 번들 ffmpeg로 PNG로 변환한다(FR-D1.2 폴백).
/// 실측 근거: XHS 썸네일은 항상 RIFF/WEBP — WPF WIC에 webp 코덱이 없으면 디코드 불가.
/// NFR-D3: 썸네일당 1회 · 3초 타임아웃 · 임시파일 정리.
/// </summary>
internal static class WebpImageConverter
{
    /// <summary>로컬 영상 파일에서 대표 프레임 1장을 PNG 바이트로 추출한다(완료 항목 썸네일 폴백).
    /// 원격 썸네일이 만료(403)되거나 없을 때 완성된 파일 자체에서 미리보기를 만든다.</summary>
    public static async Task<byte[]?> ExtractVideoFrameAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            return null;

        var ffmpeg = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");
        if (!File.Exists(ffmpeg))
            ffmpeg = "ffmpeg";

        var tmpOut = Path.Combine(Path.GetTempPath(), $"mpdl-frame-{Guid.NewGuid():N}.png");
        Process? process = null;
        try
        {
            // -ss 1: 1초 지점(검은 첫 프레임 회피) · 96px 다운스케일 · 1프레임. HEVC도 ffmpeg는 디코드 가능.
            var psi = new ProcessStartInfo(ffmpeg,
                $"-y -loglevel error -ss 1 -i \"{videoPath}\" -frames:v 1 -vf scale=96:-1 \"{tmpOut}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            process = Process.Start(psi);
            if (process is null)
                return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            if (process.ExitCode != 0 || !File.Exists(tmpOut))
                return null;
            return await File.ReadAllBytesAsync(tmpOut, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); }
            catch { /* 무시 */ }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            process?.Dispose();
            try { File.Delete(tmpOut); } catch { /* 무시 */ }
        }
    }

    public static async Task<byte[]?> ConvertToPngAsync(byte[] webpBytes, CancellationToken cancellationToken = default)
    {
        var ffmpeg = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");
        if (!File.Exists(ffmpeg))
            ffmpeg = "ffmpeg"; // PATH 폴백 (Bootstrapper가 tools를 PATH에 추가)

        var tmpIn = Path.Combine(Path.GetTempPath(), $"mpdl-thumb-{Guid.NewGuid():N}.webp");
        var tmpOut = Path.ChangeExtension(tmpIn, ".png");
        Process? process = null;
        try
        {
            await File.WriteAllBytesAsync(tmpIn, webpBytes, cancellationToken).ConfigureAwait(false);

            var psi = new ProcessStartInfo(ffmpeg, $"-y -loglevel error -i \"{tmpIn}\" \"{tmpOut}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            process = Process.Start(psi);
            if (process is null)
                return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            if (process.ExitCode != 0 || !File.Exists(tmpOut))
                return null;
            return await File.ReadAllBytesAsync(tmpOut, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); }
            catch { /* 종료 실패 무시 */ }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            process?.Dispose();
            try { File.Delete(tmpIn); } catch { /* 정리 실패 무시 */ }
            try { File.Delete(tmpOut); } catch { /* 정리 실패 무시 */ }
        }
    }
}

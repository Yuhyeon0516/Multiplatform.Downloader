using System.Diagnostics;

namespace Multiplatform_Downloader.Avalonia.Services;

/// <summary>
/// 로컬 영상에서 대표 프레임 1장을 PNG로 추출(완료 항목 썸네일 폴백) — WPF WebpImageConverter에서
/// 프레임 추출만 이식. WebP→PNG 변환은 Avalonia(Skia)가 WebP를 네이티브 디코드하므로 불필요.
/// </summary>
public static class VideoFrameExtractor
{
    public static async Task<byte[]?> ExtractVideoFrameAsync(string? videoPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            return null;

        var ffmpeg = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
        if (!File.Exists(ffmpeg))
            ffmpeg = "ffmpeg"; // PATH 폴백 (부트스트랩이 tools를 PATH에 추가)

        var tmpOut = Path.Combine(Path.GetTempPath(), $"mpdl-frame-{Guid.NewGuid():N}.png");
        Process? process = null;
        try
        {
            var psi = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            // -ss 1: 1초 지점(검은 첫 프레임 회피) · 96px 다운스케일 · 1프레임
            foreach (var a in new[] { "-y", "-loglevel", "error", "-ss", "1", "-i", videoPath, "-frames:v", "1", "-vf", "scale=96:-1", tmpOut })
                psi.ArgumentList.Add(a);
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
}

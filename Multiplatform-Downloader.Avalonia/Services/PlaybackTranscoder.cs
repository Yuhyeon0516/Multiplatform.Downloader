using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Multiplatform_Downloader.Avalonia.Services;

/// <summary>
/// 인앱 재생 호환 변환(§9의 macOS 분기). WKWebView(Safari 엔진)는 MKV/WebM 컨테이너와
/// opus-in-mp4를 재생하지 못하므로, 비호환 파일은 번들 ffmpeg로 임시 mp4를 만든다
/// (비디오 스트림 복사 + 오디오 AAC 재인코딩 — 수 초 수준). 결과는 임시 폴더에 캐시된다.
/// </summary>
public static class PlaybackTranscoder
{
    private static readonly string[] DirectlyPlayable = [".mp4", ".m4v", ".mov"];

    public static bool IsDirectlyPlayable(string path) =>
        DirectlyPlayable.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>재생 가능한 파일 경로를 반환한다(원본이 호환이면 그대로, 아니면 변환본).
    /// 변환 실패 시 null.</summary>
    public static async Task<string?> EnsurePlayableAsync(string path, CancellationToken cancellationToken = default)
    {
        if (IsDirectlyPlayable(path))
            return path;
        if (!File.Exists(path))
            return null;

        // 캐시 키: 경로+수정시각 — 같은 파일 재생은 즉시
        var stamp = File.GetLastWriteTimeUtc(path).Ticks;
        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{path}|{stamp}")))[..16];
        var cached = Path.Combine(Path.GetTempPath(), $"mpdl-play-{key}.mp4");
        if (File.Exists(cached))
            return cached;

        var ffmpeg = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
        if (!File.Exists(ffmpeg))
            ffmpeg = "ffmpeg";

        var tmp = cached + ".part.mp4";
        Process? process = null;
        try
        {
            var psi = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            // 비디오 복사 + 오디오 AAC — h264/hevc mkv를 몇 초 만에 mp4로. (vp9/av1 비디오는
            // mp4 복사로도 Safari가 재생 못 하므로 실패 시 기본 플레이어 폴백 안내로 처리)
            foreach (var a in new[] { "-y", "-loglevel", "error", "-i", path, "-c:v", "copy", "-c:a", "aac", "-movflags", "+faststart", tmp })
                psi.ArgumentList.Add(a);
            process = Process.Start(psi);
            if (process is null)
                return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            if (process.ExitCode != 0 || !File.Exists(tmp))
            {
                try { File.Delete(tmp); } catch { /* 무시 */ }
                return null;
            }
            File.Move(tmp, cached, overwrite: true);
            return cached;
        }
        catch (OperationCanceledException)
        {
            try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); } catch { /* 무시 */ }
            try { File.Delete(tmp); } catch { /* 무시 */ }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }
}

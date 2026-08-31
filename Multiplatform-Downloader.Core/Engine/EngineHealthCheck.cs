namespace Multiplatform_Downloader.Core.Engine;

/// <summary>엔진 바이너리 존재 점검 결과(NFR-14).</summary>
public sealed record EngineHealthReport(bool AllPresent, IReadOnlyList<string> Missing);

/// <summary>
/// 시작 시 yt-dlp·FFmpeg·ffprobe·JS런타임(Deno) 존재를 확인한다(P0-03·NFR-14).
/// 누락 시 사용자가 복구할 수 있도록 목록을 반환한다.
/// </summary>
public sealed class EngineHealthCheck
{
    // Windows 번들은 .exe, macOS/Linux 번들은 확장자 없는 동일 이름을 요구한다
    private static readonly string[] RequiredBinaries = BuildRequiredBinaries();

    private static string[] BuildRequiredBinaries()
    {
        var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        return ["yt-dlp" + suffix, "ffmpeg" + suffix, "ffprobe" + suffix, "deno" + suffix];
    }

    private readonly string _toolsDirectory;

    public EngineHealthCheck(string toolsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolsDirectory);
        _toolsDirectory = toolsDirectory;
    }

    public EngineHealthReport Check()
    {
        var missing = RequiredBinaries
            .Where(binary => !File.Exists(Path.Combine(_toolsDirectory, binary)))
            .ToList();
        return new EngineHealthReport(missing.Count == 0, missing);
    }
}

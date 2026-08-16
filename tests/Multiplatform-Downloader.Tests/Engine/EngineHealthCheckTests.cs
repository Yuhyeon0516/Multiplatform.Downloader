using System.IO;
using Multiplatform_Downloader.Core.Engine;

namespace Multiplatform_Downloader.Tests.Engine;

public class EngineHealthCheckTests : IDisposable
{
    private readonly string _tempDir;

    public EngineHealthCheckTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mpdl-tools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 무시 */ }
    }

    private void CreateBinary(string name) => File.WriteAllText(Path.Combine(_tempDir, name), "stub");

    [Fact]
    public void should_report_all_present_when_binaries_exist()
    {
        foreach (var binary in new[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe" })
            CreateBinary(binary);

        var report = new EngineHealthCheck(_tempDir).Check();

        Assert.True(report.AllPresent);
        Assert.Empty(report.Missing);
    }

    [Fact]
    public void should_report_missing_when_binary_absent()
    {
        CreateBinary("yt-dlp.exe");
        CreateBinary("ffmpeg.exe");
        // ffprobe.exe, deno.exe 누락

        var report = new EngineHealthCheck(_tempDir).Check();

        Assert.False(report.AllPresent);
        Assert.Contains("ffprobe.exe", report.Missing);
        Assert.Contains("deno.exe", report.Missing);
    }
}

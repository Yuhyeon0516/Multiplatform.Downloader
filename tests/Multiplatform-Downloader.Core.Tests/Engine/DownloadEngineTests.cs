using Multiplatform_Downloader.Core.Engine;
using Multiplatform_Downloader.Core.Models;

namespace Multiplatform_Downloader.Tests.Engine;

public class DownloadEngineTests
{
    private static DownloadRequest Request(bool cont = false) =>
        new("https://youtu.be/abc", "137+140", "%(title)s.%(ext)s", Continue: cont);

    [Fact]
    public async Task should_report_mapped_progress_when_downloading_merged_format()
    {
        // 병합 포맷(137+140)은 영상 raw %가 0~85% 대역으로 매핑된다(MultiStreamProgressMapper)
        var runner = new LineEmittingRunner(
            [
                "[download]   0.0% of 10.00MiB at 1.00MiB/s ETA 00:10",
                "[download]  50.0% of 10.00MiB at 2.00MiB/s ETA 00:05",
                "[download] 100% of 10.00MiB in 00:05",
                @"D:\Videos\video.mp4",
            ],
            exitCode: 0);
        var engine = new DownloadEngine(runner, "yt-dlp.exe");

        // Progress<T>는 SynchronizationContext에 비동기 게시하므로 테스트에서는 동기 IProgress 구현 사용
        var collector = new CollectingProgress();
        var result = await engine.DownloadAsync(Request(), collector);

        Assert.True(result.Success);
        Assert.Contains(collector.Reports, r => Math.Abs(r.Percent - 50.0 * 0.85) < 0.01);
        Assert.Contains(collector.Reports, r => Math.Abs(r.Percent - 85.0) < 0.01);
    }

    [Fact]
    public async Task should_report_raw_progress_when_single_stream_format()
    {
        var runner = new LineEmittingRunner(
            [
                "[download]  50.0% of 10.00MiB at 2.00MiB/s ETA 00:05",
                "[download] 100% of 10.00MiB in 00:05",
                @"D:\Videos\video.mp4",
            ],
            exitCode: 0);
        var engine = new DownloadEngine(runner, "yt-dlp.exe");

        var collector = new CollectingProgress();
        var result = await engine.DownloadAsync(
            new DownloadRequest("https://youtu.be/abc", "best", "%(title)s.%(ext)s"), collector);

        Assert.True(result.Success);
        Assert.Contains(collector.Reports, r => r.Percent == 50.0);
        Assert.Contains(collector.Reports, r => r.Percent == 100.0);
    }

    [Fact]
    public async Task should_return_output_path_when_success()
    {
        // 엔진은 Path.IsPathRooted로 출력 경로를 판별하므로 실행 OS 기준의 루트 경로를 써야 한다
        var rootedPath = OperatingSystem.IsWindows() ? @"D:\Videos\video.mp4" : "/Videos/video.mp4";
        var runner = new LineEmittingRunner(
            ["[download] 100% of 10.00MiB in 00:05", rootedPath],
            exitCode: 0);
        var engine = new DownloadEngine(runner, "yt-dlp.exe");

        var result = await engine.DownloadAsync(Request());

        Assert.True(result.Success);
        Assert.Equal(rootedPath, result.OutputFilePath);
    }

    [Fact]
    public async Task should_return_failure_when_exit_nonzero()
    {
        var runner = new LineEmittingRunner(["ERROR: unavailable"], exitCode: 1);
        var engine = new DownloadEngine(runner, "yt-dlp.exe");

        var result = await engine.DownloadAsync(Request());

        Assert.False(result.Success);
        Assert.Null(result.OutputFilePath);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task should_pass_continue_flag_when_resuming()
    {
        var runner = new LineEmittingRunner([], exitCode: 0);
        var engine = new DownloadEngine(runner, "yt-dlp.exe");

        await engine.DownloadAsync(Request(cont: true));

        Assert.Contains("-c", runner.LastArguments!);
    }

    private sealed class CollectingProgress : IProgress<DownloadProgress>
    {
        public List<DownloadProgress> Reports { get; } = [];
        public void Report(DownloadProgress value) => Reports.Add(value);
    }

    private sealed class LineEmittingRunner(IReadOnlyList<string> lines, int exitCode) : IProcessRunner
    {
        public IReadOnlyList<string>? LastArguments { get; private set; }

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            Action<string>? onOutputLine = null,
            CancellationToken cancellationToken = default)
        {
            LastArguments = arguments;
            foreach (var line in lines)
                onOutputLine?.Invoke(line);
            return Task.FromResult(new ProcessResult(exitCode, lines, Array.Empty<string>()));
        }
    }
}

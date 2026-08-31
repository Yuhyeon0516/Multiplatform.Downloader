using System.Diagnostics;
using Multiplatform_Downloader.Core.Abstractions;
using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Engine;
using Multiplatform_Downloader.Core.Platforms;
using Multiplatform_Downloader.Core.Queue;
using Multiplatform_Downloader.Core.Settings;

namespace Multiplatform_Downloader.Tests.Integration;

/// <summary>실제 서비스 체인(ProcessRunner + 없는 yt-dlp + AppLogger)으로 파이프라인이 로그로 관찰되는지 검증.</summary>
public class PipelineLoggingTests
{
    [Fact]
    public async Task should_log_stages_when_pipeline_runs_and_fails()
    {
        var logger = new AppLogger(new SystemClock());
        using var queue = new DownloadQueueService(
            new BatchUrlParser(new PlatformDetector()),
            new MediaMetadataService(new ProcessRunner(), "definitely-not-a-real-ytdlp.exe"),
            new DownloadEngine(new ProcessRunner(), "definitely-not-a-real-ytdlp.exe"),
            new InMemorySettings(),
            new MediaFormatSelector(),
            resolveUrl: null,
            logger: logger);

        var item = queue.Enqueue("https://www.youtube.com/watch?v=abc").Added[0];

        await WaitForAsync(() => item.Status == DownloadStatus.Failed);

        // 등록 → 분석 시작 → 실패가 모두 로그로 남는다
        Assert.Contains(logger.Recent, e => e.Message.Contains("등록"));
        Assert.Contains(logger.Recent, e => e.Message.Contains("분석 시작"));
        Assert.Contains(logger.Recent, e => e.Level is AppLogLevel.Warning or AppLogLevel.Error);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(20);
        Assert.True(condition(), "파이프라인이 제한 시간 내에 완료되지 않았습니다.");
    }

    private sealed class InMemorySettings : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public bool SettingsFileExisted => true;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

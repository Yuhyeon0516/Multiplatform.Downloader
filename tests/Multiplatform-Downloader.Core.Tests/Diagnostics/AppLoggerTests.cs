using System.IO;
using Multiplatform_Downloader.Core.Abstractions;
using Multiplatform_Downloader.Core.Diagnostics;

namespace Multiplatform_Downloader.Tests.Diagnostics;

public class AppLoggerTests
{
    private sealed class FixedClock : IClock
    {
        public DateTime Now { get; } = new(2026, 7, 30, 12, 0, 0);
        public DateTime UtcNow => Now;
    }

    [Fact]
    public void should_store_recent_and_raise_event()
    {
        var logger = new AppLogger(new FixedClock());
        LogEntry? received = null;
        logger.Logged += (_, entry) => received = entry;

        logger.Info("Test", "hello");

        Assert.Single(logger.Recent);
        Assert.NotNull(received);
        Assert.Equal("hello", received!.Message);
        Assert.Equal(AppLogLevel.Info, received.Level);
    }

    [Fact]
    public void should_write_to_file_when_path_provided()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "mpdl-log-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            var logger = new AppLogger(new FixedClock(), tempPath);
            logger.Error("Engine", "boom");

            var content = File.ReadAllText(tempPath);
            Assert.Contains("boom", content);
            Assert.Contains("Error", content);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* 무시 */ }
        }
    }

    [Fact]
    public void should_cap_recent_entries()
    {
        var logger = new AppLogger(new FixedClock());

        for (var i = 0; i < 1500; i++)
            logger.Debug("cat", i.ToString());

        Assert.True(logger.Recent.Count <= 1000);
    }
}

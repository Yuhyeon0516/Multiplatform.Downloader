using Multiplatform_Downloader.Core.Engine;

namespace Multiplatform_Downloader.Tests.Engine;

public class YtDlpProgressParserTests
{
    [Fact]
    public void should_parse_percent_speed_eta_when_download_line()
    {
        var progress = YtDlpOutputParser.ParseProgressLine("[download]  42.3% of ~10.00MiB at 1.23MiB/s ETA 00:41");

        Assert.NotNull(progress);
        Assert.Equal(42.3, progress!.Percent, precision: 1);
        Assert.Equal((long)(1.23 * 1024 * 1024), progress.SpeedBytesPerSec);
        Assert.Equal(TimeSpan.FromSeconds(41), progress.Eta);
    }

    [Fact]
    public void should_parse_hours_when_eta_has_hours()
    {
        var progress = YtDlpOutputParser.ParseProgressLine("[download]   5.0% of 1.00GiB at 500.00KiB/s ETA 1:02:03");

        Assert.NotNull(progress);
        Assert.Equal(new TimeSpan(1, 2, 3), progress!.Eta);
    }

    [Fact]
    public void should_parse_percent_only_when_completion_line()
    {
        var progress = YtDlpOutputParser.ParseProgressLine("[download] 100% of 10.00MiB in 00:08");

        Assert.NotNull(progress);
        Assert.Equal(100, progress!.Percent);
        Assert.Null(progress.SpeedBytesPerSec);
    }

    [Theory]
    [InlineData("[download] Destination: video.mp4")]
    [InlineData("[info] Available formats")]
    [InlineData("some random line")]
    [InlineData("")]
    [InlineData(null)]
    public void should_return_null_when_not_progress_line(string? line)
    {
        Assert.Null(YtDlpOutputParser.ParseProgressLine(line));
    }
}

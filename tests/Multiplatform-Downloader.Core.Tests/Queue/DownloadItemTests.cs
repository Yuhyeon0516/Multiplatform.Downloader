using Multiplatform_Downloader.Core.Models;
using Multiplatform_Downloader.Core.Platforms;
using Multiplatform_Downloader.Core.Queue;

namespace Multiplatform_Downloader.Tests.Queue;

public class DownloadItemTests
{
    private static DownloadItem NewItem() => new("https://youtu.be/abc", PlatformType.YouTube);

    private static DownloadItem Ready()
    {
        var item = NewItem();
        item.MarkAnalyzing();
        item.MarkReady();
        return item;
    }

    private static DownloadItem Downloading()
    {
        var item = Ready();
        item.Start();
        return item;
    }

    [Fact]
    public void should_start_in_queued_state()
    {
        Assert.Equal(DownloadStatus.Queued, NewItem().Status);
    }

    [Fact]
    public void should_transition_queued_to_analyzing_to_ready()
    {
        var item = NewItem();

        item.MarkAnalyzing();
        Assert.Equal(DownloadStatus.Analyzing, item.Status);

        item.MarkReady();
        Assert.Equal(DownloadStatus.Ready, item.Status);
    }

    [Fact]
    public void should_transition_downloading_to_paused_when_pause()
    {
        var item = Downloading();
        item.Pause();
        Assert.Equal(DownloadStatus.Paused, item.Status);
    }

    [Fact]
    public void should_resume_to_downloading_when_resume()
    {
        var item = Downloading();
        item.Pause();

        item.Resume();

        Assert.Equal(DownloadStatus.Downloading, item.Status);
    }

    [Fact]
    public void should_reject_pause_when_merging()
    {
        var item = Downloading();
        item.MarkMerging();

        Assert.Throws<InvalidOperationException>(() => item.Pause());
    }

    [Fact]
    public void should_reject_start_when_queued()
    {
        Assert.Throws<InvalidOperationException>(() => NewItem().Start());
    }

    [Fact]
    public void should_go_failed_when_error()
    {
        var item = Downloading();

        item.Fail("network error", ErrorCategory.Network);

        Assert.Equal(DownloadStatus.Failed, item.Status);
        Assert.Equal(ErrorCategory.Network, item.LastErrorCategory);
        Assert.Equal("network error", item.ErrorMessage);
    }

    [Fact]
    public void should_complete_with_output_path_and_full_progress()
    {
        var item = Downloading();
        item.MarkMerging();

        item.Complete(@"D:\Videos\v.mp4");

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.Equal(@"D:\Videos\v.mp4", item.OutputFilePath);
        Assert.Equal(100, item.ProgressPercent);
    }

    [Fact]
    public void should_reject_cancel_when_already_completed()
    {
        var item = Downloading();
        item.Complete(@"D:\v.mp4");

        Assert.Throws<InvalidOperationException>(() => item.Cancel());
    }

    [Fact]
    public void should_increment_retry_and_reset_when_prepare_retry()
    {
        var item = Downloading();
        item.Fail("err");

        item.PrepareRetry();

        Assert.Equal(DownloadStatus.Ready, item.Status);
        Assert.Equal(1, item.RetryCount);
        Assert.Null(item.ErrorMessage);
    }

    [Fact]
    public void should_update_progress_when_downloading()
    {
        var item = Downloading();

        item.UpdateProgress(new DownloadProgress(50, 1000, TimeSpan.FromSeconds(10)));

        Assert.Equal(50, item.ProgressPercent);
        Assert.Equal(1000, item.SpeedBytesPerSec);
    }

    [Fact]
    public void should_ignore_progress_when_not_downloading()
    {
        var item = Ready();

        item.UpdateProgress(new DownloadProgress(50, 1000, null));

        Assert.Equal(0, item.ProgressPercent);
    }

    [Fact]
    public void should_complete_when_output_path_null() // H3 회귀
    {
        var item = Downloading();

        item.Complete(null);

        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.Null(item.OutputFilePath);
        Assert.Equal(100, item.ProgressPercent);
    }

    [Fact]
    public void should_reject_fail_when_already_canceled() // H5 회귀
    {
        var item = Downloading();
        item.Cancel();

        Assert.Throws<InvalidOperationException>(() => item.Fail("네트워크 오류"));
        Assert.Equal(DownloadStatus.Canceled, item.Status); // Canceled 유지
    }
}

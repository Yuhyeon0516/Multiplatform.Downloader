using System.IO;
using Multiplatform_Downloader.Core.Platforms;
using Multiplatform_Downloader.Core.Queue;

namespace Multiplatform_Downloader.Tests.Queue;

public class QueuePersistenceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public QueuePersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mpdl-queue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "queue-state.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 무시 */ }
    }

    private static DownloadItem ItemWithStatus(DownloadStatus status)
    {
        var item = new DownloadItem("https://youtu.be/" + Guid.NewGuid().ToString("N"), PlatformType.YouTube);
        // 상태 진행
        if (status is DownloadStatus.Analyzing or DownloadStatus.Ready or DownloadStatus.Downloading or DownloadStatus.Completed)
            item.MarkAnalyzing();
        if (status is DownloadStatus.Ready or DownloadStatus.Downloading or DownloadStatus.Completed)
            item.MarkReady();
        if (status is DownloadStatus.Downloading or DownloadStatus.Completed)
            item.Start();
        if (status is DownloadStatus.Completed)
            item.Complete(@"D:\done.mp4");
        return item;
    }

    [Fact]
    public async Task should_save_completed_with_output_path()
    {
        // v2: 완료 항목도 저장한다 — 재시작 후 받음/안받음 구분(사용자 요청)
        var persistence = new QueuePersistence(_filePath);
        var items = new[]
        {
            ItemWithStatus(DownloadStatus.Ready),
            ItemWithStatus(DownloadStatus.Downloading),
            ItemWithStatus(DownloadStatus.Completed),
        };

        await persistence.SaveAsync(items);
        var restored = await persistence.LoadAsync();

        Assert.Equal(3, restored.Count);
        var completed = Assert.Single(restored, s => s.Status == DownloadStatus.Completed);
        Assert.Equal(@"D:\done.mp4", completed.OutputFilePath);
    }

    [Fact]
    public async Task should_cap_completed_snapshots_dropping_oldest()
    {
        var persistence = new QueuePersistence(_filePath);
        var items = Enumerable.Range(0, QueuePersistence.MaxCompletedSnapshots + 5)
            .Select(_ => ItemWithStatus(DownloadStatus.Completed))
            .ToList();
        var oldestUrl = items[0].OriginalUrl;
        var newestUrl = items[^1].OriginalUrl;

        await persistence.SaveAsync(items);
        var restored = await persistence.LoadAsync();

        Assert.Equal(QueuePersistence.MaxCompletedSnapshots, restored.Count);
        Assert.DoesNotContain(restored, s => s.OriginalUrl == oldestUrl); // 오래된 것부터 제외
        Assert.Contains(restored, s => s.OriginalUrl == newestUrl);
    }

    [Fact]
    public async Task should_load_v1_snapshot_without_output_path()
    {
        // 구버전(v1) 파일 호환 — OutputFilePath만 null로 읽힌다
        await File.WriteAllTextAsync(_filePath, """
            { "schemaVersion": 1, "items": [ {
                "id": "11111111-1111-1111-1111-111111111111",
                "originalUrl": "https://youtu.be/v1compat",
                "platform": "YouTube",
                "status": "Ready",
                "extractionRoute": "YtDlp"
            } ] }
            """);
        var persistence = new QueuePersistence(_filePath);

        var restored = await persistence.LoadAsync();

        var snapshot = Assert.Single(restored);
        Assert.Equal("https://youtu.be/v1compat", snapshot.OriginalUrl);
        Assert.Null(snapshot.OutputFilePath);
    }

    [Fact]
    public async Task should_restore_item_fields_on_load()
    {
        var persistence = new QueuePersistence(_filePath);
        var item = ItemWithStatus(DownloadStatus.Ready);
        item.Title = "제목";

        await persistence.SaveAsync([item]);
        var restored = await persistence.LoadAsync();

        var snapshot = Assert.Single(restored);
        Assert.Equal(item.OriginalUrl, snapshot.OriginalUrl);
        Assert.Equal("제목", snapshot.Title);
        Assert.Equal(PlatformType.YouTube, snapshot.Platform);
    }

    [Fact]
    public async Task should_return_empty_when_file_missing()
    {
        var persistence = new QueuePersistence(_filePath);
        var restored = await persistence.LoadAsync();
        Assert.Empty(restored);
    }

    [Fact]
    public async Task should_recover_empty_when_corrupt()
    {
        await File.WriteAllTextAsync(_filePath, "{ broken json ]");
        var persistence = new QueuePersistence(_filePath);

        var restored = await persistence.LoadAsync();

        Assert.Empty(restored);
    }

    [Fact]
    public async Task should_return_empty_when_schema_version_mismatch()
    {
        await File.WriteAllTextAsync(_filePath, """{ "schemaVersion": 999, "items": [] }""");
        var persistence = new QueuePersistence(_filePath);

        var restored = await persistence.LoadAsync();

        Assert.Empty(restored);
    }
}

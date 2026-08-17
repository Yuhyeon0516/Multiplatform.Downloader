using System.IO;
using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Platforms;
using Multiplatform_Downloader.Core.Queue;
using Multiplatform_Downloader.ViewModels;

namespace Multiplatform_Downloader.Tests.ViewModels;

/// <summary>캡컷(외부 앱) 드래그 아웃 — FR-DG2/DG3 판정·수집 R1 검증 (capcut-drag-drop PRD S1·S3·S4).</summary>
public class CardDragTests : IDisposable
{
    private readonly string _tempDir;

    public CardDragTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mpdl-drag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 무시 */ }
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "video");
        return path;
    }

    private static DownloadItem CompletedItem(string? outputPath)
    {
        var item = new DownloadItem("https://youtu.be/" + Guid.NewGuid().ToString("N"), PlatformType.YouTube);
        item.MarkAnalyzing(); item.MarkReady(); item.Start();
        item.Complete(outputPath);
        return item;
    }

    private sealed class FakeSettings : Multiplatform_Downloader.Core.Settings.ISettingsService
    {
        public Multiplatform_Downloader.Core.Settings.AppSettings Current { get; } = new();
        public bool SettingsFileExisted => true;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeQueue : IDownloadQueueService
    {
        private readonly List<DownloadItem> _items = [];
        public IReadOnlyList<DownloadItem> Items => _items;
        public event EventHandler<DownloadItem>? ItemChanged;
        public void AddAndRaise(DownloadItem item) { _items.Add(item); ItemChanged?.Invoke(this, item); }
        public EnqueueResult Enqueue(string urlsText) => new([], [], 0);
        public void Start(Guid id) { }
        public void StartAll() { }
        public void ChangeFormat(Guid id, string formatId) { }
        public void Cancel(Guid id) { }
        public void Pause(Guid id) { }
        public void Resume(Guid id) { }
        public void Remove(Guid id) { }
        public void Retry(Guid id) { }
        public void PauseAll() { }
        public void ResumeAll() { }
        public void SweepOrphanPartials() { }
        public void RestoreCompleted(QueueItemSnapshot snapshot) { }
    }

    private static ShellViewModel CreateShell(FakeQueue queue)
        => new(queue, new FakeSettings(), NullAppLogger.Instance, null!,
               new BatchUrlParser(new PlatformDetector()));

    // ── FR-DG3: CanDragItem 판정 (S1-11~18) ──

    [Fact]
    public void should_enable_drag_when_completed_with_path()
    {
        var vm = new DownloadItemViewModel(CompletedItem(@"C:\o.mp4"), new FakeQueue(), 0, null);
        Assert.True(vm.CanDragItem);
    }

    [Fact]
    public void should_disable_drag_when_completed_without_path()
    {
        // H3: 경로 미상 완료 (S1-18)
        var vm = new DownloadItemViewModel(CompletedItem(null), new FakeQueue(), 0, null);
        Assert.False(vm.CanDragItem);
    }

    [Fact]
    public void should_disable_drag_when_restored_file_missing()
    {
        // v2.12.4 파일없음 복원 = Unavailable (S1-17)
        var item = new DownloadItem("https://youtu.be/gone", PlatformType.YouTube);
        item.MarkAnalyzing();
        item.MarkUnavailable("받은 파일이 폴더에 없습니다");
        var vm = new DownloadItemViewModel(item, new FakeQueue(), 0, null);
        Assert.False(vm.CanDragItem);
    }

    [Fact]
    public void should_disable_drag_when_not_completed()
    {
        // S1-12: Ready 카드
        var item = new DownloadItem("https://youtu.be/rdy", PlatformType.YouTube);
        item.MarkAnalyzing(); item.MarkReady();
        var vm = new DownloadItemViewModel(item, new FakeQueue(), 0, null);
        Assert.False(vm.CanDragItem);
    }

    // ── FR-DG3: 드래그 직전 파일 실재 검증 (S1-19·S4-06/07) ──

    [Fact]
    public void should_return_path_when_file_exists()
    {
        var path = CreateFile("영상 [abc123].mp4");
        var vm = new DownloadItemViewModel(CompletedItem(path), new FakeQueue(), 0, null);
        Assert.Equal(path, vm.GetDraggablePath());
    }

    [Fact]
    public void should_return_null_when_file_deleted()
    {
        // S1-19: 완료였지만 파일이 삭제됨 → 드래그 미시작 근거
        var vm = new DownloadItemViewModel(
            CompletedItem(Path.Combine(_tempDir, "없는파일 [x1].mp4")), new FakeQueue(), 0, null);
        Assert.Null(vm.GetDraggablePath());
    }

    [Fact]
    public void should_resolve_renamed_file_when_id_token_matches()
    {
        // S4-07: 기록 경로와 다른 실파일명([id] 유지) → 재해석 성공
        var actual = CreateFile("바뀐제목 [vid42].mp4");
        var recorded = Path.Combine(_tempDir, "원래제목 [vid42].mp4"); // 실존하지 않는 기록 경로
        var vm = new DownloadItemViewModel(CompletedItem(recorded), new FakeQueue(), 0, null);
        Assert.Equal(actual, vm.GetDraggablePath());
    }

    // ── FR-DG2: 멀티 수집 규칙 (S3) ──

    [Fact]
    public void should_collect_all_checked_draggables_when_origin_checked()
    {
        // S3-01·S3-04: 체크된 완료만 전체 수집(미완료 체크 항목 제외)
        var queue = new FakeQueue();
        var shell = CreateShell(queue);
        var f1 = CreateFile("a [i1].mp4");
        var f2 = CreateFile("b [i2].mp4");
        queue.AddAndRaise(CompletedItem(f1));
        queue.AddAndRaise(CompletedItem(f2));
        var ready = new DownloadItem("https://youtu.be/rdy", PlatformType.YouTube);
        ready.MarkAnalyzing(); ready.MarkReady();
        queue.AddAndRaise(ready); // 체크되지만 드래그 불가

        var origin = shell.Items.First(i => i.CanDragItem);
        Assert.True(origin.IsChecked); // 기본값 true

        var paths = shell.CollectDragPaths(origin);

        Assert.Equal(2, paths.Count);
        Assert.Contains(f1, paths);
        Assert.Contains(f2, paths);
    }

    [Fact]
    public void should_collect_single_when_origin_unchecked()
    {
        // S3-02: 시작 카드가 체크 해제 → 그 카드 1개만
        var queue = new FakeQueue();
        var shell = CreateShell(queue);
        var f1 = CreateFile("a [i1].mp4");
        var f2 = CreateFile("b [i2].mp4");
        queue.AddAndRaise(CompletedItem(f1));
        queue.AddAndRaise(CompletedItem(f2));

        var origin = shell.Items[0];
        origin.IsChecked = false;

        var paths = shell.CollectDragPaths(origin);

        Assert.Equal([f1], paths);
    }

    [Fact]
    public void should_skip_missing_files_when_collecting()
    {
        // S3-05: 체크된 완료 2개 중 1개 파일 삭제 → 실존 1개만
        var queue = new FakeQueue();
        var shell = CreateShell(queue);
        var live = CreateFile("live [ok1].mp4");
        queue.AddAndRaise(CompletedItem(live));
        queue.AddAndRaise(CompletedItem(Path.Combine(_tempDir, "deleted [no1].mp4")));

        var paths = shell.CollectDragPaths(shell.Items[0]);

        Assert.Equal([live], paths);
    }

    [Fact]
    public void should_return_empty_when_all_files_missing()
    {
        // S3-06: 전부 삭제됨 → 빈 목록(드래그 미시작)
        var queue = new FakeQueue();
        var shell = CreateShell(queue);
        queue.AddAndRaise(CompletedItem(Path.Combine(_tempDir, "gone [g1].mp4")));

        var paths = shell.CollectDragPaths(shell.Items[0]);

        Assert.Empty(paths);
    }
}

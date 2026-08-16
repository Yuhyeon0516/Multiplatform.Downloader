using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Platforms;
using Multiplatform_Downloader.Core.Queue;
using Multiplatform_Downloader.Core.Settings;
using Multiplatform_Downloader.ViewModels;

namespace Multiplatform_Downloader.Tests.ViewModels;

public class ShellViewModelTests
{
    private static ShellViewModel CreateSut(FakeQueue queue)
        => new(queue, new FakeSettings(), NullAppLogger.Instance, null!, new BatchUrlParser(new PlatformDetector()));

    [Fact]
    public void should_enqueue_when_add_urls_called()
    {
        var queue = new FakeQueue();
        var sut = CreateSut(queue);
        sut.UrlInput = "https://youtu.be/abc";

        sut.AddUrls();

        Assert.Equal("https://youtu.be/abc", queue.LastEnqueued);
        Assert.Equal(string.Empty, sut.UrlInput);
    }

    [Fact]
    public void should_not_enqueue_when_input_blank()
    {
        var queue = new FakeQueue();
        var sut = CreateSut(queue);
        sut.UrlInput = "   ";

        sut.AddUrls();

        Assert.Null(queue.LastEnqueued);
    }

    [Fact]
    public void should_disable_add_when_input_blank()
    {
        var sut = CreateSut(new FakeQueue());

        Assert.False(sut.CanAddUrls);
        sut.UrlInput = "https://youtu.be/x";
        Assert.True(sut.CanAddUrls);
    }

    [Fact]
    public void should_add_card_when_queue_raises_item_changed()
    {
        var queue = new FakeQueue();
        var sut = CreateSut(queue);
        var item = new DownloadItem("https://youtu.be/abc", PlatformType.YouTube);

        queue.AddAndRaise(item);

        Assert.Single(sut.Items);
    }

    private static DownloadItem ReadyItem()
    {
        var item = new DownloadItem("https://youtu.be/" + Guid.NewGuid().ToString("N"), PlatformType.YouTube);
        item.MarkAnalyzing();
        item.MarkReady();
        return item;
    }

    [Fact]
    public void should_start_only_checked_ready_items_when_start_checked()
    {
        var queue = new FakeQueue();
        var sut = CreateSut(queue);
        var ready1 = ReadyItem();
        var ready2 = ReadyItem();
        var queued = new DownloadItem("https://youtu.be/q", PlatformType.YouTube); // Ready 아님
        queue.AddAndRaise(ready1);
        queue.AddAndRaise(ready2);
        queue.AddAndRaise(queued);

        sut.Items.First(i => i.Id == ready2.Id).IsChecked = false; // 두 번째는 선택 해제

        sut.StartChecked();

        Assert.Equal([ready1.Id], queue.StartedIds); // 체크된 Ready 항목만 시작
    }

    [Fact]
    public void should_toggle_all_items_when_select_all_set()
    {
        // FR-D3.2: 헤더 전체선택
        var queue = new FakeQueue();
        var sut = CreateSut(queue);
        queue.AddAndRaise(ReadyItem());
        queue.AddAndRaise(ReadyItem());
        sut.Items[0].IsChecked = false; // 일부 선택 상태

        Assert.Null(sut.SelectAllState); // tri-state: 일부 선택

        sut.SelectAllState = true;
        Assert.True(sut.Items.All(i => i.IsChecked));
        Assert.True(sut.SelectAllState);

        sut.SelectAllState = false;
        Assert.True(sut.Items.All(i => !i.IsChecked));
        Assert.Equal("0/2 선택", sut.SelectionSummary);
    }

    [Fact]
    public async Task should_remove_only_checked_items_when_delete_confirmed()
    {
        // FR-D3.4: 선택 삭제 — 확인(테마 일치 커스텀 창) 승인 시 체크된 항목만 큐에서 제거
        var queue = new FakeQueue();
        var sut = CreateSut(queue);
        sut.ConfirmInteraction = (_, _) => Task.FromResult(true); // 확인창 승인 페이크
        var keep = ReadyItem();
        var remove = ReadyItem();
        queue.AddAndRaise(keep);
        queue.AddAndRaise(remove);
        sut.Items.First(i => i.Id == keep.Id).IsChecked = false;

        Assert.Equal("선택 삭제 (1)", sut.DeleteCheckedLabel);
        await sut.DeleteChecked();

        Assert.Equal([remove.Id], queue.RemovedIds);
    }

    [Fact]
    public async Task should_not_remove_when_delete_confirm_rejected()
    {
        // 확인창에서 [취소] → 아무 것도 삭제하지 않는다
        var queue = new FakeQueue();
        var sut = CreateSut(queue);
        sut.ConfirmInteraction = (_, _) => Task.FromResult(false);
        queue.AddAndRaise(ReadyItem());

        await sut.DeleteChecked();

        Assert.Empty(queue.RemovedIds);
    }

    [Fact]
    public async Task should_remove_card_only_when_item_confirm_accepted()
    {
        // 카드 [삭제] 버튼도 확인창을 거친다 — 거부 시 유지, 승인 시 제거
        var queue = new FakeQueue();
        var item = ReadyItem();
        var card = new DownloadItemViewModel(item, queue);
        card.Refresh(item);

        card.ConfirmRemove = _ => Task.FromResult(false);
        await card.RemoveItemAsync();
        Assert.Empty(queue.RemovedIds);

        card.ConfirmRemove = _ => Task.FromResult(true);
        await card.RemoveItemAsync();
        Assert.Equal([item.Id], queue.RemovedIds);
    }

    [Fact]
    public void should_update_start_checked_label_and_guard_when_toggling()
    {
        var queue = new FakeQueue();
        var sut = CreateSut(queue);
        var ready = ReadyItem();
        queue.AddAndRaise(ready);

        Assert.Equal("선택 받기 (1)", sut.StartCheckedLabel); // 기본 체크됨
        Assert.True(sut.CanStartChecked);

        sut.Items[0].IsChecked = false;

        Assert.Equal("선택 받기 (0)", sut.StartCheckedLabel);
        Assert.False(sut.CanStartChecked);
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public bool SettingsFileExisted => true;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeQueue : IDownloadQueueService
    {
        private readonly List<DownloadItem> _items = [];
        public string? LastEnqueued { get; private set; }
        public List<Guid> StartedIds { get; } = [];
        public List<Guid> RemovedIds { get; } = [];
        public IReadOnlyList<DownloadItem> Items => _items;
        public event EventHandler<DownloadItem>? ItemChanged;

        public void AddAndRaise(DownloadItem item)
        {
            if (!_items.Contains(item)) _items.Add(item);
            ItemChanged?.Invoke(this, item);
        }

        public EnqueueResult Enqueue(string urlsText)
        {
            LastEnqueued = urlsText;
            return new EnqueueResult([], [], 0);
        }

        public void Start(Guid id) => StartedIds.Add(id);
        public void StartAll() { }
        public void ChangeFormat(Guid id, string formatId) { }
        public void Cancel(Guid id) { }
        public void Pause(Guid id) { }
        public void Resume(Guid id) { }
        public void Remove(Guid id) => RemovedIds.Add(id);
        public void Retry(Guid id) { }
        public void PauseAll() { }
        public void ResumeAll() { }
        public void SweepOrphanPartials() { }
        public void RestoreCompleted(QueueItemSnapshot snapshot) { }
    }
}

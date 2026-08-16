using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Platforms;
using Multiplatform_Downloader.Core.Queue;
using Multiplatform_Downloader.Core.Settings;
using Multiplatform_Downloader.ViewModels;

namespace Multiplatform_Downloader.Tests.ViewModels;

/// <summary>FR-U2.2/U2.3 — 필터 버킷·검색 술어·카운트 분해 검증 (ui-redesign Phase 2).</summary>
public class ShellFilterTests
{
    private static ShellViewModel CreateSut(FakeQueue queue)
        => new(queue, new FakeSettings(), NullAppLogger.Instance, null!, new BatchUrlParser(new PlatformDetector()));

    private static DownloadItem Item(string url, Action<DownloadItem>? mutate = null)
    {
        var item = new DownloadItem(url, PlatformType.YouTube);
        mutate?.Invoke(item);
        return item;
    }

    [Theory]
    [InlineData(DownloadStatus.Downloading, QueueFilter.Active)]
    [InlineData(DownloadStatus.Merging, QueueFilter.Active)]
    [InlineData(DownloadStatus.Queued, QueueFilter.Waiting)]
    [InlineData(DownloadStatus.Analyzing, QueueFilter.Waiting)]
    [InlineData(DownloadStatus.Ready, QueueFilter.Waiting)]
    [InlineData(DownloadStatus.Paused, QueueFilter.Waiting)]
    [InlineData(DownloadStatus.Completed, QueueFilter.Done)]
    [InlineData(DownloadStatus.Failed, QueueFilter.Failed)]
    [InlineData(DownloadStatus.Canceled, QueueFilter.Failed)]
    [InlineData(DownloadStatus.Unavailable, QueueFilter.Failed)]
    public void should_map_status_to_bucket_when_classified(DownloadStatus status, QueueFilter expected)
    {
        // 상태바 집계(SUM 시뮬 60건)와 동일 매핑이어야 한다(FR-U2.3)
        Assert.Equal(expected, ShellViewModel.BucketOf(status));
    }

    [Fact]
    public void should_match_only_bucket_items_when_filter_active()
    {
        var queue = new FakeQueue();
        var sut = CreateSut(queue);
        var ready = Item("https://youtu.be/r1", i => { i.MarkAnalyzing(); i.MarkReady(); });
        var downloading = Item("https://youtu.be/d1", i => { i.MarkAnalyzing(); i.MarkReady(); i.Start(); });
        queue.AddAndRaise(ready);
        queue.AddAndRaise(downloading);

        sut.ActiveFilter = QueueFilter.Active;

        Assert.False(sut.MatchesFilter(sut.Items[0])); // Ready → Waiting 버킷
        Assert.True(sut.MatchesFilter(sut.Items[1]));  // Downloading → Active 버킷
    }

    [Fact]
    public void should_match_title_and_url_when_search_text_set()
    {
        var queue = new FakeQueue();
        var sut = CreateSut(queue);
        var byTitle = Item("https://youtu.be/a1", i => i.Title = "서울 야경 드라이브");
        var byUrl = Item("https://youtu.be/CatJump99");
        var noMatch = Item("https://youtu.be/b2", i => i.Title = "요리 브이로그");
        queue.AddAndRaise(byTitle);
        queue.AddAndRaise(byUrl);
        queue.AddAndRaise(noMatch);

        sut.SearchText = "야경";
        Assert.True(sut.MatchesFilter(sut.Items[0]));
        Assert.False(sut.MatchesFilter(sut.Items[2]));

        sut.SearchText = "catjump"; // URL 대소문자 무시
        Assert.True(sut.MatchesFilter(sut.Items[1]));
        Assert.False(sut.MatchesFilter(sut.Items[0]));
    }

    [Fact]
    public void should_expose_decomposed_counts_when_queue_changes()
    {
        var queue = new FakeQueue();
        var sut = CreateSut(queue);
        queue.AddAndRaise(Item("https://youtu.be/1", i => { i.MarkAnalyzing(); i.MarkReady(); i.Start(); }));
        queue.AddAndRaise(Item("https://youtu.be/2", i => { i.MarkAnalyzing(); i.MarkReady(); }));
        queue.AddAndRaise(Item("https://youtu.be/3", i => { i.MarkAnalyzing(); i.Fail("x", ErrorCategory.Network); }));
        queue.AddAndRaise(Item("https://youtu.be/4", i =>
        {
            i.MarkAnalyzing(); i.MarkReady(); i.Start(); i.MarkMerging(); i.Complete(@"C:\out.mp4");
        }));

        Assert.Equal(1, sut.DownloadingCount);
        Assert.Equal(1, sut.WaitingCount);
        Assert.Equal(1, sut.CompletedCount);
        Assert.Equal(1, sut.FailedCount);
        Assert.Equal(4, sut.QueueCount);
        Assert.Equal("진행 1 · 대기 1 · 완료 1 · 실패 1", sut.StatusSummary);
    }

    [Fact]
    public void should_report_has_selection_when_any_item_checked()
    {
        var queue = new FakeQueue();
        var sut = CreateSut(queue);
        queue.AddAndRaise(Item("https://youtu.be/1")); // 기본 체크됨

        Assert.True(sut.HasSelection);
        sut.Items[0].IsChecked = false;
        Assert.False(sut.HasSelection);
    }

    [Fact]
    public void should_toggle_between_light_and_dark_when_toggle_theme_called()
    {
        var settings = new FakeSettings();
        var sut = new ShellViewModel(new FakeQueue(), settings, NullAppLogger.Instance,
            null!, new BatchUrlParser(new PlatformDetector()));

        // 사용자 피드백(2026-08-03): 순환이 아니라 라이트↔다크 토글. 아이콘은 전환 대상 표시.
        settings.Current.Theme = AppTheme.Light;
        Assert.True(sut.ShowMoonIcon);   // 라이트 → 달(다크로 전환 예고)
        sut.ToggleTheme();               // Application.Current 없음 → Apply는 no-op, 설정만 변경
        Assert.Equal(AppTheme.Dark, settings.Current.Theme);
        Assert.True(sut.ShowSunIcon);    // 다크 → 해(라이트로 전환 예고)
        sut.ToggleTheme();
        Assert.Equal(AppTheme.Light, settings.Current.Theme);
    }

    [Fact]
    public void should_leave_system_mode_when_toggle_theme_called()
    {
        var settings = new FakeSettings();
        var sut = new ShellViewModel(new FakeQueue(), settings, NullAppLogger.Instance,
            null!, new BatchUrlParser(new PlatformDetector()));

        Assert.Equal(AppTheme.System, settings.Current.Theme);
        sut.ToggleTheme(); // System은 실효 테마의 반대(Light/Dark)로 확정 전환된다
        Assert.True(settings.Current.Theme is AppTheme.Light or AppTheme.Dark);
    }

    [Fact]
    public void should_resolve_media_path_by_id_token_when_stored_name_mangled()
    {
        // CP949 stdout 훼손 재현: 저장 이름에서 간체자 일부가 빠져도 [id] 토큰으로 실제 파일을 찾는다
        var dir = Path.Combine(Path.GetTempPath(), "mpdl-resolve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var actual = Path.Combine(dir, "据说是全世界最美味的布朗尼！！！ [6a63128a000000000101f4fe].mp4");
            File.WriteAllText(actual, "x");
            var mangled = Path.Combine(dir, "据是全世界最美味的布朗尼！！！ [6a63128a000000000101f4fe].mp4");

            Assert.Equal(actual, ShellViewModel.ResolveMediaPath(mangled));
            Assert.Equal(actual, ShellViewModel.ResolveMediaPath(actual)); // 정상 경로는 그대로
            Assert.Null(ShellViewModel.ResolveMediaPath(Path.Combine(dir, "없는 파일 [deadbeef].mp4")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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
        public IReadOnlyList<DownloadItem> Items => _items;
        public event EventHandler<DownloadItem>? ItemChanged;

        public void AddAndRaise(DownloadItem item)
        {
            if (!_items.Contains(item))
                _items.Add(item);
            ItemChanged?.Invoke(this, item);
        }

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
}

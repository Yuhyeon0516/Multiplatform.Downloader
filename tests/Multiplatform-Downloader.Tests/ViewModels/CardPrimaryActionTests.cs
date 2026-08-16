using Multiplatform_Downloader.Core.Platforms;
using Multiplatform_Downloader.Core.Queue;
using Multiplatform_Downloader.ViewModels;

namespace Multiplatform_Downloader.Tests.ViewModels;

/// <summary>FR-U3.1 — 카드 주요 액션 매핑(PRD §3 표 12행) 전수 검증 (ui-redesign Phase 3).</summary>
public class CardPrimaryActionTests
{
    private static DownloadItemViewModel Vm(Action<DownloadItem> drive)
    {
        var item = new DownloadItem("https://youtu.be/" + Guid.NewGuid().ToString("N"), PlatformType.YouTube);
        drive(item);
        return new DownloadItemViewModel(item, new NoopQueue(), 0, null);
    }

    public static TheoryData<string, string, string> MappingRows() => new()
    {
        // 시나리오 키, 기대 kind, 기대 라벨 — PRD §3 매핑 표와 1:1
        { "queued", "danger", "취소" },
        { "analyzing", "danger", "취소" },
        { "ready", "accent", "받기" },
        { "downloading", "neutral", "일시정지" },
        { "paused", "accent", "재개" },
        { "merging", "none", "" },
        { "completed", "accent", "재생" }, // §9: 경로 있는 완료는 [재생] 승격, 폴더는 오버플로
        { "completedNoPath", "danger", "삭제" },
        { "failed", "accent", "재시도" },
        { "canceled", "accent", "재시도" },
        { "unavailableLogin", "accent", "로그인" },
        { "unavailableOther", "accent", "재시도" },
    };

    private static readonly Dictionary<string, Action<DownloadItem>> _drivers = new()
    {
        ["queued"] = _ => { },
        ["analyzing"] = i => i.MarkAnalyzing(),
        ["ready"] = i => { i.MarkAnalyzing(); i.MarkReady(); },
        ["downloading"] = i => { i.MarkAnalyzing(); i.MarkReady(); i.Start(); },
        ["paused"] = i => { i.MarkAnalyzing(); i.MarkReady(); i.Start(); i.Pause(); },
        ["merging"] = i => { i.MarkAnalyzing(); i.MarkReady(); i.Start(); i.MarkMerging(); },
        ["completed"] = i => { i.MarkAnalyzing(); i.MarkReady(); i.Start(); i.Complete(@"C:\o.mp4"); },
        ["completedNoPath"] = i => { i.MarkAnalyzing(); i.MarkReady(); i.Start(); i.Complete(null); },
        ["failed"] = i => { i.MarkAnalyzing(); i.Fail("x", ErrorCategory.Network); },
        ["canceled"] = i => { i.MarkAnalyzing(); i.Cancel(); },
        ["unavailableLogin"] = i => { i.MarkAnalyzing(); i.MarkUnavailable("x", ErrorCategory.LoginRequired); },
        ["unavailableOther"] = i => { i.MarkAnalyzing(); i.MarkUnavailable("x", ErrorCategory.GeoBlocked); },
    };

    [Theory]
    [MemberData(nameof(MappingRows))]
    public void should_expose_primary_action_when_status_mapped(string scenario, string kind, string label)
    {
        var vm = Vm(_drivers[scenario]);

        Assert.Equal(kind, vm.PrimaryActionKind);
        Assert.Equal(label, vm.PrimaryActionLabel);
        Assert.Equal(kind != "none", vm.CanPrimaryAction);
    }

    [Fact]
    public void should_disable_all_overflow_actions_when_merging()
    {
        // FR-U3.5 — Merging은 액션 0개: 오버플로 8개 전부 비활성이어야 한다
        var vm = Vm(_drivers["merging"]);

        Assert.False(vm.CanStartItem);
        Assert.False(vm.CanPauseItem);
        Assert.False(vm.CanResumeItem);
        Assert.False(vm.CanRetryItem);
        Assert.False(vm.CanLoginFixItem);
        Assert.False(vm.CanOpenFolderItem);
        Assert.False(vm.CanCancelItem);
        Assert.False(vm.CanRemoveItem);
        Assert.Contains("합치는 중", vm.OverflowHint);
    }

    private sealed class NoopQueue : IDownloadQueueService
    {
        public IReadOnlyList<DownloadItem> Items => [];
        public event EventHandler<DownloadItem>? ItemChanged { add { } remove { } }
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

using System.Reflection;
using Caliburn.Micro;
using Multiplatform_Downloader.Core.Abstractions;
using Multiplatform_Downloader.Core.Settings;
using Multiplatform_Downloader.Core.Update;
using Multiplatform_Downloader.Services;

namespace Multiplatform_Downloader.Tests.Update;

/// <summary>UpdateCoordinator 통합 판정 — B1(304 재사용)·H1(리마인더 영속)·수동 경로 피드백 검증.</summary>
public class UpdateCoordinatorTests
{
    // 현재 어셈블리 버전보다 확실히 높은/낮은 버전을 만들어 판정을 결정적으로.
    private static readonly Version Current =
        Assembly.GetAssembly(typeof(UpdateCoordinator))!.GetName().Version ?? new Version(2, 13, 0, 0);
    private static string Higher => $"v{Current.Major}.{Current.Minor}.{Current.Build + 1}";
    private static string Lower => $"v{Current.Major}.{Current.Minor}.{Math.Max(Current.Build - 1, 0)}";

    private static UpdateInfo Info(string tag)
    {
        VersionComparer.TryParseTag(tag, out var v);
        return new UpdateInfo(tag, v, "notes", "ShyshyroongDownloader_Setup_v9.9.9.0.exe", "https://github.com/x/s.exe", 100);
    }

    private static UpdateCoordinator Create(
        IUpdateChecker checker, ISettingsService settings, IUpdateStateStore store, FakeClock clock)
    {
        var installer = new UpdateInstaller();
        return new UpdateCoordinator(checker, installer, settings, store, clock,
            new WindowManager(), Core.Diagnostics.NullAppLogger.Instance);
    }

    [Fact] // H1 — 리마인더가 update-state.json에 영속 저장·복원되는지(재시작에도 지속)
    public void should_persist_reminder_across_reload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mpdl-us-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonUpdateStateStore(path);
            var s = store.Load();
            s.LastRemindedAtUtc = new DateTime(2026, 8, 30, 9, 0, 0, DateTimeKind.Utc);
            store.Save(s);

            var reloaded = new JsonUpdateStateStore(path).Load();
            Assert.Equal(new DateTime(2026, 8, 30, 9, 0, 0, DateTimeKind.Utc), reloaded.LastRemindedAtUtc);
        }
        finally { File.Delete(path); }
    }

    [Fact] // 수동 — 최신이면 UpToDate 피드백(FR-U6.3)
    public async Task should_report_up_to_date_when_manual_and_same_version()
    {
        var coord = Create(new FakeChecker(Info($"v{Current.Major}.{Current.Minor}.{Current.Build}")),
            new FakeSettings(), new InMemoryStore(), new FakeClock());
        Assert.Equal(ManualCheckOutcome.UpToDate, await coord.CheckManualAsync());
    }

    [Fact] // 수동 — 하위 버전도 UpToDate(다운그레이드 안내 금지)
    public async Task should_report_up_to_date_when_manual_and_lower_version()
    {
        var coord = Create(new FakeChecker(Info(Lower)), new FakeSettings(), new InMemoryStore(), new FakeClock());
        Assert.Equal(ManualCheckOutcome.UpToDate, await coord.CheckManualAsync());
    }

    [Fact] // 수동 — 체크 실패면 Failed
    public async Task should_report_failed_when_manual_check_fails()
    {
        var coord = Create(new FakeChecker(null, UpdateCheckFailure.Offline),
            new FakeSettings(), new InMemoryStore(), new FakeClock());
        Assert.Equal(ManualCheckOutcome.Failed, await coord.CheckManualAsync());
    }

    [Fact] // 자동 — 설정 off면 체커 호출 자체가 없음
    public async Task should_not_call_checker_when_auto_update_disabled()
    {
        var settings = new FakeSettings();
        settings.Current.AutoUpdateCheck = false;
        var checker = new FakeChecker(Info(Higher));
        var coord = Create(checker, settings, new InMemoryStore(), new FakeClock());
        await coord.CheckAutoAsync(windowVisible: true);
        Assert.Equal(0, checker.CallCount);
    }

    // ── Fakes ──
    private sealed class FakeChecker(UpdateInfo? info, UpdateCheckFailure failure = UpdateCheckFailure.None) : IUpdateChecker
    {
        public int CallCount { get; private set; }
        public UpdateCheckFailure LastFailure => failure;
        public Task<UpdateInfo?> FetchLatestAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(info);
        }
    }

    private sealed class InMemoryStore : IUpdateStateStore
    {
        public UpdateState State { get; private set; } = new();
        public UpdateState Load() => State;
        public void Save(UpdateState state) => State = state;
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public bool SettingsFileExisted => true;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeClock : IClock
    {
        public DateTime Now { get; set; } = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Local);
        public DateTime UtcNow { get; set; } = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
    }
}

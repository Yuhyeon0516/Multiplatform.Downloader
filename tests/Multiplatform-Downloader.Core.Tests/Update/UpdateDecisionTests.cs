using Multiplatform_Downloader.Core.Update;

namespace Multiplatform_Downloader.Tests.Update;

public class UpdateDecisionTests
{
    private static readonly Version Current = new(2, 12, 7, 0);
    private static readonly DateTime Now = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Suppress = TimeSpan.FromHours(24);

    private static UpdateNotifyResult Decide(
        string latest, string? skipped = null, DateTime? reminded = null, bool shown = false)
    {
        VersionComparer.TryParseTag(latest, out var latestV);
        return UpdateDecision.Decide(Current, latestV, skipped, reminded, Now, Suppress, shown);
    }

    [Fact] // UP-A07
    public void should_notify_when_newer_and_no_suppression()
        => Assert.Equal(UpdateNotifyResult.Notify, Decide("v2.13.0"));

    [Fact] // UP-A08
    public void should_not_notify_when_same_version()
        => Assert.Equal(UpdateNotifyResult.None, Decide("v2.12.7"));

    [Fact] // UP-A09
    public void should_not_notify_when_downgrade()
        => Assert.Equal(UpdateNotifyResult.None, Decide("v2.12.5"));

    [Fact] // UP-A11(a) — 스킵한 버전 억제
    public void should_not_notify_when_version_skipped()
        => Assert.Equal(UpdateNotifyResult.None, Decide("v2.12.8", skipped: "2.12.8"));

    [Fact] // UP-A11(b) — 스킵보다 상위는 재안내
    public void should_notify_when_newer_than_skipped()
        => Assert.Equal(UpdateNotifyResult.Notify, Decide("v2.12.9", skipped: "2.12.8"));

    [Fact] // ISS-08 — 손상된 스킵값은 '스킵 없음' 폴백
    public void should_notify_when_skipped_value_unparseable()
        => Assert.Equal(UpdateNotifyResult.Notify, Decide("v2.13.0", skipped: "garbage"));

    [Fact] // FR-U4.4 — 세션당 1회
    public void should_not_notify_when_already_shown_this_session()
        => Assert.Equal(UpdateNotifyResult.None, Decide("v2.13.0", shown: true));

    [Fact] // ISS-04 — [나중에] 24h 억제
    public void should_not_notify_when_within_remind_suppression()
        => Assert.Equal(UpdateNotifyResult.None, Decide("v2.13.0", reminded: Now.AddHours(-1)));

    [Fact] // 억제 만료 후 재안내
    public void should_notify_when_remind_suppression_expired()
        => Assert.Equal(UpdateNotifyResult.Notify, Decide("v2.13.0", reminded: Now.AddHours(-25)));
}

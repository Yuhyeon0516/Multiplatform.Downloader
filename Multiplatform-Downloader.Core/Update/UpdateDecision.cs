namespace Multiplatform_Downloader.Core.Update;

/// <summary>업데이트 안내 판정 결과.</summary>
public enum UpdateNotifyResult
{
    /// <summary>안내하지 않음 — 최신·다운그레이드·스킵·억제·세션중복 등.</summary>
    None,
    /// <summary>안내 대상 — 상위 버전이고 억제 조건에 걸리지 않음.</summary>
    Notify,
}

/// <summary>
/// 자동 경로의 안내 여부를 결정하는 순수 함수(FR-U1.3, FR-U4.4). 부작용·시간 직접 접근 없음.
/// 입력만으로 결정되어 단위 테스트가 용이하다.
/// </summary>
public static class UpdateDecision
{
    /// <param name="current">현재 앱 버전</param>
    /// <param name="latest">원격 최신 버전(파싱 성공분만 전달)</param>
    /// <param name="skippedVersion">사용자가 [건너뛰기]한 버전 문자열(없으면 null). 파싱 불가 시 '스킵 없음'으로 폴백.</param>
    /// <param name="lastRemindedAtUtc">마지막으로 [나중에]/닫기한 UTC 시각(없으면 null).</param>
    /// <param name="nowUtc">현재 UTC(IClock 주입).</param>
    /// <param name="remindSuppression">[나중에] 후 재안내 억제 기간.</param>
    /// <param name="shownThisSession">이번 세션에 이미 안내했는가.</param>
    public static UpdateNotifyResult Decide(
        Version current,
        Version latest,
        string? skippedVersion,
        DateTime? lastRemindedAtUtc,
        DateTime nowUtc,
        TimeSpan remindSuppression,
        bool shownThisSession)
    {
        // 상위 버전이 아니면(동일·다운그레이드) 안내 없음
        if (!VersionComparer.IsNewer(latest, current))
            return UpdateNotifyResult.None;

        // 세션당 1회
        if (shownThisSession)
            return UpdateNotifyResult.None;

        // [건너뛰기]한 버전 — 단, 그보다 상위가 오면 재안내(스킵은 해당 버전만 유효)
        if (VersionComparer.TryParseTag(skippedVersion, out var skipped)
            && VersionComparer.Compare(latest, skipped) <= 0)
            return UpdateNotifyResult.None;

        // [나중에] 억제 기간 내면 보류
        if (lastRemindedAtUtc is { } reminded && nowUtc - reminded < remindSuppression)
            return UpdateNotifyResult.None;

        return UpdateNotifyResult.Notify;
    }
}

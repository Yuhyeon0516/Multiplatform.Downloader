namespace Multiplatform_Downloader.Core.Update;

/// <summary>
/// 업데이트 체커의 캐시·백오프·리마인더 상태(NFR-U4). 사용자 설정과 분리해 update-state.json에 저장한다
/// (작성자가 체커/코디네이터뿐이라 설정 다이얼로그와의 read-modify-write 경합이 없다).
/// </summary>
public sealed class UpdateState
{
    /// <summary>마지막 200 응답의 ETag(If-None-Match 조건부 요청용 — 대역폭 절약).</summary>
    public string? ETag { get; set; }

    /// <summary>마지막으로 HTTP 체크에 성공한 시각(UTC). 진단·표시용 — 리마인더와 분리(H1).</summary>
    public DateTime? LastCheckUtc { get; set; }

    /// <summary>사용자가 [나중에]/닫기를 누른 시각(UTC). 24h 재안내 억제 판정용(FR-U4.4). 재시작에도 지속.</summary>
    public DateTime? LastRemindedAtUtc { get; set; }

    /// <summary>rate limit(403) 리셋 시각(UTC). 이 시각 전에는 HTTP 요청을 생략한다.</summary>
    public DateTime? RateLimitResetUtc { get; set; }

    // ── 304 재사용을 위한 마지막 성공 파싱 결과 캐시(B1) ──
    public string? CachedTag { get; set; }
    public string? CachedNotes { get; set; }
    public string? CachedAssetName { get; set; }
    public string? CachedDownloadUrl { get; set; }
    public long CachedAssetSize { get; set; }

    /// <summary>캐시된 릴리스 정보를 <see cref="UpdateInfo"/>로 복원한다(304 경로). 불완전하면 null.</summary>
    public UpdateInfo? ToCachedInfo()
    {
        if (string.IsNullOrEmpty(CachedTag)
            || string.IsNullOrEmpty(CachedAssetName)
            || string.IsNullOrEmpty(CachedDownloadUrl)
            || CachedAssetSize <= 0
            || !VersionComparer.TryParseTag(CachedTag, out var version))
            return null;
        return new UpdateInfo(CachedTag, version, CachedNotes ?? string.Empty, CachedAssetName, CachedDownloadUrl, CachedAssetSize);
    }

    /// <summary>성공 파싱 결과를 캐시에 저장한다.</summary>
    public void CacheInfo(UpdateInfo info)
    {
        CachedTag = info.TagName;
        CachedNotes = info.ReleaseNotes;
        CachedAssetName = info.AssetName;
        CachedDownloadUrl = info.DownloadUrl;
        CachedAssetSize = info.AssetSize;
    }
}

/// <summary>업데이트 상태 영속(FR-U2.4, NFR-U4).</summary>
public interface IUpdateStateStore
{
    UpdateState Load();
    void Save(UpdateState state);
}

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Multiplatform_Downloader.Core.Abstractions;
using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Net;

namespace Multiplatform_Downloader.Core.Update;

/// <summary>최신 릴리스를 조회한다(FR-U2). 실패는 예외 대신 결과로 표현한다(자동 경로 무소음 정책).</summary>
public interface IUpdateChecker
{
    /// <summary>최신 릴리스 정보를 조회한다. 실패·최신 없음은 null. 실패 사유는 <see cref="LastFailure"/>.</summary>
    Task<UpdateInfo?> FetchLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>직전 조회 실패 분류(수동 경로 피드백용). 성공 시 None.</summary>
    UpdateCheckFailure LastFailure { get; }
}

/// <summary>업데이트 체크 실패 분류(수동 경로 피드백용, FR-U6.3).</summary>
public enum UpdateCheckFailure
{
    None,
    Offline,
    RateLimited,
    ServerError,
    NotFound,
    MalformedResponse,
    NoAsset,
}

/// <summary>
/// GitHub Releases의 최신 릴리스를 조회하는 체커(FR-U2). 가드 HttpClient, User-Agent 필수,
/// ETag 조건부 요청, rate limit 백오프, 응답 1MB 상한, 전 실패 조용한 처리.
/// </summary>
public sealed partial class GitHubUpdateChecker : IUpdateChecker, IDisposable
{
    private const string LatestUrl = "https://api.github.com/repos/ghlee0786/Multiplatform.Downloader/releases/latest";
    private const long MaxResponseBytes = 1024 * 1024; // 1MB — 정상 latest JSON은 수 KB (FR-U2.4)
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    // 자산명 앵커드 검증: ShyshyroongDownloader_Setup_v2.13.0.0.exe 형태만 허용 (FR-U2.2)
    [GeneratedRegex(@"^ShyshyroongDownloader_Setup_v\d{1,9}\.\d{1,9}\.\d{1,9}(?:\.\d{1,9})?\.exe$", RegexOptions.CultureInvariant)]
    private static partial Regex AssetPattern();

    private readonly HttpClient _http;
    private readonly bool _ownsHandler;
    private readonly IUpdateStateStore _stateStore;
    private readonly IClock _clock;
    private readonly IAppLogger _logger;

    /// <inheritdoc />
    public UpdateCheckFailure LastFailure { get; private set; }

    public GitHubUpdateChecker(
        IUpdateStateStore stateStore,
        IClock clock,
        IAppLogger? logger = null,
        HttpMessageHandler? handler = null)
    {
        _stateStore = stateStore;
        _clock = clock;
        _logger = logger ?? NullAppLogger.Instance;
        _ownsHandler = handler is null;
        _http = new HttpClient(handler ?? SsrfGuard.CreateGuardedHandler(allowAutoRedirect: true), disposeHandler: _ownsHandler)
        {
            Timeout = RequestTimeout,
            MaxResponseContentBufferSize = MaxResponseBytes,
        };
        // api.github.com은 User-Agent 없으면 403. 제품 식별자 사용.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Shyshyroong-Downloader-Updater");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<UpdateInfo?> FetchLatestAsync(CancellationToken cancellationToken = default)
    {
        LastFailure = UpdateCheckFailure.None;
        var state = _stateStore.Load();

        // rate limit 백오프 — 리셋 전에는 요청 자체를 보내지 않는다 (FR-U2.4)
        if (state.RateLimitResetUtc is { } reset && _clock.UtcNow < reset)
        {
            _logger.Info("Update", $"rate limit 리셋 전 — 체크 생략(리셋 {reset:u})");
            LastFailure = UpdateCheckFailure.RateLimited;
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
            if (!string.IsNullOrEmpty(state.ETag))
                request.Headers.TryAddWithoutValidation("If-None-Match", state.ETag);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            state.LastCheckUtc = _clock.UtcNow;

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                _stateStore.Save(state);
                _logger.Info("Update", "304 — 변경 없음(캐시 재사용)");
                // B1: 캐시된 파싱 결과를 반환해야 상위 계층의 재안내(24h)·스킵 판정이 계속 동작한다.
                return state.ToCachedInfo();
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                var remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var rem) ? rem.FirstOrDefault() : null;
                var isRateLimited = remaining == "0"
                    || response.Headers.RetryAfter is not null; // M3: secondary rate limit은 Retry-After로 통지
                if (isRateLimited)
                {
                    if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetVals)
                        && long.TryParse(resetVals.FirstOrDefault(), out var epoch))
                        state.RateLimitResetUtc = DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
                    else if (response.Headers.RetryAfter?.Delta is { } delta)
                        state.RateLimitResetUtc = _clock.UtcNow + delta;
                    else if (response.Headers.RetryAfter?.Date is { } date)
                        state.RateLimitResetUtc = date.UtcDateTime;
                    _stateStore.Save(state);
                    _logger.Warning("Update", "요청 한도 초과(403) — 리셋까지 대기");
                    LastFailure = UpdateCheckFailure.RateLimited;
                    return null;
                }
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _stateStore.Save(state);
                _logger.Warning("Update", "업데이트 소스 접근 불가(404)");
                LastFailure = UpdateCheckFailure.NotFound;
                return null;
            }

            if ((int)response.StatusCode >= 500)
            {
                _logger.Warning("Update", $"서버 오류({(int)response.StatusCode})");
                LastFailure = UpdateCheckFailure.ServerError;
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning("Update", $"예상 밖 상태코드({(int)response.StatusCode})");
                LastFailure = UpdateCheckFailure.MalformedResponse;
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var info = ParseRelease(body);

            if (response.Headers.ETag is not null)
                state.ETag = response.Headers.ETag.Tag;
            if (info is not null)
                state.CacheInfo(info); // B1: 304 재사용을 위해 성공 파싱 결과를 캐시
            _stateStore.Save(state);
            return info;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Info("Update", "타임아웃");
            LastFailure = UpdateCheckFailure.Offline;
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.Info("Update", $"오프라인·네트워크 오류: {ex.Message}");
            LastFailure = UpdateCheckFailure.Offline;
            return null;
        }
        catch (SsrfBlockedException ex)
        {
            _logger.Warning("Update", $"차단된 주소: {ex.Message}");
            LastFailure = UpdateCheckFailure.MalformedResponse;
            return null;
        }
    }

    private UpdateInfo? ParseRelease(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // 이중 방어 — /releases/latest는 draft/prerelease를 제외하지만 스키마·프록시 오염 대비
            if (TryBool(root, "draft") || TryBool(root, "prerelease"))
            {
                _logger.Warning("Update", "draft/prerelease 응답 — 스킵");
                LastFailure = UpdateCheckFailure.MalformedResponse;
                return null;
            }

            var tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;
            if (!VersionComparer.TryParseTag(tag, out var version))
            {
                _logger.Warning("Update", $"태그 파싱 불가: '{tag}'");
                LastFailure = UpdateCheckFailure.MalformedResponse;
                return null;
            }

            var notes = root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String
                ? b.GetString() ?? string.Empty
                : string.Empty;

            // 자산 선택 — 첫 자산 하드코딩 금지, 이름 패턴 매칭
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                _logger.Warning("Update", "assets 없음");
                LastFailure = UpdateCheckFailure.NoAsset;
                return null;
            }

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(name) || !AssetPattern().IsMatch(name))
                    continue;
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
                if (string.IsNullOrEmpty(url) || size <= 0)
                    continue;
                return new UpdateInfo(tag!, version, notes, name, url, size);
            }

            _logger.Warning("Update", "자산명 패턴 매칭 실패");
            LastFailure = UpdateCheckFailure.NoAsset;
            return null;
        }
        catch (JsonException ex)
        {
            var snippet = body.Length > 200 ? body[..200] : body;
            _logger.Warning("Update", $"JSON 파싱 실패: {ex.Message} | {snippet}");
            LastFailure = UpdateCheckFailure.MalformedResponse;
            return null;
        }
    }

    private static bool TryBool(JsonElement root, string prop)
        => root.TryGetProperty(prop, out var e)
            && e.ValueKind == JsonValueKind.True;

    public void Dispose() => _http.Dispose();
}

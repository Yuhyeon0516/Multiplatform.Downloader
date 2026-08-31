using System.Net;
using Multiplatform_Downloader.Core.Abstractions;
using Multiplatform_Downloader.Core.Update;

namespace Multiplatform_Downloader.Tests.Update;

public class GitHubUpdateCheckerTests
{
    // 자산 선택은 OS별로 다르다(Windows: Setup exe / macOS: tar.gz + .sha256 사이드카) — 실행 OS에 맞는 픽스처 생성
    private static string ExpectedAssetName => OperatingSystem.IsMacOS()
        ? GitHubUpdateChecker.MacAssetName
        : "ShyshyroongDownloader_Setup_v2.13.0.0.exe";

    private static readonly string Body200 = $$"""
    {
      "tag_name": "v2.13.0",
      "draft": false,
      "prerelease": false,
      "body": "릴리스 노트",
      "assets": [
        { "name": "Source.zip", "browser_download_url": "https://x/s.zip", "size": 10 },
        { "name": "{{ExpectedAssetName}}", "browser_download_url": "https://github.com/x/releases/download/v2.13.0/{{ExpectedAssetName}}", "size": 176781304 },
        { "name": "{{ExpectedAssetName}}.sha256", "browser_download_url": "https://github.com/x/releases/download/v2.13.0/{{ExpectedAssetName}}.sha256", "size": 100 }
      ]
    }
    """;

    private static GitHubUpdateChecker Create(HttpStatusCode code, string? body = null,
        Action<HttpResponseMessage>? decorate = null, FakeClock? clock = null, InMemoryStore? store = null)
    {
        var handler = new StubHandler(code, body, decorate);
        return new GitHubUpdateChecker(store ?? new InMemoryStore(), clock ?? new FakeClock(), null, handler);
    }

    [Fact] // UP-A13, A07 — 정상 파싱 + 자산 패턴 매칭(첫 자산 하드코딩 아님)
    public async Task should_return_info_when_valid_release()
    {
        using var sut = Create(HttpStatusCode.OK, Body200);
        var info = await sut.FetchLatestAsync();
        Assert.NotNull(info);
        Assert.Equal("v2.13.0", info!.TagName);
        Assert.Equal(ExpectedAssetName, info.AssetName); // Source.zip 아님
        Assert.Equal(176781304, info.AssetSize);
    }

    [Fact] // UP-A13(b) — prerelease 이중 방어
    public async Task should_skip_when_prerelease_true()
    {
        var body = Body200.Replace("\"prerelease\": false", "\"prerelease\": true");
        using var sut = Create(HttpStatusCode.OK, body);
        Assert.Null(await sut.FetchLatestAsync());
    }

    [Fact] // UP-A15 — 자산명 불일치
    public async Task should_return_null_when_no_matching_asset()
    {
        var body = """{ "tag_name": "v2.13.0", "assets": [ { "name": "checksums.txt", "browser_download_url": "https://x/c.txt", "size": 5 } ] }""";
        using var sut = Create(HttpStatusCode.OK, body);
        var s = new InMemoryStore();
        using var sut2 = Create(HttpStatusCode.OK, body, store: s);
        Assert.Null(await sut2.FetchLatestAsync());
        Assert.Equal(UpdateCheckFailure.NoAsset, sut2.LastFailure);
    }

    [Fact] // UP-A17 — 404
    public async Task should_return_null_when_not_found()
    {
        using var sut = Create(HttpStatusCode.NotFound);
        Assert.Null(await sut.FetchLatestAsync());
        Assert.Equal(UpdateCheckFailure.NotFound, sut.LastFailure);
    }

    [Fact] // UP-A16 — 비JSON 응답
    public async Task should_return_null_when_malformed_body()
    {
        using var sut = Create(HttpStatusCode.OK, "<html>error</html>");
        Assert.Null(await sut.FetchLatestAsync());
        Assert.Equal(UpdateCheckFailure.MalformedResponse, sut.LastFailure);
    }

    [Fact] // UP-A18 — 403 rate limit → 리셋 기록
    public async Task should_record_reset_when_rate_limited()
    {
        var reset = DateTimeOffset.FromUnixTimeSeconds(2_000_000_000).ToUnixTimeSeconds();
        var store = new InMemoryStore();
        using var sut = Create(HttpStatusCode.Forbidden, "", r =>
        {
            r.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            r.Headers.TryAddWithoutValidation("X-RateLimit-Reset", reset.ToString());
        }, store: store);

        Assert.Null(await sut.FetchLatestAsync());
        Assert.Equal(UpdateCheckFailure.RateLimited, sut.LastFailure);
        Assert.NotNull(store.State.RateLimitResetUtc);
    }

    [Fact] // UP-A18(2) — 리셋 전에는 요청 생략
    public async Task should_skip_request_when_before_rate_limit_reset()
    {
        var clock = new FakeClock { UtcNow = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc) };
        var store = new InMemoryStore();
        store.State.RateLimitResetUtc = clock.UtcNow.AddHours(1); // 아직 리셋 전
        var handler = new StubHandler(HttpStatusCode.OK, Body200, null);
        using var sut = new GitHubUpdateChecker(store, clock, null, handler);

        Assert.Null(await sut.FetchLatestAsync());
        Assert.Equal(0, handler.CallCount); // HTTP 요청 자체가 없어야 함
    }

    [Fact] // UP-A22 — ETag 조건부 요청 + 304
    public async Task should_send_if_none_match_when_etag_present()
    {
        var store = new InMemoryStore();
        store.State.ETag = "\"abc\"";
        var handler = new StubHandler(HttpStatusCode.NotModified, null, null);
        using var sut = new GitHubUpdateChecker(store, new FakeClock(), null, handler);

        await sut.FetchLatestAsync();
        Assert.Equal("\"abc\"", handler.LastIfNoneMatch);
    }

    [Fact] // B1 — 304는 캐시된 릴리스 정보를 반환해야 재안내/스킵 판정이 계속 동작
    public async Task should_return_cached_info_when_304()
    {
        var store = new InMemoryStore();
        // 1차 200으로 캐시 채우기
        using (var warm = Create(HttpStatusCode.OK, Body200, store: store))
            Assert.NotNull(await warm.FetchLatestAsync());
        Assert.NotNull(store.State.CachedTag);

        // 2차 304 — 캐시 재사용
        var handler = new StubHandler(HttpStatusCode.NotModified, null, null);
        using var sut = new GitHubUpdateChecker(store, new FakeClock(), null, handler);
        var info = await sut.FetchLatestAsync();
        Assert.NotNull(info);
        Assert.Equal("v2.13.0", info!.TagName);
        // macOS는 SHA256 검증 필수 — 캐시 왕복 후에도 체크섬 URL이 보존돼야 설치가 가능하다
        if (OperatingSystem.IsMacOS())
            Assert.NotNull(info.ChecksumUrl);
    }

    [Fact] // M3 — Retry-After 헤더로 통지되는 secondary rate limit 백오프
    public async Task should_record_reset_when_retry_after_header()
    {
        var store = new InMemoryStore();
        using var sut = Create(HttpStatusCode.Forbidden, "", r =>
            r.Headers.TryAddWithoutValidation("Retry-After", "3600"), store: store);
        Assert.Null(await sut.FetchLatestAsync());
        Assert.Equal(UpdateCheckFailure.RateLimited, sut.LastFailure);
        Assert.NotNull(store.State.RateLimitResetUtc);
    }

    // ── Fakes ──
    private sealed class StubHandler(HttpStatusCode code, string? body, Action<HttpResponseMessage>? decorate)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? LastIfNoneMatch { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (request.Headers.TryGetValues("If-None-Match", out var v))
                LastIfNoneMatch = v.FirstOrDefault();
            var resp = new HttpResponseMessage(code) { RequestMessage = request };
            if (body is not null)
                resp.Content = new StringContent(body);
            decorate?.Invoke(resp);
            return Task.FromResult(resp);
        }
    }

    private sealed class InMemoryStore : IUpdateStateStore
    {
        public UpdateState State { get; private set; } = new();
        public UpdateState Load() => State;
        public void Save(UpdateState state) => State = state;
    }

    private sealed class FakeClock : IClock
    {
        public DateTime Now { get; set; } = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Local);
        public DateTime UtcNow { get; set; } = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
    }
}

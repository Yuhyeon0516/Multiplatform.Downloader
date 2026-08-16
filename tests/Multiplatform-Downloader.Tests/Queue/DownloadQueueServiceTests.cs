using System.Diagnostics;
using Multiplatform_Downloader.Core.Engine;
using Multiplatform_Downloader.Core.Models;
using Multiplatform_Downloader.Core.Platforms;
using Multiplatform_Downloader.Core.Queue;
using Multiplatform_Downloader.Core.Settings;

namespace Multiplatform_Downloader.Tests.Queue;

public class DownloadQueueServiceTests
{
    private static DownloadQueueService CreateSut(
        IMediaMetadataService? metadata = null,
        IDownloadEngine? engine = null,
        ISettingsService? settings = null,
        IXhsResolutionStrategy? xhsStrategy = null,
        IDirectStreamDownloader? directDownloader = null,
        IThreadsResolutionStrategy? threadsStrategy = null)
    {
        return new DownloadQueueService(
            new BatchUrlParser(new PlatformDetector()),
            metadata ?? new FakeMetadata(),
            engine ?? new FakeEngine(success: true),
            settings ?? new FakeSettings(),
            new MediaFormatSelector(),
            resolveUrl: null,
            logger: null,
            xhsStrategy: xhsStrategy,
            directDownloader: directDownloader,
            threadsStrategy: threadsStrategy,
            retryDelayBase: TimeSpan.Zero); // 테스트 결정성 — 백오프 대기 없음
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(10);
        Assert.True(condition(), "조건이 제한 시간 내에 충족되지 않았습니다.");
    }

    /// <summary>등록 → 분석 완료(Ready) 대기 → 수동 다운로드 시작(신규 동작 흐름 헬퍼).</summary>
    private static async Task<DownloadItem> EnqueueAndStartAsync(DownloadQueueService sut, string url)
    {
        var item = sut.Enqueue(url).Added[0];
        await WaitForAsync(() => item.Status is DownloadStatus.Ready or DownloadStatus.Failed);
        if (item.Status == DownloadStatus.Ready)
            sut.Start(item.Id);
        return item;
    }

    [Fact]
    public void should_enqueue_valid_and_reject_invalid()
    {
        using var sut = CreateSut();

        var result = sut.Enqueue("https://youtu.be/abc\nhttps://unknown.com/x");

        Assert.Single(result.Added);
        Assert.Single(result.Rejected);
    }

    [Fact]
    public async Task should_auto_start_download_when_option_enabled()
    {
        // FR-N3.2: 자동 다운로드 옵션 켜짐 → 분석 완료(Ready) 즉시 다운로드 시작
        var settings = new FakeSettings();
        settings.Current.AutoStartDownload = true;
        using var sut = CreateSut(settings: settings);

        var item = sut.Enqueue("https://youtu.be/abc").Added[0];

        // 대기 없이 Downloading/Completed로 진행해야 함(사용자 Start 호출 없이)
        await WaitForAsync(() => item.Status is DownloadStatus.Downloading or DownloadStatus.Completed);
    }

    [Fact]
    public async Task should_wait_at_ready_when_auto_start_disabled()
    {
        // FR-N3.1 기본값: 옵션 꺼짐 → Ready에서 대기(기존 동작 유지)
        var settings = new FakeSettings();
        settings.Current.AutoStartDownload = false;
        using var sut = CreateSut(settings: settings);

        var item = sut.Enqueue("https://youtu.be/abc").Added[0];

        await WaitForAsync(() => item.Status == DownloadStatus.Ready);
        await Task.Delay(80); // 자동 시작이 없음을 확인
        Assert.Equal(DownloadStatus.Ready, item.Status);
    }

    [Fact]
    public void should_restore_completed_when_file_exists()
    {
        // 사용자 요청: 재시작 후 받은 항목은 폴더의 파일 존재를 대조해 완료로 복원(재분석 없음)
        var file = Path.Combine(Path.GetTempPath(), $"mpdl-restore-{Guid.NewGuid():N}.mp4");
        File.WriteAllText(file, "video");
        try
        {
            using var sut = CreateSut();
            var raised = new List<DownloadItem>();
            sut.ItemChanged += (_, i) => raised.Add(i);
            var snapshot = new QueueItemSnapshot(
                Guid.NewGuid(), "https://youtu.be/done", PlatformType.YouTube,
                null, "받은 영상", null, "22", DownloadStatus.Completed, ExtractionRoute.YtDlp, file);

            sut.RestoreCompleted(snapshot);

            var item = Assert.Single(sut.Items);
            Assert.Equal(DownloadStatus.Completed, item.Status);
            Assert.Equal(file, item.OutputFilePath);
            Assert.Equal("받은 영상", item.Title);
            Assert.Single(raised); // 카드 생성 이벤트
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void should_mark_unavailable_when_restored_file_missing()
    {
        // 받은 파일을 사용자가 폴더에서 삭제/이동 → 완료로 속이지 않고 안내 + [재시도] 가능 상태
        using var sut = CreateSut();
        var snapshot = new QueueItemSnapshot(
            Guid.NewGuid(), "https://youtu.be/gone", PlatformType.YouTube,
            null, "지워진 영상", null, "22", DownloadStatus.Completed, ExtractionRoute.YtDlp,
            Path.Combine(Path.GetTempPath(), $"mpdl-missing-{Guid.NewGuid():N}.mp4"));

        sut.RestoreCompleted(snapshot);

        var item = Assert.Single(sut.Items);
        Assert.Equal(DownloadStatus.Unavailable, item.Status);
        Assert.Contains("폴더에 없습니다", item.ErrorMessage);
        Assert.Null(item.SelectedFormatId); // [재시도]가 재분석부터 타도록 포맷 미복원
    }

    [Fact]
    public void should_restore_completed_when_output_path_unknown()
    {
        // 경로를 못 얻은 완료(H3)는 대조 불가 — 받았다는 사실을 보존해 완료로 복원
        using var sut = CreateSut();
        var snapshot = new QueueItemSnapshot(
            Guid.NewGuid(), "https://youtu.be/nopath", PlatformType.YouTube,
            null, null, null, null, DownloadStatus.Completed, ExtractionRoute.YtDlp, null);

        sut.RestoreCompleted(snapshot);

        Assert.Equal(DownloadStatus.Completed, Assert.Single(sut.Items).Status);
    }

    [Fact]
    public void should_skip_when_over_queue_capacity()
    {
        using var sut = CreateSut(engine: new BlockingEngine()); // 완료되지 않게 막아 active 유지
        var urls = string.Join('\n', Enumerable.Range(0, 20).Select(i => $"https://www.youtube.com/watch?v=vid{i}"));

        var result = sut.Enqueue(urls);

        Assert.Equal(15, result.Added.Count);      // MaxQueueItems 기본 15
        Assert.Equal(5, result.SkippedOverCapacity);
    }

    [Fact]
    public async Task should_stop_at_ready_and_not_auto_download_when_enqueued()
    {
        // 신규 동작: 등록은 분석까지만. 다운로드는 사용자가 Start를 호출하기 전까지 시작되지 않는다.
        var engine = new FakeEngine(success: true);
        using var sut = CreateSut(engine: engine);

        var item = sut.Enqueue("https://youtu.be/abc").Added[0];

        await WaitForAsync(() => item.Status == DownloadStatus.Ready);
        await Task.Delay(150); // 자동으로 다운로드가 시작되지 않는지 잠시 관찰
        Assert.Equal(DownloadStatus.Ready, item.Status);
        Assert.Equal(0, engine.CallCount); // 엔진(다운로드)이 호출되지 않았다
    }

    [Fact]
    public async Task should_download_when_start_called_on_ready_item()
    {
        using var sut = CreateSut(engine: new FakeEngine(success: true));

        var item = sut.Enqueue("https://youtu.be/abc").Added[0];
        await WaitForAsync(() => item.Status == DownloadStatus.Ready);
        sut.Start(item.Id);

        await WaitForAsync(() => item.Status == DownloadStatus.Completed);
        Assert.Equal(@"D:\out.mp4", item.OutputFilePath);
    }

    [Fact]
    public async Task should_start_all_ready_items_when_start_all_called()
    {
        var engine = new BlockingEngine();
        using var sut = CreateSut(engine: engine, settings: SettingsWithConcurrency(3));

        var urls = string.Join('\n', Enumerable.Range(0, 3).Select(i => $"https://www.youtube.com/watch?v=r{i}"));
        var added = sut.Enqueue(urls).Added;
        await WaitForAsync(() => added.All(i => i.Status == DownloadStatus.Ready));

        sut.StartAll();

        await WaitForAsync(() => added.All(i => i.Status == DownloadStatus.Downloading));
        engine.ReleaseAll();
    }

    [Fact]
    public async Task should_complete_item_when_pipeline_succeeds()
    {
        using var sut = CreateSut(engine: new FakeEngine(success: true));

        var item = await EnqueueAndStartAsync(sut, "https://youtu.be/abc");

        await WaitForAsync(() => item.Status == DownloadStatus.Completed);
        Assert.Equal(@"D:\out.mp4", item.OutputFilePath);
    }

    [Fact]
    public async Task should_select_format_during_analysis()
    {
        using var sut = CreateSut();

        var item = sut.Enqueue("https://youtu.be/abc").Added[0];

        // 포맷 선택은 분석 단계에서 완료된다 — 다운로드 없이 Ready 상태에서 확인
        await WaitForAsync(() => item.Status == DownloadStatus.Ready);
        Assert.Equal("137", item.SelectedFormatId);
        Assert.Equal("테스트", item.Title);
    }

    [Fact]
    public async Task should_complete_when_engine_succeeds_without_path() // H3 회귀
    {
        using var sut = CreateSut(engine: new PathlessSuccessEngine());

        var item = await EnqueueAndStartAsync(sut, "https://youtu.be/abc");

        await WaitForAsync(() => item.Status is DownloadStatus.Completed or DownloadStatus.Failed);
        Assert.Equal(DownloadStatus.Completed, item.Status); // 경로가 없어도 성공은 성공
    }

    [Fact]
    public async Task should_fail_item_when_engine_fails()
    {
        using var sut = CreateSut(engine: new FakeEngine(success: false));

        var item = await EnqueueAndStartAsync(sut, "https://youtu.be/abc");

        await WaitForAsync(() => item.Status == DownloadStatus.Failed);
        Assert.Equal(ErrorCategory.EngineFailure, item.LastErrorCategory);
    }

    [Fact]
    public async Task should_mark_unavailable_when_metadata_fails_definitively()
    {
        // FR-D2.4: 분류상 확정 실패(재시도 불가)는 즉시 '다운로드 불가'
        using var sut = CreateSut(metadata: new ThrowingMetadata());

        var item = sut.Enqueue("https://youtu.be/abc").Added[0];

        await WaitForAsync(() => item.Status == DownloadStatus.Unavailable);
        Assert.NotNull(item.ErrorMessage);
    }

    [Fact]
    public async Task should_retry_analysis_when_network_failure_then_succeed()
    {
        // FR-D2.1: 네트워크 계열만 자동 재시도 — 2회 실패 후 3번째 성공
        var metadata = new FlakyNetworkMetadata(failCount: 2);
        using var sut = CreateSut(metadata: metadata); // 기본 retry=2

        var item = sut.Enqueue("https://youtu.be/abc").Added[0];

        await WaitForAsync(() => item.Status == DownloadStatus.Ready);
        Assert.Equal(3, metadata.CallCount); // 1 + 재시도 2
    }

    [Fact]
    public async Task should_mark_unavailable_when_network_retries_exhausted()
    {
        var metadata = new FlakyNetworkMetadata(failCount: 99); // 항상 네트워크 실패
        var settings = new FakeSettings();
        settings.Current.AnalysisRetryCount = 1;
        using var sut = CreateSut(metadata: metadata, settings: settings);

        var item = sut.Enqueue("https://youtu.be/abc").Added[0];

        await WaitForAsync(() => item.Status == DownloadStatus.Unavailable);
        Assert.Equal(2, metadata.CallCount); // 1 + 재시도 1 (소진)
    }

    [Fact]
    public async Task should_not_retry_definitive_failure()
    {
        // NFR-D2: 확정 실패(Unsupported 등)는 재시도하지 않는다 — 플랫폼 부하 방지
        var metadata = new DefinitiveFailMetadata();
        using var sut = CreateSut(metadata: metadata);

        var item = sut.Enqueue("https://youtu.be/abc").Added[0];

        await WaitForAsync(() => item.Status == DownloadStatus.Unavailable);
        Assert.Equal(1, metadata.CallCount); // 재시도 없음
    }

    [Fact]
    public async Task should_set_login_required_category_when_bot_check_failure()
    {
        // FR-L1: 봇 확인/로그인 실패는 LoginRequired 카테고리 — 카드 [로그인] 버튼 노출 기준
        using var sut = CreateSut(metadata: new LoginFailMetadata());

        var item = sut.Enqueue("https://youtu.be/abc").Added[0];

        await WaitForAsync(() => item.Status == DownloadStatus.Unavailable);
        Assert.Equal(ErrorCategory.LoginRequired, item.LastErrorCategory);
    }

    [Fact]
    public async Task should_not_set_category_when_unsupported_url_failure()
    {
        // FR-L1: 로그인으로 해결 불가능한 실패에는 카테고리를 남기지 않는다
        using var sut = CreateSut(metadata: new DefinitiveFailMetadata());

        var item = sut.Enqueue("https://youtu.be/abc").Added[0];

        await WaitForAsync(() => item.Status == DownloadStatus.Unavailable);
        Assert.Null(item.LastErrorCategory);
    }

    [Fact]
    public async Task should_retry_unavailable_item_via_reanalysis()
    {
        // Unavailable → Retry → 분석부터 재수행(신선한 썸네일 URL 확보, FR-D1.5)
        var metadata = new FlakyNetworkMetadata(failCount: 99);
        var settings = new FakeSettings();
        settings.Current.AnalysisRetryCount = 0;
        using var sut = CreateSut(metadata: metadata, settings: settings);

        var item = sut.Enqueue("https://youtu.be/abc").Added[0];
        await WaitForAsync(() => item.Status == DownloadStatus.Unavailable);

        metadata.StopFailing(); // 이제 성공하게
        sut.Retry(item.Id);

        await WaitForAsync(() => item.Status == DownloadStatus.Ready);
        Assert.Equal("테스트", item.Title);
    }

    [Fact]
    public async Task should_raise_item_changed_events()
    {
        using var sut = CreateSut();
        var statuses = new List<DownloadStatus>();
        sut.ItemChanged += (_, item) => { lock (statuses) statuses.Add(item.Status); };

        var item = await EnqueueAndStartAsync(sut, "https://youtu.be/abc");
        await WaitForAsync(() => item.Status == DownloadStatus.Completed);

        lock (statuses)
        {
            Assert.Contains(DownloadStatus.Analyzing, statuses);
            Assert.Contains(DownloadStatus.Ready, statuses);
            Assert.Contains(DownloadStatus.Completed, statuses);
        }
    }

    [Fact]
    public async Task should_limit_concurrency_to_max()
    {
        var engine = new BlockingEngine();
        using var sut = CreateSut(engine: engine, settings: SettingsWithConcurrency(2));

        var urls = string.Join('\n', Enumerable.Range(0, 4).Select(i => $"https://www.youtube.com/watch?v=v{i}"));
        var added = sut.Enqueue(urls).Added;
        await WaitForAsync(() => added.All(i => i.Status == DownloadStatus.Ready));
        sut.StartAll();

        // 동시에 2개만 Downloading에 도달
        await WaitForAsync(() => engine.CurrentConcurrent == 2);
        await Task.Delay(100); // 초과 진입이 없는지 잠시 관찰
        Assert.True(engine.MaxObservedConcurrent <= 2);

        engine.ReleaseAll(); // 정리
    }

    [Fact]
    public async Task should_pause_and_resume_item()
    {
        var engine = new BlockingEngine();
        using var sut = CreateSut(engine: engine);

        var item = sut.Enqueue("https://youtu.be/abc").Added[0];
        await WaitForAsync(() => item.Status == DownloadStatus.Ready);
        sut.Start(item.Id);
        await WaitForAsync(() => item.Status == DownloadStatus.Downloading);

        sut.Pause(item.Id);
        await WaitForAsync(() => item.Status == DownloadStatus.Paused);

        engine.SetSuccessOnNext();
        sut.Resume(item.Id);
        await WaitForAsync(() => item.Status is DownloadStatus.Downloading or DownloadStatus.Completed);
    }

    [Fact]
    public async Task should_route_xhs_to_fallback_and_direct_download_when_no_ytdlp_formats()
    {
        // FR-13: 샤오홍슈에서 yt-dlp가 포맷을 못 얻으면 자체 추출기 → 직접 스트림 다운로드로 라우팅.
        var resolution = new XhsResolution(
            ExtractionRoute.XhsFallback,
            new MediaInfo { Title = "샤오홍슈 영상", Formats = [] },
            DirectStreamUrl: "https://cdn.xhscdn.com/stream/abc.mp4");
        var direct = new RecordingDirectDownloader();
        using var sut = CreateSut(
            xhsStrategy: new FakeXhsStrategy(resolution),
            directDownloader: direct);

        var item = sut.Enqueue("https://www.xiaohongshu.com/explore/6a5a012e").Added[0];
        await WaitForAsync(() => item.Status == DownloadStatus.Ready);
        Assert.Equal(ExtractionRoute.XhsFallback, item.ExtractionRoute);
        Assert.Equal("https://cdn.xhscdn.com/stream/abc.mp4", item.DirectStreamUrl);

        sut.Start(item.Id);
        await WaitForAsync(() => item.Status == DownloadStatus.Completed);
        Assert.Equal("https://cdn.xhscdn.com/stream/abc.mp4", direct.StreamUrl);
        Assert.NotNull(direct.OutputPath);
    }

    [Fact]
    public async Task should_use_ytdlp_engine_for_xhs_when_strategy_returns_ytdlp_route()
    {
        var resolution = new XhsResolution(
            ExtractionRoute.YtDlp,
            new MediaInfo { Title = "t", Formats = [new MediaFormat { FormatId = "0", Height = 720, VideoCodec = "avc1" }] },
            DirectStreamUrl: null);
        var engine = new FakeEngine(success: true);
        var direct = new RecordingDirectDownloader();
        using var sut = CreateSut(engine: engine, xhsStrategy: new FakeXhsStrategy(resolution), directDownloader: direct);

        var item = await EnqueueAndStartAsync(sut, "https://www.xiaohongshu.com/explore/abc");
        await WaitForAsync(() => item.Status == DownloadStatus.Completed);

        Assert.Equal(1, engine.CallCount);  // yt-dlp 엔진 경로 사용
        Assert.Null(direct.StreamUrl);      // 직접 다운로드는 호출되지 않음
    }

    [Fact]
    public async Task should_change_format_when_item_ready()
    {
        using var sut = CreateSut(metadata: new TwoFormatMetadata());

        var item = sut.Enqueue("https://youtu.be/abc").Added[0];
        await WaitForAsync(() => item.Status == DownloadStatus.Ready);
        Assert.Equal("137", item.SelectedFormatId); // 분석 기본값(Best=1080p)

        sut.ChangeFormat(item.Id, "18");

        Assert.Equal("18", item.SelectedFormatId);
    }

    [Fact]
    public async Task should_download_with_changed_format_when_started()
    {
        var engine = new FakeEngine(success: true);
        using var sut = CreateSut(metadata: new TwoFormatMetadata(), engine: engine);

        var item = sut.Enqueue("https://youtu.be/abc").Added[0];
        await WaitForAsync(() => item.Status == DownloadStatus.Ready);
        sut.ChangeFormat(item.Id, "18");
        sut.Start(item.Id);
        await WaitForAsync(() => item.Status == DownloadStatus.Completed);

        Assert.NotNull(engine.LastRequest);
        Assert.StartsWith("18", engine.LastRequest!.FormatId); // 변경한 포맷으로 다운로드
    }

    [Fact]
    public async Task should_ignore_format_change_when_downloading()
    {
        var engine = new BlockingEngine();
        using var sut = CreateSut(metadata: new TwoFormatMetadata(), engine: engine);

        var item = sut.Enqueue("https://youtu.be/abc").Added[0];
        await WaitForAsync(() => item.Status == DownloadStatus.Ready);
        sut.Start(item.Id);
        await WaitForAsync(() => item.Status == DownloadStatus.Downloading);

        sut.ChangeFormat(item.Id, "18");

        Assert.Equal("137", item.SelectedFormatId); // 진행 중에는 변경 불가
        engine.ReleaseAll();
    }

    [Fact]
    public async Task should_ignore_unknown_format_id_when_changing()
    {
        using var sut = CreateSut(metadata: new TwoFormatMetadata());

        var item = sut.Enqueue("https://youtu.be/abc").Added[0];
        await WaitForAsync(() => item.Status == DownloadStatus.Ready);

        sut.ChangeFormat(item.Id, "999");

        Assert.Equal("137", item.SelectedFormatId);
    }

    // ── Fakes ─────────────────────────────────────────────

    private static FakeSettings SettingsWithConcurrency(int max)
    {
        var s = new FakeSettings();
        s.Current.MaxConcurrent = max;
        return s;
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public bool SettingsFileExisted => true;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMetadata : IMediaMetadataService
    {
        public Task<MediaInfo> FetchAsync(string url, CancellationToken cancellationToken = default)
            => Task.FromResult(new MediaInfo
            {
                Title = "테스트",
                Formats = [new MediaFormat { FormatId = "137", Height = 1080, VideoCodec = "avc1", IsVideoOnly = true }],
            });
    }

    private sealed class ThrowingMetadata : IMediaMetadataService
    {
        public Task<MediaInfo> FetchAsync(string url, CancellationToken cancellationToken = default)
            => throw new MetadataFetchException("boom"); // Failure 없음 → 재시도 불가 → Unavailable
    }

    /// <summary>지정 횟수만큼 네트워크 실패 후 성공하는 페이크(FR-D2.1 재시도 검증).</summary>
    private sealed class FlakyNetworkMetadata(int failCount) : IMediaMetadataService
    {
        private int _calls;
        private volatile bool _failing = true;
        public int CallCount => Volatile.Read(ref _calls);
        public void StopFailing() => _failing = false;

        public Task<MediaInfo> FetchAsync(string url, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            if (_failing && call <= failCount)
                throw new MetadataFetchException(new ExtractionFailure(
                    ExtractionFailureKind.Network, "네트워크 오류 — 연결·프록시를 확인하세요", "ERROR: fake network"));
            return Task.FromResult(new MediaInfo
            {
                Title = "테스트",
                Formats = [new MediaFormat { FormatId = "137", Height = 1080, VideoCodec = "avc1", IsVideoOnly = true }],
            });
        }
    }

    /// <summary>봇 확인/로그인 실패 페이크(FR-L1 검증 — 실측 시그니처 kind).</summary>
    private sealed class LoginFailMetadata : IMediaMetadataService
    {
        public Task<MediaInfo> FetchAsync(string url, CancellationToken cancellationToken = default)
            => throw new MetadataFetchException(new ExtractionFailure(
                ExtractionFailureKind.LoginOrBotCheck, "봇 확인 차단", "ERROR: Sign in to confirm you’re not a bot"));
    }

    /// <summary>확정 실패(재시도 불가)를 던지는 페이크(NFR-D2 검증).</summary>
    private sealed class DefinitiveFailMetadata : IMediaMetadataService
    {
        private int _calls;
        public int CallCount => Volatile.Read(ref _calls);

        public Task<MediaInfo> FetchAsync(string url, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            throw new MetadataFetchException(new ExtractionFailure(
                ExtractionFailureKind.UnsupportedUrl, "지원하지 않는 링크", "ERROR: Unsupported URL: x"));
        }
    }

    private sealed class FakeXhsStrategy(XhsResolution resolution) : IXhsResolutionStrategy
    {
        public string? LastUrl { get; private set; }
        public Task<XhsResolution> ResolveAsync(string url, CancellationToken cancellationToken = default)
        {
            LastUrl = url;
            return Task.FromResult(resolution);
        }
    }

    private sealed class RecordingDirectDownloader : IDirectStreamDownloader
    {
        public string? StreamUrl { get; private set; }
        public string? OutputPath { get; private set; }
        public Task DownloadAsync(string streamUrl, string outputPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            StreamUrl = streamUrl;
            OutputPath = outputPath;
            progress?.Report(new DownloadProgress(100, null, null));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEngine(bool success) : IDownloadEngine
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);
        public DownloadRequest? LastRequest { get; private set; }

        public Task<DownloadResult> DownloadAsync(DownloadRequest request, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            LastRequest = request;
            progress?.Report(new DownloadProgress(100, null, null));
            return Task.FromResult(new DownloadResult(success, success ? @"D:\out.mp4" : null, success ? 0 : 1));
        }
    }

    /// <summary>1080p(137)·360p(18) 두 옵션을 주는 메타데이터 페이크(포맷 변경 테스트용).</summary>
    private sealed class TwoFormatMetadata : IMediaMetadataService
    {
        public Task<MediaInfo> FetchAsync(string url, CancellationToken cancellationToken = default)
            => Task.FromResult(new MediaInfo
            {
                Title = "테스트",
                Formats =
                [
                    new MediaFormat { FormatId = "137", Height = 1080, VideoCodec = "avc1", IsVideoOnly = true },
                    new MediaFormat { FormatId = "18", Height = 360, VideoCodec = "avc1" },
                ],
            });
    }

    private sealed class PathlessSuccessEngine : IDownloadEngine
    {
        public Task<DownloadResult> DownloadAsync(DownloadRequest request, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new DownloadResult(Success: true, OutputFilePath: null, ExitCode: 0));
    }

    private sealed class BlockingEngine : IDownloadEngine
    {
        private readonly SemaphoreSlim _release = new(0);
        private readonly object _lock = new();
        private int _current;
        private bool _completeNext;

        public int CurrentConcurrent { get { lock (_lock) return _current; } }
        public int MaxObservedConcurrent { get; private set; }

        public void ReleaseAll() => _release.Release(1000);
        public void SetSuccessOnNext() { lock (_lock) _completeNext = true; }

        public async Task<DownloadResult> DownloadAsync(DownloadRequest request, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            bool complete;
            lock (_lock) { _current++; MaxObservedConcurrent = Math.Max(MaxObservedConcurrent, _current); complete = _completeNext; }
            try
            {
                if (!complete)
                    await _release.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new DownloadResult(true, @"D:\out.mp4", 0);
            }
            finally
            {
                lock (_lock) { _current--; }
            }
        }
    }
}

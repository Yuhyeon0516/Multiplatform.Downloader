using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Engine;
using Multiplatform_Downloader.Core.Platforms;
using Multiplatform_Downloader.Core.Settings;

namespace Multiplatform_Downloader.Core.Queue;

/// <summary>
/// 다운로드 큐 오케스트레이터(FR-05). 각 항목을 분석(메타 조회) → 다운로드 파이프라인으로 처리하고,
/// 동시 실행을 <see cref="SemaphoreSlim"/>으로 제한한다. 항목별 <see cref="CancellationTokenSource"/>로
/// 취소·일시정지를 제어하며, 상태 변경 이벤트는 항상 lock 밖에서 발화한다(규칙 I-05).
/// URL 정규화는 선택적 resolver 델리게이트로 주입한다.
/// </summary>
public sealed class DownloadQueueService : IDownloadQueueService, IDisposable
{
    private readonly BatchUrlParser _parser;
    private readonly IMediaMetadataService _metadata;
    private readonly IDownloadEngine _engine;
    private readonly ISettingsService _settings;
    private readonly MediaFormatSelector _selector;
    private readonly Func<string, CancellationToken, Task<string>>? _resolveUrl;
    private readonly IAppLogger _logger;
    private readonly IXhsResolutionStrategy? _xhsStrategy;      // 샤오홍슈 폴백 분석(FR-13). null이면 일반 yt-dlp 경로만 사용
    private readonly IDirectStreamDownloader? _directDownloader; // 샤오홍슈·스레드 폴백 직접 다운로드
    private readonly IThreadsResolutionStrategy? _threadsStrategy; // 스레드 자체 추출(yt-dlp 미지원, FR-N1.8)
    private readonly TimeSpan _retryDelayBase;                   // 분석 재시도 백오프 기준(FR-D2.1) — 테스트에서 0 주입

    private readonly object _gate = new();
    private readonly List<DownloadItem> _items = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _itemCts = [];
    private readonly HashSet<Guid> _pauseRequested = [];
    private readonly HashSet<Guid> _pausedByAll = [];
    private readonly List<Task> _processing = [];
    private readonly SemaphoreSlim _limiter;        // 동시 다운로드 제한
    private readonly SemaphoreSlim _analysisLimiter; // 동시 메타데이터 분석 제한(다운로드와 독립)

    /// <summary>등록 → 분석(Analyze) → Ready 처리 방식. Ready에서 멈추고 사용자의 <see cref="Start"/>를 기다린다.</summary>
    private enum ProcessMode { Analyze, Download, Resume }

    public event EventHandler<DownloadItem>? ItemChanged;

    public DownloadQueueService(
        BatchUrlParser parser,
        IMediaMetadataService metadata,
        IDownloadEngine engine,
        ISettingsService settings,
        MediaFormatSelector selector,
        Func<string, CancellationToken, Task<string>>? resolveUrl = null,
        IAppLogger? logger = null,
        IXhsResolutionStrategy? xhsStrategy = null,
        IDirectStreamDownloader? directDownloader = null,
        IThreadsResolutionStrategy? threadsStrategy = null,
        TimeSpan? retryDelayBase = null)
    {
        _retryDelayBase = retryDelayBase ?? TimeSpan.FromSeconds(1);
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _resolveUrl = resolveUrl;
        _logger = logger ?? NullAppLogger.Instance;
        _xhsStrategy = xhsStrategy;
        _directDownloader = directDownloader;
        _threadsStrategy = threadsStrategy;

        var maxConcurrent = Math.Clamp(settings.Current.MaxConcurrent, AppSettings.MinConcurrent, AppSettings.MaxConcurrentLimit);
        _limiter = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        _analysisLimiter = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    public IReadOnlyList<DownloadItem> Items
    {
        get { lock (_gate) return _items.ToArray(); }
    }

    public EnqueueResult Enqueue(string urlsText)
    {
        var parsed = _parser.Parse(urlsText);
        var added = new List<DownloadItem>();
        var skipped = 0;

        lock (_gate)
        {
            var activeCount = _items.Count(i => !IsFinished(i.Status));
            var capacity = _settings.Current.MaxQueueItems - activeCount;

            foreach (var parsedUrl in parsed.Valid)
            {
                if (added.Count >= capacity)
                {
                    skipped++;
                    continue;
                }
                var item = new DownloadItem(parsedUrl.Url, parsedUrl.Platform);
                _items.Add(item);
                added.Add(item);
            }
        }

        foreach (var item in added)
        {
            // 등록 시에는 분석(메타데이터·썸네일)만 시작한다. 다운로드는 사용자가 Start를 호출할 때까지 시작하지 않는다.
            // CTS를 먼저 등록한 뒤 이벤트를 발화한다 — 동기 구독자가 즉시 Cancel해도 유실되지 않는다(H2)
            StartTask(item, ProcessMode.Analyze);
            RaiseChanged(item);
        }

        _logger.Info("Queue", $"등록 {added.Count}건 — 분석 시작 (거부 {parsed.Rejected.Count}건, 용량초과 {skipped}건)");
        return new EnqueueResult(added, parsed.Rejected, skipped);
    }

    /// <inheritdoc />
    public void RestoreCompleted(QueueItemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var item = new DownloadItem(snapshot.OriginalUrl, snapshot.Platform) { Id = snapshot.Id };
        item.ResolvedUrl = snapshot.ResolvedUrl;
        item.Title = snapshot.Title;
        item.ThumbnailPath = snapshot.ThumbnailPath;
        item.ExtractionRoute = snapshot.ExtractionRoute;

        // 다운로드 폴더가 진실의 원천 — 저장된 최종 경로의 파일 존재로 받음/안받음을 대조한다.
        // 경로를 모르는 완료(H3: 완료했지만 경로 미확보)는 대조 불가라 완료로 복원한다.
        var pathUnknown = string.IsNullOrWhiteSpace(snapshot.OutputFilePath);
        if (pathUnknown || File.Exists(snapshot.OutputFilePath))
        {
            item.SelectedFormatId = snapshot.SelectedFormatId;
            item.Complete(pathUnknown ? null : snapshot.OutputFilePath);
        }
        else
        {
            // 포맷은 복원하지 않는다 — [재시도]가 재분석부터 타게 해 폴백 경로의 만료 스트림 URL을 회피
            item.MarkUnavailable("받은 파일이 폴더에 없습니다 (삭제/이동됨) — [재시도]로 다시 받을 수 있습니다");
        }

        lock (_gate) { _items.Add(item); }
        _logger.Info("Queue", $"[{Tag(item)}] 완료 항목 복원 — " +
            (item.Status == DownloadStatus.Completed ? "파일 확인됨" : "파일 없음(삭제/이동)"));
        RaiseChanged(item);
    }

    public void Start(Guid id)
    {
        DownloadItem? item;
        lock (_gate)
        {
            item = _items.FirstOrDefault(i => i.Id == id);
            if (item is null || item.Status != DownloadStatus.Ready)
                return; // Ready(분석 완료) 상태만 다운로드를 시작할 수 있다
        }
        _logger.Info("Queue", $"[{Tag(item)}] 사용자 다운로드 시작");
        StartTask(item, ProcessMode.Download);
    }

    public void StartAll()
    {
        List<Guid> ready;
        lock (_gate)
        {
            ready = _items.Where(i => i.Status == DownloadStatus.Ready).Select(i => i.Id).ToList();
        }
        foreach (var id in ready)
            Start(id);
    }

    public void ChangeFormat(Guid id, string formatId)
    {
        if (string.IsNullOrWhiteSpace(formatId))
            return;
        DownloadItem? item;
        lock (_gate)
        {
            item = _items.FirstOrDefault(i => i.Id == id);
            // 진행/종료 중 변경 금지 — 다운로드 전(Ready) 또는 재시도 대상(Failed/Canceled)만 허용
            if (item is null || item.Status is not (DownloadStatus.Ready or DownloadStatus.Failed or DownloadStatus.Canceled))
                return;
            if (!item.Formats.Any(f => f.FormatId == formatId))
                return;
            if (item.SelectedFormatId == formatId)
                return;
            item.SelectedFormatId = formatId;
        }
        _logger.Info("Queue", $"[{Tag(item)}] 포맷 변경: {formatId}");
        RaiseChanged(item);
    }

    public void Cancel(Guid id) => CancelInternal(id, asPause: false);

    public void Pause(Guid id) => CancelInternal(id, asPause: true);

    public void Resume(Guid id)
    {
        DownloadItem? item;
        lock (_gate)
        {
            item = _items.FirstOrDefault(i => i.Id == id);
            if (item is null || item.Status != DownloadStatus.Paused)
                return;
            _pausedByAll.Remove(id);
        }
        StartTask(item, ProcessMode.Resume);
    }

    public void Remove(Guid id)
    {
        DownloadItem? item;
        lock (_gate)
        {
            item = _items.FirstOrDefault(i => i.Id == id);
            if (item is null)
                return;
            if (_itemCts.TryGetValue(id, out var cts))
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }
            _items.Remove(item);
            _pauseRequested.Remove(id);   // 잔여 요청 플래그 정리 (M7)
            _pausedByAll.Remove(id);
        }
        if (item is not null)
            RaiseChanged(item);
    }

    public void Retry(Guid id)
    {
        DownloadItem? item;
        lock (_gate)
        {
            item = _items.FirstOrDefault(i => i.Id == id);
            if (item is null || item.Status is not (DownloadStatus.Failed or DownloadStatus.Canceled or DownloadStatus.Unavailable))
                return;
        }
        SafeTransition(item, item.PrepareRetry);
        // 분석까지 마쳤던 항목(포맷 보유)은 다운로드부터 재시도, 분석 단계에서 실패했으면 다시 분석한다.
        var alreadyAnalyzed = item.SelectedFormatId is not null || item.Formats.Count > 0;
        StartTask(item, alreadyAnalyzed ? ProcessMode.Download : ProcessMode.Analyze);
    }

    public void PauseAll()
    {
        List<DownloadItem> toPause;
        lock (_gate)
        {
            toPause = _items.Where(i => i.Status == DownloadStatus.Downloading).ToList();
            foreach (var item in toPause)
                _pausedByAll.Add(item.Id);
        }
        foreach (var item in toPause)
            Pause(item.Id);
    }

    public void ResumeAll()
    {
        // PauseAll이 멈춘 항목만 재개 (수동 일시정지 항목은 제외) — PRD P1-07
        List<Guid> toResume;
        lock (_gate)
        {
            toResume = _items
                .Where(i => i.Status == DownloadStatus.Paused && _pausedByAll.Contains(i.Id))
                .Select(i => i.Id)
                .ToList();
        }
        foreach (var id in toResume)
            Resume(id);
    }

    private void CancelInternal(Guid id, bool asPause)
    {
        DownloadItem? cancelledDirectly = null;
        lock (_gate)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item is null)
                return;
            if (asPause)
                _pauseRequested.Add(id);
            if (_itemCts.TryGetValue(id, out var cts))
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }
            else if (!asPause && !IsFinished(item.Status))
            {
                // 처리 Task가 아직 없으면(대기 중) 즉시 취소. 이벤트는 lock 밖에서 발화(H2/L3)
                try { item.Cancel(); cancelledDirectly = item; }
                catch (InvalidOperationException) { }
            }
        }
        if (cancelledDirectly is not null)
            RaiseChanged(cancelledDirectly);
    }

    private void StartTask(DownloadItem item, ProcessMode mode)
    {
        var cts = new CancellationTokenSource();
        var task = Task.Run(() => ProcessItemAsync(item, mode, cts.Token));
        lock (_gate)
        {
            _itemCts[item.Id] = cts;
            _pauseRequested.Remove(item.Id);
            _processing.RemoveAll(t => t.IsCompleted);   // 완료 태스크 정리 (M8)
            _processing.Add(task);
        }
    }

    private async Task ProcessItemAsync(DownloadItem item, ProcessMode mode, CancellationToken cancellationToken)
    {
        if (mode == ProcessMode.Analyze)
        {
            await AnalyzeStageAsync(item, cancellationToken).ConfigureAwait(false);
            return;
        }
        await DownloadStageAsync(item, resume: mode == ProcessMode.Resume, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 분석 단계: 메타데이터·썸네일을 조회하고 <c>Ready</c>에서 멈춘다. 다운로드는 시작하지 않는다.
    /// 네트워크 계열 실패는 설정 횟수만큼 자동 재시도(FR-D2.1, 지수 백오프),
    /// 재시도 소진·확정 실패는 <c>Unavailable</c>(다운로드 불가)로 확정한다(FR-D2.4).
    /// </summary>
    private async Task AnalyzeStageAsync(DownloadItem item, CancellationToken cancellationToken)
    {
        await _analysisLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        var shouldAutoStart = false; // 자동 다운로드는 이 태스크의 CTS 정리(finally) 이후에 시작한다
        try
        {
            var maxRetry = Math.Clamp(_settings.Current.AnalysisRetryCount, AppSettings.MinRetry, AppSettings.MaxRetry);

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (!await AnalyzeAsync(item, cancellationToken).ConfigureAwait(false))
                        return; // 상태 경합(취소 등) → 중단
                    // 자동 다운로드 옵션(FR-N3): 플래그만 세우고 실제 시작은 finally 이후(CTS 정리 후).
                    // break 로 루프를 벗어나야 finally 뒤 자동 시작 코드가 실행된다(return이면 건너뜀).
                    shouldAutoStart = _settings.Current.AutoStartDownload;
                    _logger.Info("Queue", shouldAutoStart
                        ? $"[{Tag(item)}] 분석 완료 — 자동 다운로드 예약(옵션 켜짐)"
                        : $"[{Tag(item)}] 분석 완료 — 다운로드 대기(Ready). 사용자 시작 대기 중");
                    break;
                }
                catch (MetadataFetchException ex)
                {
                    var retryable = ex.Failure?.IsRetryable ?? false; // 네트워크 계열만(NFR-D2)
                    if (retryable && attempt < maxRetry)
                    {
                        _logger.Warning("Queue", $"[{Tag(item)}] 분석 실패(네트워크) — 재시도 {attempt + 1}/{maxRetry}: {ex.Message}");
                        if (!SafeTransition(item, item.ReturnToQueued))
                            return; // 재시도 준비 중 상태 경합
                        var delay = TimeSpan.FromTicks(_retryDelayBase.Ticks << attempt); // 1s→2s→4s
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    // 확정 실패 또는 재시도 소진 → 다운로드 불가(FR-D2.4)
                    var reason = ex.Failure?.UserMessage ?? ex.Message;
                    var category = MapFailureCategory(ex.Failure?.Kind);
                    SafeTransition(item, () => item.MarkUnavailable(reason, category));
                    _logger.Warning("Queue", $"[{Tag(item)}] 다운로드 불가 확정: {reason}"
                        + (string.IsNullOrEmpty(ex.Failure?.RawErrorLine) ? "" : $" | {ex.Failure.RawErrorLine}"));
                    return;
                }
                catch (XhsExtractionException ex)
                {
                    SafeTransition(item, () => item.MarkUnavailable($"링크 만료 또는 열람 불가 — {ex.Message}"));
                    _logger.Warning("Queue", $"[{Tag(item)}] 샤오홍슈 추출 실패 → 다운로드 불가: {ex.Message}");
                    return;
                }
                catch (ThreadsExtractionException ex)
                {
                    SafeTransition(item, () => item.MarkUnavailable(ex.Message));
                    _logger.Warning("Queue", $"[{Tag(item)}] 스레드 추출 실패 → 다운로드 불가: {ex.Message}");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            HandleCancellation(item);
            _logger.Info("Queue", $"[{Tag(item)}] 분석 취소됨 (상태 {item.Status})");
        }
        catch (Exception ex)
        {
            SafeTransition(item, () => item.Fail(ex.Message, ErrorCategory.Unknown));
            _logger.Error("Queue", $"[{Tag(item)}] 분석 오류: {ex.GetType().Name} {ex.Message}");
        }
        finally
        {
            _analysisLimiter.Release();
            CleanupItemTask(item); // 분석 CTS 제거 — 이후 Start()가 새 CTS를 안전히 등록
        }

        // CTS 정리 후 자동 시작 — 분석 CTS 정리와의 경합 없음(FR-N3.2)
        if (shouldAutoStart)
            Start(item.Id);
    }

    /// <summary>다운로드 단계: <c>Ready</c>/<c>Paused</c>에서 시작하여 엔진으로 다운로드한다.</summary>
    private async Task DownloadStageAsync(DownloadItem item, bool resume, CancellationToken cancellationToken)
    {
        await _limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!SafeTransition(item, item.Start))
            {
                _logger.Info("Queue", $"[{Tag(item)}] 시작 안 함(취소/종료됨)");
                return; // 이미 취소/종료된 항목은 다운로드하지 않는다 (H1/H2)
            }
            _logger.Info("Queue", $"[{Tag(item)}] 다운로드 시작 (format={item.SelectedFormatId ?? "best"})");

            var progress = new DelegateProgress(p =>
            {
                item.UpdateProgress(p);
                RaiseChanged(item);
            });

            DownloadResult result;
            if (item.ExtractionRoute == ExtractionRoute.XhsFallback && item.DirectStreamUrl is not null && _directDownloader is not null)
            {
                // 샤오홍슈 폴백: yt-dlp 대신 파싱한 스트림 URL을 직접 파일로 받는다(FR-13).
                var outputPath = BuildDirectOutputPath(item);
                _logger.Info("Queue", $"[{Tag(item)}] 폴백 직접 다운로드 → {outputPath}");
                await _directDownloader.DownloadAsync(item.DirectStreamUrl, outputPath, progress, cancellationToken).ConfigureAwait(false);
                result = new DownloadResult(Success: true, OutputFilePath: outputPath, ExitCode: 0);
            }
            else
            {
                var cookies = _settings.Current.ResolveCookies();
                var request = new DownloadRequest(
                    item.ResolvedUrl ?? item.OriginalUrl,
                    BuildFormatSelector(item),
                    BuildOutputTemplate(),
                    Continue: resume,
                    ProxyUrl: null,
                    CookieFile: cookies.CookieFile,
                    CookieFromBrowser: cookies.FromBrowser);

                result = await _engine.DownloadAsync(request, progress, cancellationToken).ConfigureAwait(false);
            }

            if (result.Success)
            {
                SafeTransition(item, () => item.Complete(result.OutputFilePath)); // 경로 null이어도 성공은 성공(H3)
                _logger.Info("Queue", $"[{Tag(item)}] 완료: {item.OutputFilePath ?? "(경로 미확인)"}");
            }
            else
            {
                var reason = string.IsNullOrWhiteSpace(result.ErrorLine)
                    ? $"다운로드 실패 (엔진 종료 코드 {result.ExitCode})"
                    : $"다운로드 실패 — {result.ErrorLine}";
                SafeTransition(item, () => item.Fail(reason, ErrorCategory.EngineFailure));
                _logger.Warning("Queue", $"[{Tag(item)}] 실패(코드 {result.ExitCode}): {result.ErrorLine ?? "(stderr 없음)"}");
            }
        }
        catch (OperationCanceledException)
        {
            HandleCancellation(item);
            _logger.Info("Queue", $"[{Tag(item)}] 취소/일시정지 처리됨 (상태 {item.Status})");
        }
        catch (XhsExtractionException ex)
        {
            SafeTransition(item, () => item.Fail(ex.Message, ErrorCategory.UnsupportedContent));
            _logger.Warning("Queue", $"[{Tag(item)}] 샤오홍슈 추출 실패: {ex.Message}");
        }
        catch (Exception ex)
        {
            SafeTransition(item, () => item.Fail(ex.Message, ErrorCategory.Unknown));
            _logger.Error("Queue", $"[{Tag(item)}] 오류: {ex.GetType().Name} {ex.Message}");
        }
        finally
        {
            _limiter.Release();
            CleanupItemTask(item);
            CleanupPartialDownloads(item); // 실패·취소 시 남은 조각 파일 제거(일시정지·완료는 보존)
        }
    }

    /// <summary>
    /// 실패·취소·불가로 끝난 항목의 yt-dlp 조각 파일(.part/.ytdl/.part-Frag)을 삭제한다.
    /// 일시정지(재개용 .part 필요)·완료는 건드리지 않는다. id를 아는 경우에만(안전) 정리한다.
    /// </summary>
    private void CleanupPartialDownloads(DownloadItem item)
    {
        if (item.Status is not (DownloadStatus.Failed or DownloadStatus.Canceled or DownloadStatus.Unavailable))
            return;
        var id = item.SourceId;
        if (string.IsNullOrEmpty(id))
            return; // id 미상 — 오삭제 방지 위해 정리 생략

        try
        {
            var folder = _settings.Current.DownloadFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                if (!PartialDownloadCleaner.IsArtifactOf(Path.GetFileName(file), id))
                    continue;
                try
                {
                    File.Delete(file);
                    _logger.Info("Queue", $"[{Tag(item)}] 조각 파일 정리: {Path.GetFileName(file)}");
                }
                catch { /* 잠금·권한 — 무시 */ }
            }
        }
        catch { /* best-effort 정리 */ }
    }

    /// <summary>기동 시 다운로드 폴더의 고아 조각 파일(이전 크래시·실패 잔여)을 일괄 정리한다(FR: 아티팩트 제거).</summary>
    public void SweepOrphanPartials()
    {
        try
        {
            var folder = _settings.Current.DownloadFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return;
            var removed = 0;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                if (!PartialDownloadCleaner.IsYtDlpArtifact(Path.GetFileName(file)))
                    continue;
                try { File.Delete(file); removed++; } catch { /* 진행 중일 수 있음 — 무시 */ }
            }
            if (removed > 0)
                _logger.Info("Queue", $"기동 정리: 고아 조각 파일 {removed}건 삭제");
        }
        catch { /* best-effort */ }
    }

    /// <summary>항목의 CTS를 정리한다. 재개 대기(Paused)가 아니면 일시정지 요청 플래그도 정리(M7).</summary>
    private void CleanupItemTask(DownloadItem item)
    {
        lock (_gate)
        {
            if (_itemCts.TryGetValue(item.Id, out var cts))
            {
                cts.Dispose();
                _itemCts.Remove(item.Id);
            }
            if (item.Status != DownloadStatus.Paused)
                _pauseRequested.Remove(item.Id);
        }
    }

    /// <returns>분석·상태 전이가 모두 성공하면 true. 상태 경합으로 실패하면 false(호출부 중단).</returns>
    private async Task<bool> AnalyzeAsync(DownloadItem item, CancellationToken cancellationToken)
    {
        if (!SafeTransition(item, item.MarkAnalyzing))
            return false;

        var url = item.OriginalUrl;
        if (_resolveUrl is not null)
            url = await _resolveUrl(url, cancellationToken).ConfigureAwait(false);
        url = UrlNormalizer.Normalize(url); // rednote→xiaohongshu, FB /videos/→watch (FR-01, FR-N1.3)
        item.ResolvedUrl = url;

        _logger.Info("Queue", $"[{Tag(item)}] 분석 시작: {item.OriginalUrl}");

        Models.MediaInfo info;
        if (item.Platform == PlatformType.Xiaohongshu && _xhsStrategy is not null)
        {
            // 샤오홍슈: yt-dlp → 자체 추출기 폴백(FR-13). 폴백 시 직접 스트림 URL을 보관한다.
            var resolution = await _xhsStrategy.ResolveAsync(url, cancellationToken).ConfigureAwait(false);
            info = resolution.Info;
            item.ExtractionRoute = resolution.Route;
            item.DirectStreamUrl = resolution.DirectStreamUrl;
            _logger.Info("Queue", $"[{Tag(item)}] 샤오홍슈 추출 경로: {resolution.Route}");
        }
        else if (item.Platform == PlatformType.Threads && _threadsStrategy is not null)
        {
            // 스레드: yt-dlp 익스트랙터 부재 → 곧바로 자체 추출(FR-N1.8). 직접 스트림 URL 보관.
            var resolution = await _threadsStrategy.ResolveAsync(url, cancellationToken).ConfigureAwait(false);
            info = resolution.Info;
            item.ExtractionRoute = resolution.Route;
            item.DirectStreamUrl = resolution.DirectStreamUrl;
            _logger.Info("Queue", $"[{Tag(item)}] 스레드 자체 추출 완료 (직접 스트림)");
        }
        else
        {
            info = await _metadata.FetchAsync(url, cancellationToken).ConfigureAwait(false);
        }

        item.Title = info.Title;
        item.SourceId = info.Id; // 조각 파일 정리·파일명 매칭용(FR: 실패 아티팩트 제거)
        item.ThumbnailPath = info.ThumbnailUrl;
        item.ThumbnailCandidates = info.ThumbnailUrls;
        item.Duration = info.Duration;
        item.Formats = info.Formats;

        var options = _selector.BuildOptions(info.Formats);
        var selected = _selector.SelectByPreference(options, _settings.Current.DefaultQuality);
        item.SelectedFormatId = selected?.FormatId;

        _logger.Info("Queue", $"[{Tag(item)}] 분석 완료: '{info.Title}' 포맷 {info.Formats.Count}개, 선택 {item.SelectedFormatId ?? "없음"}");
        return SafeTransition(item, item.MarkReady);
    }

    private void HandleCancellation(DownloadItem item)
    {
        bool isPause;
        lock (_gate) { isPause = _pauseRequested.Contains(item.Id); }

        try
        {
            if (isPause && item.Status == DownloadStatus.Downloading)
                SafeTransition(item, item.Pause);
            else if (item.Status is not (DownloadStatus.Canceled or DownloadStatus.Completed or DownloadStatus.Failed))
                SafeTransition(item, item.Cancel);
        }
        catch (InvalidOperationException)
        {
            // 상태 경합 — 무시
        }
    }

    private string BuildOutputTemplate()
        // %(title).120s: 제목을 120자로 제한한다. 페이스북/틱톡 등은 캡션 전체가 제목이라(200자+)
        // 제한이 없으면 Windows 경로 한계 초과로 'unable to open for writing: [Errno 22]' 실패(실측 2026-08-02).
        => Path.Combine(_settings.Current.DownloadFolder, "%(title).120s [%(id)s].%(ext)s");

    /// <summary>폴백 직접 다운로드용 구체 파일 경로를 만든다(yt-dlp 템플릿을 쓸 수 없으므로 직접 조립).</summary>
    private string BuildDirectOutputPath(DownloadItem item)
    {
        var title = string.IsNullOrWhiteSpace(item.Title) ? item.Id.ToString("N") : item.Title!;
        var safe = SanitizeFileName(title);
        var ext = GuessExtension(item.DirectStreamUrl);
        return Path.Combine(_settings.Current.DownloadFolder, $"{safe} [{Tag(item)}]{ext}");
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim();
        return name.Length > 120 ? name[..120] : name;
    }

    private static string GuessExtension(string? streamUrl)
    {
        if (Uri.TryCreate(streamUrl, UriKind.Absolute, out var uri))
        {
            var ext = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrEmpty(ext) && ext.Length <= 5)
                return ext;
        }
        return ".mp4";
    }

    /// <summary>선택 포맷이 영상만(video-only)이면 최적 오디오를 병합하도록 yt-dlp 포맷 문자열을 만든다.</summary>
    private static string BuildFormatSelector(DownloadItem item)
    {
        // 폴백 체인 bv*+ba/b: 선택 실패(예: Reddit v.redd.it 분리 스트림 'Requested format is not
        // available')를 영상+오디오 병합 → 단독 muxed 순으로 구제한다(FR-N1.6).
        const string fallback = "bv*+ba/b";
        var id = item.SelectedFormatId;
        if (string.IsNullOrEmpty(id))
            return fallback;
        var format = item.Formats.FirstOrDefault(f => f.FormatId == id);
        return format?.IsVideoOnly == true ? $"{id}+bestaudio/{fallback}" : $"{id}/{fallback}";
    }

    private static bool IsFinished(DownloadStatus status)
        => status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Canceled or DownloadStatus.Unavailable;

    /// <summary>전이를 시도한다. 성공하면 이벤트를 발화하고 true, 상태 경합으로 실패하면 false를 반환한다(H1).</summary>
    private bool SafeTransition(DownloadItem item, Action transition)
    {
        try { transition(); }
        catch (InvalidOperationException) { return false; }
        RaiseChanged(item);
        return true;
    }

    private void RaiseChanged(DownloadItem item) => ItemChanged?.Invoke(this, item);

    private static string Tag(DownloadItem item) => item.Id.ToString("N")[..8];

    /// <summary>로그인/봇 확인으로 해결 가능한 실패만 LoginRequired로 매핑(FR-L1) — 카드 [로그인] 버튼 노출 기준.</summary>
    private static ErrorCategory? MapFailureCategory(ExtractionFailureKind? kind) => kind switch
    {
        ExtractionFailureKind.LoginOrBotCheck => ErrorCategory.LoginRequired,
        ExtractionFailureKind.InstagramLoginOrGone => ErrorCategory.LoginRequired,
        _ => null,
    };

    /// <summary>
    /// rednote.com을 xiaohongshu.com으로 정규화한다(FR-01). yt-dlp의 rednote.com은 Generic extractor로만
    /// 처리돼 포맷이 제한되지만, xiaohongshu.com은 전용 extractor로 완전한 포맷을 반환한다.
    /// </summary>
    public void Dispose()
    {
        Task[] pending;
        lock (_gate)
        {
            foreach (var cts in _itemCts.Values)
            {
                try { cts.Cancel(); } catch { /* ignore */ }
            }
            pending = _processing.ToArray();
        }

        // 진행 중 태스크가 finally에서 _limiter.Release()·CTS 정리를 마치도록 대기(H4)
        try { Task.WaitAll(pending, TimeSpan.FromSeconds(5)); } catch { /* 취소·타임아웃 무시 */ }

        lock (_gate)
        {
            foreach (var cts in _itemCts.Values)
            {
                try { cts.Dispose(); } catch { /* ignore */ }
            }
            _itemCts.Clear();
            _processing.Clear();
        }
        _limiter.Dispose();
        _analysisLimiter.Dispose();
    }

    /// <summary>동기적으로 Report하는 IProgress 구현(테스트 결정성·즉시 반영).</summary>
    private sealed class DelegateProgress(Action<Models.DownloadProgress> onReport) : IProgress<Models.DownloadProgress>
    {
        public void Report(Models.DownloadProgress value) => onReport(value);
    }
}

using Autofac;
using Caliburn.Micro;
using Multiplatform_Downloader.Bases;
using Multiplatform_Downloader.Core.Abstractions;
using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Engine;
using Multiplatform_Downloader.Core.Ipc;
using Multiplatform_Downloader.Core.Net;
using Multiplatform_Downloader.Core.Platforms;
using Multiplatform_Downloader.Core.Queue;
using Multiplatform_Downloader.Core.Settings;
using Multiplatform_Downloader.Services;
using Multiplatform_Downloader.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Multiplatform_Downloader;
/****************************************************************************
       Purpose      : This class is an implementation class that oversees 
                      the process of registering and starting Caliburn.Micro's 
                      instances. Register here for various instances to be used
                      for actual projects.
       Created On   : 2026-07-30 오전 10:05:59
    ****************************************************************************/
internal class Bootstrapper : ParentBootstrapper<ShellViewModel>
{
    #region - Ctors -
    public Bootstrapper()
    {
        Initialize();
    }

    #endregion
    #region - Implementation of Interface -
    #endregion
    #region - Overrides -
    protected override async Task Start()
    {
        try
        {
            var container = Container ?? throw new InvalidOperationException("컨테이너가 준비되지 않았습니다.");
            var settings = container.Resolve<ISettingsService>();
            await settings.LoadAsync(CancellationTokenSourceHandler.Token);

            var logger = container.Resolve<IAppLogger>();
            logger.Info("App", $"앱 시작. 다운로드 폴더: {settings.Current.DownloadFolder}");

            // 엔진 바이너리 헬스체크 — 없으면 로그로 안내(앱은 계속 실행)
            var toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
            var health = new EngineHealthCheck(toolsDir).Check();
            if (health.AllPresent)
                logger.Info("Engine", "엔진 바이너리 모두 존재");
            else
                logger.Warning("Engine", $"누락된 엔진 바이너리: {string.Join(", ", health.Missing)} — tools 폴더에 배치하세요 ({toolsDir})");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Bootstrapper] Start failed: {ex}");
        }
    }

    protected override Task Stop()
    {

        try
        {
            CancellationTokenSourceHandler?.Cancel();
            CancellationTokenSourceHandler?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Bootstrapper] Stop failed: {ex}");
        }
        return Task.CompletedTask;
    }

    private bool _exiting;
    private SingleInstanceGuard? _instanceGuard;
    private PipeIpcServer? _ipcServer;
    private QueuePersistence? _queuePersistence;
    private System.Threading.Timer? _queueSaveTimer;

    private const string SingleInstanceMutexName = "MultiplatformDownloader.SingleInstance";
    private const string IpcPipeName = "MultiplatformDownloader.Ipc";

    protected override async void OnStartup(object sender, StartupEventArgs e)
    {
        // 단일 인스턴스 보장(FR-08): 두 번째 인스턴스는 mpdl:// 인자만 기존 인스턴스에 파이프로 넘기고 즉시 종료
        _instanceGuard = new SingleInstanceGuard(SingleInstanceMutexName);
        if (!_instanceGuard.IsPrimaryInstance)
        {
            var protocolArg = Array.Find(e.Args, a => a.StartsWith("mpdl://", StringComparison.OrdinalIgnoreCase));
            if (protocolArg is not null)
                await PipeIpcClient.TrySendAsync(IpcPipeName, protocolArg, TimeSpan.FromSeconds(2));
            _exiting = true;
            Application.Current.Shutdown();
            return;
        }

        base.OnStartup(sender, e);

        var container = Container;
        var minimized = Array.Exists(e.Args, a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

        // 스플래시 — 백그라운드(--minimized) 기동 시에는 표시하지 않는다
        SplashScreenViewModel? splash = null;
        if (container is not null && !minimized)
        {
            splash = container.Resolve<SplashScreenViewModel>();
            splash.Version = $"v{typeof(Bootstrapper).Assembly.GetName().Version?.ToString(3) ?? "?"}";
            await container.Resolve<IWindowManager>().ShowWindowAsync(splash);
        }

        // 단계 갱신 헬퍼 — 순식간에 지나가지 않도록 최소 표시 시간을 준다
        async Task StepAsync(string message, int progress)
        {
            if (splash is null)
                return;
            splash.UpdateStep(message, progress);
            await Task.Delay(110);
        }

        if (container is not null)
        {
            var logger = container.Resolve<IAppLogger>();
            var settings = container.Resolve<ISettingsService>();

            await StepAsync("설정을 불러오는 중...", 12);
            await settings.LoadAsync(CancellationTokenSourceHandler.Token);
            TryRun(() => ThemeService.Apply(settings.Current.Theme)); // 저장된 테마(라이트/다크/시스템) 적용

            await StepAsync("다운로드 엔진을 확인하는 중...", 28);

            await StepAsync("mpdl:// 프로토콜을 등록하는 중...", 42);
            TryRun(() => { container.Resolve<ProtocolRegistrar>().Register(); logger.Info("App", "mpdl:// 프로토콜 등록됨"); });

            await StepAsync("Windows 시작 프로그램을 설정하는 중...", 54);
            // 첫 실행(설정 파일 없음): 인스톨러가 설정한 시작 등록 상태를 그대로 채택한다.
            // (인스톨러 체크박스가 Run 키를 만들어도, 앱이 설정 기본값으로 덮어써 지워지던 문제 수정 — FR-09)
            if (!settings.SettingsFileExisted)
            {
                TryRun(() => { settings.Current.LaunchOnStartup = container.Resolve<StartupRegistrar>().IsEnabled(); });
                await settings.SaveAsync(CancellationTokenSourceHandler.Token);
                logger.Info("App", $"첫 실행 — 인스톨러 시작 등록 상태 채택: {settings.Current.LaunchOnStartup}");
            }
            TryRun(() => { container.Resolve<StartupRegistrar>().SetEnabled(settings.Current.LaunchOnStartup, settings.Current.StartMinimized); logger.Info("App", $"Windows 시작 등록: {settings.Current.LaunchOnStartup}"); });

            await StepAsync("트레이 아이콘을 초기화하는 중...", 66);
            TryRun(() => InitializeTray(container, logger));

            await StepAsync("알림·IPC 서비스를 시작하는 중...", 78);
            TryRun(() => ConnectToast(container));
            TryRun(() => StartIpcServer(container, logger));
            TryRun(() => WireQueuePersistence(container, logger));
            TryRun(() => container.Resolve<IDownloadQueueService>().SweepOrphanPartials()); // 이전 실패 조각 정리

            await StepAsync("메인 화면을 준비하는 중...", 90);
        }

        await DisplayRootViewForAsync<ShellViewModel>();

        // 스플래시가 먼저 떠서 Application.MainWindow가 스플래시로 잡혀 있다 — 셸 창으로 교정
        var shellWindow = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w is Views.ShellView);
        if (shellWindow is not null && Application.Current is not null)
            Application.Current.MainWindow = shellWindow;

        if (container is not null)
        {
            HandleProtocolArgs(e.Args, container);
            WireCloseToTray(container);

            // 이전 세션 미완료 항목 복원(FR-D3.5) — 재분석으로 신선한 썸네일·포맷 확보(FR-D1.5)
            await TryRestoreQueueAsync(container);

            // --minimized 로 시작되면 트레이로 최소화
            if (minimized)
                Application.Current?.MainWindow?.Hide();
        }

        if (splash is not null)
        {
            splash.UpdateStep("준비가 완료되었습니다.", 100);
            await Task.Delay(300);
            await splash.TryCloseAsync();
        }
    }

    /// <summary>큐 영속화 배선(FR-D3.5): ItemChanged 1초 디바운스 저장 + 종료 시 최종 저장.</summary>
    private void WireQueuePersistence(IContainer container, IAppLogger logger)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Multiplatform-Downloader", "queue-state.json");
        _queuePersistence = new QueuePersistence(path);

        var queue = container.Resolve<IDownloadQueueService>();
        _queueSaveTimer = new System.Threading.Timer(_ =>
        {
            try { _queuePersistence.SaveAsync(queue.Items).GetAwaiter().GetResult(); }
            catch (Exception ex) { Debug.WriteLine($"[QueuePersistence] save failed: {ex.Message}"); }
        }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

        queue.ItemChanged += (_, _) => _queueSaveTimer?.Change(1000, System.Threading.Timeout.Infinite);
        logger.Info("Queue", "큐 영속화 배선됨 (queue-state.json)");
    }

    /// <summary>이전 세션 항목 복원(FR-D3.5). 완료 항목은 재분석 없이 파일 존재를 대조해 복원하고
    /// (받음/안받음 구분 — 사용자 요청), 미완료 항목은 원본 URL 재등록으로 분석부터 다시 수행한다.</summary>
    private async Task TryRestoreQueueAsync(IContainer container)
    {
        try
        {
            if (_queuePersistence is null)
                return;
            var snapshots = await _queuePersistence.LoadAsync();
            if (snapshots.Count == 0)
                return;

            var queue = container.Resolve<IDownloadQueueService>();

            var completed = snapshots.Where(s => s.Status == DownloadStatus.Completed).ToList();
            foreach (var snapshot in completed)
                queue.RestoreCompleted(snapshot);

            var pendingUrls = snapshots
                .Where(s => s.Status != DownloadStatus.Completed)
                .Select(s => s.OriginalUrl)
                .Distinct()
                .ToList();
            if (pendingUrls.Count > 0)
                queue.Enqueue(string.Join('\n', pendingUrls));

            container.Resolve<IAppLogger>().Info("Queue",
                $"이전 세션 복원 — 완료 {completed.Count}건(파일 대조), 미완료 {pendingUrls.Count}건(재분석)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[QueuePersistence] restore failed: {ex.Message}");
        }
    }

    /// <summary>후속 인스턴스가 파이프로 넘긴 mpdl:// URL을 받아 큐에 추가한다(FR-08).</summary>
    private void StartIpcServer(IContainer container, IAppLogger logger)
    {
        _ipcServer = new PipeIpcServer(IpcPipeName);
        _ipcServer.MessageReceived += (_, message) =>
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                HandleProtocolArgs([message], container);
                ShowMainWindow(); // 크롬에서 전송하면 앱 창을 앞으로 표시(사용자 요청)
            });
        _ipcServer.Start();
        logger.Info("Ipc", "단일 인스턴스 파이프 서버 시작");
    }

    protected override void OnExit(object sender, EventArgs e)
    {
        // 종료 전 최종 큐 저장(FR-D3.5)
        TryRun(() =>
        {
            _queueSaveTimer?.Dispose();
            var queue = Container?.Resolve<IDownloadQueueService>();
            if (queue is not null && _queuePersistence is not null)
                _queuePersistence.SaveAsync(queue.Items).Wait(TimeSpan.FromSeconds(2));
        });
        TryRun(() => _ipcServer?.Dispose());
        TryRun(() => Container?.Resolve<TrayIconService>().Dispose());
        TryRun(() => _instanceGuard?.Dispose());
        base.OnExit(sender, e);
    }

    // ── Windows 통합 배선 헬퍼 ──

    private static void TryRun(System.Action action)
    {
        try { action(); }
        catch (Exception ex) { Debug.WriteLine($"[Bootstrapper] {ex}"); }
    }

    private void InitializeTray(IContainer container, IAppLogger logger)
    {
        var tray = container.Resolve<TrayIconService>();
        tray.Initialize();
        tray.OpenRequested += (_, _) => ShowMainWindow();
        tray.SettingsRequested += (_, _) =>
        {
            // 메인 창을 먼저 복원한 뒤 설정 다이얼로그를 연다 (창만 뜨던 버그 수정)
            ShowMainWindow();
            Application.Current?.Dispatcher.BeginInvoke(async () =>
                await container.Resolve<ShellViewModel>().OpenSettings());
        };
        tray.PauseResumeAllRequested += (_, _) => container.Resolve<IDownloadQueueService>().PauseAll();
        tray.OpenFolderRequested += (_, _) => OpenDownloadFolder(container);
        tray.ExitRequested += (_, _) => ExitApplication(container);
        logger.Info("Tray", "트레이 아이콘 초기화됨");
    }

    private static void ConnectToast(IContainer container)
    {
        var queue = container.Resolve<IDownloadQueueService>();
        var toast = container.Resolve<ToastNotificationService>();
        queue.ItemChanged += (_, item) =>
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (item.Status == DownloadStatus.Completed)
                    toast.NotifyCompleted(item.Title ?? item.OriginalUrl);
                else if (item.Status == DownloadStatus.Failed)
                    toast.NotifyFailed(item.Title ?? item.OriginalUrl);
            });
        };
    }

    private static void HandleProtocolArgs(string[] args, IContainer container)
    {
        foreach (var arg in args)
        {
            if (!arg.StartsWith("mpdl://", StringComparison.OrdinalIgnoreCase))
                continue;
            var url = container.Resolve<ProtocolUrlParser>().Parse(arg);
            if (url is not null)
            {
                container.Resolve<IDownloadQueueService>().Enqueue(url);
                container.Resolve<IAppLogger>().Info("Protocol", "프로토콜 수신 → 큐 추가");
            }
        }
    }

    private void WireCloseToTray(IContainer container)
    {
        var window = Application.Current?.MainWindow;
        if (window is null)
            return;
        var settings = container.Resolve<ISettingsService>();
        window.Closing += (_, args) =>
        {
            if (settings.Current.CloseToTray && !_exiting)
            {
                args.Cancel = true;
                window.Hide();
            }
        };
    }

    private static void ShowMainWindow()
    {
        var window = Application.Current?.MainWindow;
        if (window is null)
            return;
        window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
        // 트레이/백그라운드에서도 확실히 전면으로 — Topmost 토글로 포그라운드 확보
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private static void OpenDownloadFolder(IContainer container)
    {
        var folder = container.Resolve<ISettingsService>().Current.DownloadFolder;
        try
        {
            if (Directory.Exists(folder))
                Process.Start("explorer.exe", folder);
        }
        catch { /* 무시 */ }
    }

    private void ExitApplication(IContainer container)
    {
        _exiting = true;
        TryRun(() => container.Resolve<TrayIconService>().Dispose());
        Application.Current?.Shutdown();
    }

    protected override void ConfigureContainer(ContainerBuilder builder)
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Multiplatform-Downloader");
        var toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
        var ytDlpPath = Path.Combine(toolsDir, "yt-dlp.exe");
        var settingsPath = Path.Combine(appDataDir, "settings.json");
        var logPath = Path.Combine(appDataDir, "logs", "app.log");

        // 번들 엔진(yt-dlp가 deno/ffmpeg를 찾도록)을 PATH 앞에 추가
        if (Directory.Exists(toolsDir))
            Environment.SetEnvironmentVariable("PATH", toolsDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));

        var clock = new SystemClock();
        var logger = new AppLogger(clock, logPath);

        // Core 서비스 등록
        builder.RegisterInstance<IClock>(clock);
        builder.RegisterInstance<IAppLogger>(logger);
        builder.RegisterType<ProcessRunner>().As<IProcessRunner>().SingleInstance();
        builder.RegisterInstance<ISettingsService>(new JsonSettingsService(settingsPath));
        builder.RegisterType<PlatformDetector>().AsSelf().SingleInstance();
        builder.Register(c => new BatchUrlParser(c.Resolve<PlatformDetector>())).AsSelf().SingleInstance();
        builder.RegisterType<MediaFormatSelector>().AsSelf().SingleInstance();
        builder.Register(c =>
        {
            var settings = c.Resolve<ISettingsService>();
            // 로그인 필요 콘텐츠 분석을 위해 현재 쿠키 설정을 매 조회에 반영(브라우저/파일)
            return new MediaMetadataService(c.Resolve<IProcessRunner>(), ytDlpPath,
                cookieProvider: () => settings.Current.ResolveCookies());
        }).As<IMediaMetadataService>().SingleInstance();
        builder.Register(c => new DownloadEngine(c.Resolve<IProcessRunner>(), ytDlpPath))
            .As<IDownloadEngine>().SingleInstance();

        // 샤오홍슈 다층 폴백(FR-13): yt-dlp → 자체 추출기 → 직접 스트림 다운로드
        builder.Register(_ => new XhsFallbackExtractor()).AsSelf().SingleInstance();
        builder.Register(_ => new DirectStreamDownloader()).As<IDirectStreamDownloader>().SingleInstance();
        builder.Register(c =>
        {
            // 페이지 HTML은 SSRF 가드 핸들러로 받는다(리다이렉트 허용, 각 연결 IP 검증)
            var http = new HttpClient(SsrfGuard.CreateGuardedHandler(allowAutoRedirect: true));
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            Task<string> FetchHtml(string url, CancellationToken ct) => http.GetStringAsync(url, ct);
            return new XhsResolutionStrategy(c.Resolve<IMediaMetadataService>(), c.Resolve<XhsFallbackExtractor>(), FetchHtml);
        }).As<IXhsResolutionStrategy>().SingleInstance();

        // 스레드 자체 폴백(FR-N1.8): yt-dlp 익스트랙터 부재 → HTML의 video_versions 직접 파싱.
        // 실측: 완전한 브라우저 네비게이션 헤더를 보내야 서버가 영상 JSON을 렌더한다(UA만 보내면 JS 셸).
        builder.Register(_ => new ThreadsFallbackExtractor()).AsSelf().SingleInstance();
        builder.Register(c =>
        {
            var http = new HttpClient(SsrfGuard.CreateGuardedHandler(allowAutoRedirect: true));
            async Task<string> FetchHtml(string url, CancellationToken ct)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
                req.Headers.TryAddWithoutValidation("Accept",
                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
                req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
                req.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
                req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
                req.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
                req.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
                req.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
                using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            return new ThreadsResolutionStrategy(c.Resolve<ThreadsFallbackExtractor>(), FetchHtml);
        }).As<IThreadsResolutionStrategy>().SingleInstance();

        builder.Register(c => new DownloadQueueService(
                c.Resolve<BatchUrlParser>(),
                c.Resolve<IMediaMetadataService>(),
                c.Resolve<IDownloadEngine>(),
                c.Resolve<ISettingsService>(),
                c.Resolve<MediaFormatSelector>(),
                resolveUrl: null,
                logger: c.Resolve<IAppLogger>(),
                xhsStrategy: c.Resolve<IXhsResolutionStrategy>(),
                directDownloader: c.Resolve<IDirectStreamDownloader>(),
                threadsStrategy: c.Resolve<IThreadsResolutionStrategy>()))
            .As<IDownloadQueueService>().SingleInstance();

        // 스플래시 (기동 진행 표시)
        builder.RegisterType<SplashScreenViewModel>().AsSelf().SingleInstance();

        // Windows 통합 서비스
        builder.Register(_ => new ProtocolUrlParser(new PlatformDetector())).AsSelf().SingleInstance();
        builder.RegisterType<StartupRegistrar>().AsSelf().SingleInstance();
        builder.Register(_ => new ProtocolRegistrar()).AsSelf().SingleInstance();
        builder.RegisterType<TrayIconService>().AsSelf().SingleInstance();
        builder.RegisterType<ToastNotificationService>().AsSelf().SingleInstance();

        logger.Info("App", "DI 컨테이너 구성 완료");
    }

    #endregion
    #region - Binding Methods -
    #endregion
    #region - Processes -
    #endregion
    #region - IHanldes -
    #endregion
    #region - Properties -
    #endregion
    #region - Attributes -
    #endregion
}

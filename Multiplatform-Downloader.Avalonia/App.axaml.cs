using Autofac;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Multiplatform_Downloader.Avalonia.Mvvm;
using Multiplatform_Downloader.Avalonia.Services;
using Multiplatform_Downloader.Avalonia.ViewModels;
using Multiplatform_Downloader.Avalonia.Views;
using Multiplatform_Downloader.Core.Abstractions;
using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Engine;
using Multiplatform_Downloader.Core.Ipc;
using Multiplatform_Downloader.Core.Net;
using Multiplatform_Downloader.Core.Platforms;
using Multiplatform_Downloader.Core.Queue;
using Multiplatform_Downloader.Core.Settings;
using Multiplatform_Downloader.Core.Update;
using System.Diagnostics;

namespace Multiplatform_Downloader.Avalonia;

/// <summary>
/// macOS 헤드의 부트스트랩 — WPF Bootstrapper.cs 이식.
/// 변경점: 관리자 강등(Windows 전용) 삭제, mpdl://는 argv 대신 IActivatableLifetime로 수신,
/// 타이틀바 테마 페인팅 삭제(시스템 처리), 설치 전환은 tar.gz 번들 교체(MacUpdateInstaller).
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = "MultiplatformDownloader.SingleInstance";
    private const string IpcPipeName = "MultiplatformDownloader.Ipc";

    private IContainer? _container;
    private bool _exiting;
    private bool _installing;
    private bool _trayHintShown;
    private SingleInstanceGuard? _instanceGuard;
    private PipeIpcServer? _ipcServer;
    private QueuePersistence? _queuePersistence;
    private Timer? _queueSaveTimer;
    private readonly CancellationTokenSource _cts = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            RegisterGlobalExceptionHandlers();
            desktop.Exit += (_, _) => OnExit();
            desktop.ShutdownRequested += (_, _) => _exiting = true; // OS 종료/로그아웃 — 닫기 가드 통과
            _ = StartupAsync(desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartupAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            // 단일 인스턴스 보장(FR-08) — 실패해도 기동을 막지 않는다
            try
            {
                _instanceGuard = new SingleInstanceGuard(SingleInstanceMutexName);
                if (!_instanceGuard.IsPrimaryInstance)
                {
                    var protocolArg = Array.Find(desktop.Args ?? [], a => a.StartsWith("mpdl://", StringComparison.OrdinalIgnoreCase));
                    if (protocolArg is not null)
                    {
                        await PipeIpcClient.TrySendAsync(IpcPipeName, protocolArg, TimeSpan.FromSeconds(2));
                    }
                    else
                    {
                        // 업데이트 재실행 경합: 구 인스턴스가 종료 중일 수 있다 — 짧게 재시도해
                        // 자리가 비면 주 인스턴스로 계속 진행한다(자동 업데이트 후 크래시/미기동 수정)
                        for (var attempt = 0; attempt < 4 && !_instanceGuard.IsPrimaryInstance; attempt++)
                        {
                            _instanceGuard.Dispose();
                            await Task.Delay(1000);
                            _instanceGuard = new SingleInstanceGuard(SingleInstanceMutexName);
                        }
                    }
                    if (!_instanceGuard.IsPrimaryInstance)
                    {
                        _exiting = true;
                        // 주의: 메인 루프 시작 전 Shutdown()은 "Dispatcher shut down" 크래시 —
                        // 루프가 돈 뒤 처리되도록 디스패처에 미룬다(실측 크래시 리포트 2건)
                        Dispatcher.UIThread.Post(() => desktop.Shutdown());
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] SingleInstance/IPC 실패(무시하고 계속): {ex}");
            }

            var container = BuildContainer();
            _container = container;

            var args = desktop.Args ?? [];
            var minimized = Array.Exists(args, a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

            // 스플래시 — 백그라운드 기동 시 미표시
            SplashScreenViewModel? splash = null;
            SplashScreenView? splashWindow = null;
            if (!minimized)
            {
                splash = container.Resolve<SplashScreenViewModel>();
                splash.Version = $"v{typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?"}";
                splashWindow = new SplashScreenView { DataContext = splash };
                splashWindow.Show();
            }

            async Task StepAsync(string message, int progress)
            {
                if (splash is null)
                    return;
                splash.UpdateStep(message, progress);
                await Task.Delay(110);
            }

            var logger = container.Resolve<IAppLogger>();
            var settings = container.Resolve<ISettingsService>();

            await StepAsync("설정을 불러오는 중...", 12);
            await settings.LoadAsync(_cts.Token);
            TryRun(() => ThemeController.Apply(settings.Current.Theme));
            logger.Info("App", $"앱 시작. 다운로드 폴더: {settings.Current.DownloadFolder}");

            await StepAsync("다운로드 엔진을 확인하는 중...", 28);
            var toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
            var health = new EngineHealthCheck(toolsDir).Check();
            if (health.AllPresent)
                logger.Info("Engine", "엔진 바이너리 모두 존재");
            else
                logger.Warning("Engine", $"누락된 엔진 바이너리: {string.Join(", ", health.Missing)} — tools 폴더에 배치하세요 ({toolsDir})");

            await StepAsync("로그인 항목을 설정하는 중...", 48);
            // mpdl:// 스킴은 .app 번들 Info.plist(CFBundleURLTypes)가 선언 — 런타임 등록 불필요
            if (!settings.SettingsFileExisted)
            {
                TryRun(() => settings.Current.LaunchOnStartup = container.Resolve<MacStartupRegistrar>().IsEnabled());
                await settings.SaveAsync(_cts.Token);
            }
            TryRun(() => container.Resolve<MacStartupRegistrar>().SetEnabled(settings.Current.LaunchOnStartup, settings.Current.StartMinimized));

            await StepAsync("메뉴바 아이콘을 초기화하는 중...", 66);
            TryRun(() => InitializeTray(container, logger));

            await StepAsync("알림·IPC 서비스를 시작하는 중...", 78);
            TryRun(() => ConnectToast(container));
            TryRun(() => StartIpcServer(container, logger));
            TryRun(() => WireQueuePersistence(container, logger));
            TryRun(() => container.Resolve<IDownloadQueueService>().SweepOrphanPartials());

            await StepAsync("메인 화면을 준비하는 중...", 90);

            var shell = container.Resolve<ShellViewModel>();
            var shellWindow = new ShellView { DataContext = shell };
            desktop.MainWindow = shellWindow;
            shellWindow.Show();

            WireProtocolActivation(container);
            HandleProtocolArgs(args, container);
            WireCloseToTray(shellWindow, container);

            await TryRestoreQueueAsync(container);

            if (minimized)
                shellWindow.Hide();

            if (splash is not null && splashWindow is not null)
            {
                splash.UpdateStep("준비가 완료되었습니다.", 100);
                await Task.Delay(300);
                splashWindow.Close();
            }

            // 자동 업데이트 확인(FR-U) — 셸 표시·복원 완료 후
            var isProtocolSession = Array.Exists(args, a => a.StartsWith("mpdl://", StringComparison.OrdinalIgnoreCase));
            if (!isProtocolSession)
                ScheduleUpdateCheck(container, windowVisible: !minimized);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Startup failed: {ex}");
        }
    }

    // ── 업데이트(FR-U5의 macOS 분기) ──

    private void ScheduleUpdateCheck(IContainer container, bool windowVisible)
    {
        var coordinator = container.Resolve<UpdateCoordinator>();
        var logger = container.Resolve<IAppLogger>();

        coordinator.InstallAction = archivePath => StartInstallAsync(container, archivePath);
        coordinator.ShowMainWindowAction = ShowMainWindow;
        coordinator.BalloonAction = (title, msg) =>
            TryRun(() => container.Resolve<MacNotificationService>().Show(title, msg));

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                await Dispatcher.UIThread.InvokeAsync(
                    () => coordinator.CheckAutoAsync(windowVisible, _cts.Token));
            }
            catch (Exception ex) { logger.Warning("Update", $"자동 확인 실패: {ex.GetType().Name} {ex.Message}"); }
        });
    }

    /// <summary>설치 전환 — 활성 다운로드 정리 → 큐 저장 → 번들 교체 + 재실행 → 종료.</summary>
    private async Task<bool> StartInstallAsync(IContainer container, string archivePath)
    {
        if (_installing)
            return true;
        _installing = true;

        var logger = container.Resolve<IAppLogger>();
        var queue = container.Resolve<IDownloadQueueService>();

        var active = queue.Items.Count(i => i.Status is DownloadStatus.Downloading or DownloadStatus.Analyzing or DownloadStatus.Merging);
        if (active > 0)
        {
            var confirm = new ConfirmDialogViewModel(
                "업데이트 설치",
                $"진행 중인 다운로드 {active}건이 중단됩니다. 계속할까요?",
                confirmText: "설치", cancelText: "취소");
            await container.Resolve<IWindowManager>().ShowDialogAsync(confirm);
            if (!confirm.Confirmed)
            {
                _installing = false;
                return false;
            }
        }

        TryRun(() => queue.PauseAll());
        await Task.Delay(500);

        try
        {
            if (_queuePersistence is not null)
                await _queuePersistence.SaveAsync(queue.Items).WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex) { logger.Warning("Update", $"종료 전 큐 저장 실패: {ex.Message}"); }

        if (_exiting)
        {
            _installing = false;
            return false;
        }

        // 새 인스턴스가 주 인스턴스로 뜰 수 있도록 파이프·뮤텍스를 먼저 놓는다(재실행 경합 방지)
        TryRun(() => { _ipcServer?.Dispose(); _ipcServer = null; });
        TryRun(() => { _instanceGuard?.Dispose(); _instanceGuard = null; });

        var installer = container.Resolve<MacUpdateInstaller>();
        if (!installer.InstallAndScheduleRelaunch(archivePath))
        {
            _installing = false;
            return false;
        }

        _exiting = true;
        TryRun(() => container.Resolve<TrayService>().Dispose());
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        return true;
    }

    // ── 큐 영속화(FR-D3.5) ──

    private void WireQueuePersistence(IContainer container, IAppLogger logger)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Multiplatform-Downloader", "queue-state.json");
        _queuePersistence = new QueuePersistence(path);

        var queue = container.Resolve<IDownloadQueueService>();
        _queueSaveTimer = new Timer(_ =>
        {
            try { _queuePersistence.SaveAsync(queue.Items).GetAwaiter().GetResult(); }
            catch (Exception ex) { Debug.WriteLine($"[QueuePersistence] save failed: {ex.Message}"); }
        }, null, Timeout.Infinite, Timeout.Infinite);

        queue.ItemChanged += (_, _) => _queueSaveTimer?.Change(1000, Timeout.Infinite);
        logger.Info("Queue", "큐 영속화 배선됨 (queue-state.json)");
    }

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

    // ── IPC + 프로토콜(FR-08) ──

    private void StartIpcServer(IContainer container, IAppLogger logger)
    {
        _ipcServer = new PipeIpcServer(IpcPipeName);
        _ipcServer.MessageReceived += (_, message) =>
            Dispatcher.UIThread.Post(() =>
            {
                HandleProtocolArgs([message], container);
                ShowMainWindow();
            });
        _ipcServer.Start();
        logger.Info("Ipc", "단일 인스턴스 파이프 서버 시작");
    }

    /// <summary>macOS URL 스킴 수신 — Finder/브라우저의 mpdl:// 열기는 argv가 아니라 활성화 이벤트로 온다.</summary>
    private void WireProtocolActivation(IContainer container)
    {
        if (TryGetFeature(typeof(IActivatableLifetime)) is not IActivatableLifetime activatable)
            return;
        activatable.Activated += (_, e) =>
        {
            if (e is ProtocolActivatedEventArgs { Uri: { } uri })
            {
                HandleProtocolArgs([uri.ToString()], container);
                ShowMainWindow();
            }
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

    // ── 트레이(메뉴바)·알림 ──

    private void InitializeTray(IContainer container, IAppLogger logger)
    {
        var tray = container.Resolve<TrayService>();
        tray.OpenRequested += (_, _) => ShowMainWindow();
        tray.SettingsRequested += (_, _) =>
        {
            ShowMainWindow();
            Dispatcher.UIThread.Post(async () =>
                await container.Resolve<ShellViewModel>().OpenSettings());
        };
        tray.PauseResumeAllRequested += (_, _) => container.Resolve<IDownloadQueueService>().PauseAll();
        tray.OpenFolderRequested += (_, _) => OpenDownloadFolder(container);
        tray.ExitRequested += (_, _) => ExitApplication(container);

        if (tray.Initialize())
            logger.Info("Tray", "메뉴바 아이콘 초기화됨");
        else
            logger.Warning("Tray", $"메뉴바 아이콘 생성 실패 — {tray.LastError}");
    }

    private static void ConnectToast(IContainer container)
    {
        var queue = container.Resolve<IDownloadQueueService>();
        var toast = container.Resolve<ToastNotificationService>();
        queue.ItemChanged += (_, item) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (item.Status == DownloadStatus.Completed)
                    toast.NotifyCompleted(item.Title ?? item.OriginalUrl);
                else if (item.Status == DownloadStatus.Failed)
                    toast.NotifyFailed(item.Title ?? item.OriginalUrl);
            });
        };
    }

    // ── 창 닫기 = 메뉴바로 최소화(설정) ──

    private void WireCloseToTray(Window window, IContainer container)
    {
        var settings = container.Resolve<ISettingsService>();
        var logger = container.Resolve<IAppLogger>();

        window.Closing += (_, e) =>
        {
            if (_exiting)
                return;

            if (!settings.Current.CloseToTray)
            {
                ExitApplication(container);
                return;
            }

            e.Cancel = true;
            var tray = container.Resolve<TrayService>();
            if (tray.EnsureCreated())
            {
                window.Hide();
                NotifyMinimizedToTrayOnce(container);
            }
            else
            {
                logger.Warning("Tray", $"메뉴바 아이콘 미가용 — 창을 닫지 않고 유지({tray.LastError})");
                window.Show();
                window.Activate();
            }
        };
    }

    private void NotifyMinimizedToTrayOnce(IContainer container)
    {
        if (_trayHintShown)
            return;
        _trayHintShown = true;
        TryRun(() => container.Resolve<MacNotificationService>().Show(
            "메뉴바로 최소화됨",
            "앱은 계속 실행 중입니다. 완전히 끄려면 메뉴바 아이콘에서 '완전 종료'를 선택하세요."));
    }

    private void ShowMainWindow()
    {
        var window = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window is null)
            return;
        window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private static void OpenDownloadFolder(IContainer container)
    {
        var folder = container.Resolve<ISettingsService>().Current.DownloadFolder;
        if (Directory.Exists(folder))
            Finder.OpenFolder(folder);
    }

    private void ExitApplication(IContainer container)
    {
        if (_exiting)
            return;
        _exiting = true;
        TryRun(() => container.Resolve<TrayService>().Dispose());
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private void OnExit()
    {
        TryRun(() =>
        {
            _queueSaveTimer?.Dispose();
            var queue = _container?.Resolve<IDownloadQueueService>();
            if (queue is not null && _queuePersistence is not null)
                _queuePersistence.SaveAsync(queue.Items).Wait(TimeSpan.FromSeconds(2));
        });
        TryRun(() => _ipcServer?.Dispose());
        TryRun(() => _container?.Resolve<TrayService>().Dispose());
        TryRun(() => _instanceGuard?.Dispose());
        TryRun(() => { _cts.Cancel(); _cts.Dispose(); });
    }

    // ── 전역 예외 방어(WPF StartupGuards 이식 — 강등 로직 제외) ──

    private static void RegisterGlobalExceptionHandlers()
    {
        var fatalLog = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Multiplatform-Downloader", "logs", "fatal.log");

        void WriteFatal(string source, object? error)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fatalLog)!);
                File.AppendAllText(fatalLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {error}{Environment.NewLine}");
            }
            catch { /* 기록 실패는 무시 */ }
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteFatal("AppDomain", e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => { WriteFatal("TaskScheduler", e.Exception); e.SetObserved(); };
    }

    private static void TryRun(Action action)
    {
        try { action(); }
        catch (Exception ex) { Debug.WriteLine($"[App] {ex}"); }
    }

    // ── DI 컨테이너(WPF ConfigureContainer 이식) ──

    private IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Multiplatform-Downloader");
        var toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
        var ytDlpPath = Path.Combine(toolsDir, "yt-dlp");
        var settingsPath = Path.Combine(appDataDir, "settings.json");
        var logPath = Path.Combine(appDataDir, "logs", "app.log");

        // 번들 엔진(yt-dlp가 deno/ffmpeg를 찾도록)을 PATH 앞에 추가
        if (Directory.Exists(toolsDir))
            Environment.SetEnvironmentVariable("PATH", toolsDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));

        var clock = new SystemClock();
        var logger = new AppLogger(clock, logPath);

        // MVVM 인프라
        builder.RegisterType<WindowManager>().As<IWindowManager>().SingleInstance();

        // Core 서비스 (WPF와 동일)
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
            return new MediaMetadataService(c.Resolve<IProcessRunner>(), ytDlpPath,
                cookieProvider: () => settings.Current.ResolveCookies());
        }).As<IMediaMetadataService>().SingleInstance();
        builder.Register(c => new DownloadEngine(c.Resolve<IProcessRunner>(), ytDlpPath))
            .As<IDownloadEngine>().SingleInstance();

        // 샤오홍슈 다층 폴백(FR-13)
        builder.Register(_ => new XhsFallbackExtractor()).AsSelf().SingleInstance();
        builder.Register(_ => new DirectStreamDownloader()).As<IDirectStreamDownloader>().SingleInstance();
        builder.Register(c =>
        {
            var http = new HttpClient(SsrfGuard.CreateGuardedHandler(allowAutoRedirect: true));
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)");
            Task<string> FetchHtml(string url, CancellationToken ct) => http.GetStringAsync(url, ct);
            return new XhsResolutionStrategy(c.Resolve<IMediaMetadataService>(), c.Resolve<XhsFallbackExtractor>(), FetchHtml);
        }).As<IXhsResolutionStrategy>().SingleInstance();

        // 스레드 자체 폴백(FR-N1.8) — 완전한 브라우저 내비게이션 헤더 필요(실측)
        builder.Register(_ => new ThreadsFallbackExtractor()).AsSelf().SingleInstance();
        builder.Register(c =>
        {
            var http = new HttpClient(SsrfGuard.CreateGuardedHandler(allowAutoRedirect: true));
            async Task<string> FetchHtml(string url, CancellationToken ct)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
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
                req.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"macOS\"");
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

        // 셸·스플래시
        builder.RegisterType<ShellViewModel>().AsSelf().SingleInstance();
        builder.RegisterType<SplashScreenViewModel>().AsSelf().SingleInstance();

        // macOS 통합 서비스
        builder.Register(_ => new ProtocolUrlParser(new PlatformDetector())).AsSelf().SingleInstance();
        builder.RegisterType<MacStartupRegistrar>().AsSelf().SingleInstance();
        builder.RegisterType<TrayService>().AsSelf().SingleInstance();
        builder.RegisterType<MacNotificationService>().AsSelf().SingleInstance();
        builder.RegisterType<ToastNotificationService>().AsSelf().SingleInstance();
        builder.RegisterType<ClipboardService>().AsSelf().SingleInstance();

        // 자동 업데이트(FR-U)
        builder.Register(_ => new JsonUpdateStateStore()).As<IUpdateStateStore>().SingleInstance();
        builder.Register(c => new GitHubUpdateChecker(
                c.Resolve<IUpdateStateStore>(), c.Resolve<IClock>(), c.Resolve<IAppLogger>()))
            .As<IUpdateChecker>().SingleInstance();
        builder.Register(c => new MacUpdateInstaller(c.Resolve<IAppLogger>()))
            .AsSelf().As<IUpdatePackageProvider>().SingleInstance();
        builder.Register(c => new UpdateCoordinator(
                c.Resolve<IUpdateChecker>(), c.Resolve<IUpdatePackageProvider>(), c.Resolve<ISettingsService>(),
                c.Resolve<IUpdateStateStore>(), c.Resolve<IClock>(), c.Resolve<IWindowManager>(), c.Resolve<IAppLogger>()))
            .AsSelf().SingleInstance();

        logger.Info("App", "DI 컨테이너 구성 완료 (macOS 헤드)");
        return builder.Build();
    }
}

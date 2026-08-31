using System.Reflection;
using Multiplatform_Downloader.Avalonia.Mvvm;
using Multiplatform_Downloader.Avalonia.ViewModels;
using Multiplatform_Downloader.Core.Abstractions;
using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Settings;
using Multiplatform_Downloader.Core.Update;

namespace Multiplatform_Downloader.Avalonia.Services;

/// <summary>수동 업데이트 확인 결과(About 피드백용, FR-U6.3).</summary>
public enum ManualCheckOutcome { UpdateAvailable, UpToDate, Failed }

/// <summary>
/// 업데이트 체크·안내·설치 전환 조율(FR-U4·U5·U6) — WPF 헤드 이식(거의 무수정).
/// 설치 전환(PauseAll→저장→교체→재실행)은 App이 주입한 콜백에 위임한다.
/// </summary>
public sealed class UpdateCoordinator
{
    private static readonly TimeSpan RemindSuppression = TimeSpan.FromHours(24);

    private readonly IUpdateChecker _checker;
    private readonly IUpdatePackageProvider _installer;
    private readonly ISettingsService _settings;
    private readonly IUpdateStateStore _stateStore;
    private readonly IClock _clock;
    private readonly IWindowManager _windowManager;
    private readonly IAppLogger _logger;

    private bool _shownThisSession;

    /// <summary>설치 전환 콜백 — 검증된 패키지 경로를 받아 정리→교체→재실행. 반환=성공 여부.</summary>
    public Func<string, Task<bool>>? InstallAction { get; set; }

    public Action? ShowMainWindowAction { get; set; }
    public Action<string, string>? BalloonAction { get; set; }

    public UpdateCoordinator(
        IUpdateChecker checker,
        IUpdatePackageProvider installer,
        ISettingsService settings,
        IUpdateStateStore stateStore,
        IClock clock,
        IWindowManager windowManager,
        IAppLogger logger)
    {
        _checker = checker;
        _installer = installer;
        _settings = settings;
        _stateStore = stateStore;
        _clock = clock;
        _windowManager = windowManager;
        _logger = logger;
    }

    private static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>자동 경로(시작 시). 설정 off·개발 빌드·억제 조건이면 조용히 종료.</summary>
    public async Task CheckAutoAsync(bool windowVisible, CancellationToken ct = default)
    {
        if (!_settings.Current.AutoUpdateCheck)
        {
            _logger.Info("Update", "자동 업데이트 확인 꺼짐 — 스킵");
            return;
        }
        if (System.Diagnostics.Debugger.IsAttached)
        {
            _logger.Info("Update", "디버거 연결 — 자동 체크 스킵(NFR-U2)");
            return;
        }

        var info = await _checker.FetchLatestAsync(ct).ConfigureAwait(true);
        if (info is null)
            return;

        var decision = UpdateDecision.Decide(
            CurrentVersion, info.Version,
            _settings.Current.SkippedVersion,
            _stateStore.Load().LastRemindedAtUtc,
            _clock.UtcNow, RemindSuppression, _shownThisSession);

        if (decision != UpdateNotifyResult.Notify)
            return;

        if (windowVisible)
            await ShowDialogAsync(info).ConfigureAwait(true);
        else
            BalloonAction?.Invoke("새 버전 사용 가능", $"v{info.Version.ToString(3)} — 클릭해 업데이트하세요");
    }

    /// <summary>수동 경로(About [업데이트 확인]) — 결과를 항상 반환(FR-U6.3).</summary>
    public async Task<ManualCheckOutcome> CheckManualAsync(CancellationToken ct = default)
    {
        var info = await _checker.FetchLatestAsync(ct).ConfigureAwait(true);
        if (info is null)
            return _checker.LastFailure != UpdateCheckFailure.None
                ? ManualCheckOutcome.Failed
                : ManualCheckOutcome.UpToDate;

        if (!VersionComparer.IsNewer(info.Version, CurrentVersion))
            return ManualCheckOutcome.UpToDate;

        await ShowDialogAsync(info).ConfigureAwait(true);
        return ManualCheckOutcome.UpdateAvailable;
    }

    private async Task ShowDialogAsync(UpdateInfo info)
    {
        _shownThisSession = true;
        ShowMainWindowAction?.Invoke();

        var vm = new UpdateViewModel(
            info, CurrentVersion, _installer,
            onInstall: async path =>
                InstallAction is not null && await InstallAction(path),
            onSkip: version =>
            {
                _settings.Current.SkippedVersion = version;
                _ = _settings.SaveAsync();
                _logger.Info("Update", $"버전 건너뛰기: {version}");
            },
            onLater: SaveReminded,
            logger: _logger);

        await _windowManager.ShowDialogAsync(vm).ConfigureAwait(true);
    }

    private void SaveReminded()
    {
        var state = _stateStore.Load();
        state.LastRemindedAtUtc = _clock.UtcNow;
        _stateStore.Save(state);
    }
}

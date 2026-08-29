using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using Multiplatform_Downloader.Core.Abstractions;
using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Settings;
using Multiplatform_Downloader.Core.Update;
using Multiplatform_Downloader.ViewModels;

namespace Multiplatform_Downloader.Services;

/// <summary>수동 업데이트 확인 결과(About 피드백용, FR-U6.3).</summary>
public enum ManualCheckOutcome { UpdateAvailable, UpToDate, Failed }

/// <summary>
/// 업데이트 체크·안내·설치 전환을 조율한다(FR-U4·U5·U6). 자동 경로(시작 시)와 수동 경로(About)가 공유한다.
/// 설치 전환(PauseAll→저장→종료)은 Bootstrapper가 주입한 콜백에 위임한다.
/// </summary>
public sealed class UpdateCoordinator
{
    private static readonly TimeSpan RemindSuppression = TimeSpan.FromHours(24);

    private readonly IUpdateChecker _checker;
    private readonly UpdateInstaller _installer;
    private readonly ISettingsService _settings;
    private readonly IUpdateStateStore _stateStore;
    private readonly IClock _clock;
    private readonly IWindowManager _windowManager;
    private readonly IAppLogger _logger;

    private bool _shownThisSession;

    /// <summary>설치 전환 콜백 — 다운로드 완료된 exe 경로를 받아 PauseAll→저장→Process.Start→Shutdown 수행.
    /// 반환: 인스톨러 실행 성공 여부(true면 앱이 곧 종료됨, false면 취소·거부로 복귀). (M1)</summary>
    public Func<string, Task<bool>>? InstallAction { get; set; }

    /// <summary>메인 창을 전면 표시(트레이 풍선 클릭·설치 전 복원).</summary>
    public System.Action? ShowMainWindowAction { get; set; }

    /// <summary>트레이 풍선 알림(창 숨김 시 다이얼로그 대체, FR-U4.3).</summary>
    public System.Action<string, string>? BalloonAction { get; set; }

    public UpdateCoordinator(
        IUpdateChecker checker,
        UpdateInstaller installer,
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
            return; // 자동 경로 무소음 (304는 캐시 info를 반환하므로 여기 도달하지 않음 — B1)

        var decision = UpdateDecision.Decide(
            CurrentVersion, info.Version,
            _settings.Current.SkippedVersion,
            _stateStore.Load().LastRemindedAtUtc,   // 영속 리마인더(H1) — 재시작에도 지속
            _clock.UtcNow, RemindSuppression, _shownThisSession);

        if (decision != UpdateNotifyResult.Notify)
            return;

        if (windowVisible)
            await ShowDialogAsync(info).ConfigureAwait(true);
        else
            // 창 숨김(트레이/minimized) — 다이얼로그 대신 풍선, 클릭 시 표시 (FR-U4.3)
            BalloonAction?.Invoke("새 버전 사용 가능", $"v{info.Version.ToString(3)} — 클릭해 업데이트하세요");
    }

    /// <summary>수동 경로(About [업데이트 확인]). 무소음 정책 무시 — 결과를 항상 반환한다(FR-U6.3).</summary>
    public async Task<ManualCheckOutcome> CheckManualAsync(CancellationToken ct = default)
    {
        var info = await _checker.FetchLatestAsync(ct).ConfigureAwait(true);
        if (info is null)
            return _checker.LastFailure != UpdateCheckFailure.None
                ? ManualCheckOutcome.Failed
                : ManualCheckOutcome.UpToDate;

        // 수동은 스킵 버전도 무시(사용자 능동 요청)
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
                InstallAction is not null && await InstallAction(path), // M1: 성공 여부 전달
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

    /// <summary>[나중에]/닫기 시각을 update-state.json에 저장(H1) — 전용 필드, 재시작에도 지속.</summary>
    private void SaveReminded()
    {
        var state = _stateStore.Load();
        state.LastRemindedAtUtc = _clock.UtcNow;
        _stateStore.Save(state);
    }
}

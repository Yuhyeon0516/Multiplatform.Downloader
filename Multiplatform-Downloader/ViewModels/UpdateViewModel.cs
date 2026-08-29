using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Models;
using Multiplatform_Downloader.Core.Update;

namespace Multiplatform_Downloader.ViewModels;

/// <summary>
/// 새 버전 안내·다운로드 진행·설치 전환 다이얼로그(FR-U4). 테마 일치(Wf* 토큰, ConfirmDialog 패턴).
/// 릴리스 노트는 평문만, 다운로드는 취소 가능, 설치 전환은 주입된 콜백에 위임한다.
/// </summary>
public sealed class UpdateViewModel : Screen
{
    private const int MaxNotesLength = 8192; // FR-U4.2

    private readonly UpdateInfo _info;
    private readonly Version _currentVersion;
    private readonly UpdateInstaller _installer;
    private readonly IAppLogger _logger;
    private readonly Func<string, Task<bool>> _onInstall;   // exe 경로 → 설치 전환. 반환=성공(앱 종료 임박) 여부(M1)
    private readonly System.Action<string>? _onSkip;         // [건너뛰기] → SkippedVersion 저장
    private readonly System.Action? _onLater;                // [나중에]/닫기 → LastRemindedAt 저장

    private CancellationTokenSource? _downloadCts;

    public UpdateViewModel(
        UpdateInfo info,
        Version currentVersion,
        UpdateInstaller installer,
        Func<string, Task<bool>> onInstall,
        System.Action<string>? onSkip = null,
        System.Action? onLater = null,
        IAppLogger? logger = null)
    {
        _info = info;
        _currentVersion = currentVersion;
        _installer = installer;
        _onInstall = onInstall;
        _onSkip = onSkip;
        _onLater = onLater;
        _logger = logger ?? NullAppLogger.Instance;

        DisplayName = "업데이트";
        NewVersion = $"v{info.Version.ToString(3)}";
        CurrentVersionText = $"v{VersionComparer.Normalize(currentVersion).ToString(3)}";
        ReleaseNotes = SanitizeNotes(info.ReleaseNotes);
        SetPromptState();
    }

    public string NewVersion { get; }
    public string CurrentVersionText { get; }
    public string ReleaseNotes { get; }

    private string _statusText = string.Empty;
    public string StatusText { get => _statusText; private set { _statusText = value; NotifyOfPropertyChange(); } }

    private double _progress;
    public double Progress { get => _progress; private set { _progress = value; NotifyOfPropertyChange(); } }

    private bool _isDownloading;
    public bool IsDownloading { get => _isDownloading; private set { _isDownloading = value; NotifyOfPropertyChange(); NotifyButtons(); } }

    // 버튼 게이트 — 다운로드 중에는 설치/나중에/건너뛰기 숨기고 취소만
    public bool CanInstallNow => !IsDownloading;
    public bool ShowPromptButtons => !IsDownloading;

    /// <summary>[지금 설치] — 다운로드(진행률·취소) 후 설치 전환 콜백 호출.</summary>
    public async Task InstallNow()
    {
        if (IsDownloading)
            return;
        IsDownloading = true;
        Progress = 0;
        StatusText = "다운로드 중…";
        _downloadCts = new CancellationTokenSource();

        var progress = new DelegateProgress(p =>
        {
            Progress = p.Percent;
            StatusText = $"다운로드 중… {p.Percent:0}%";
        });

        try
        {
            // 캐시된 유효 설치본이 있으면 재사용(단, 실행 직전 검증은 항상 재수행 — FR-U3.4)
            var cached = _installer.FindCached(_info.Version);
            UpdateDownloadResult result;
            if (cached is not null && _installer.VerifyInstaller(cached, _info.Version, _currentVersion).Success)
            {
                result = UpdateDownloadResult.Ok(cached);
                Progress = 100;
            }
            else
            {
                result = await _installer.DownloadAsync(_info, _currentVersion, progress, _downloadCts.Token);
            }

            if (!result.Success || result.InstallerPath is null)
            {
                IsDownloading = false;
                StatusText = result.Error ?? "다운로드에 실패했습니다";
                return;
            }

            StatusText = "설치를 시작합니다…";
            var started = await _onInstall(result.InstallerPath);
            if (started)
            {
                // 성공 — 앱이 곧 종료된다. UI를 '취소됨'으로 바꾸지 않는다(M1).
                StatusText = "설치 후 자동으로 다시 시작됩니다…";
                return;
            }
            // 여기 도달 = UAC 거부·활성 다운로드 취소 등으로 설치 전환이 중단됨
            IsDownloading = false;
            StatusText = "설치가 취소되었습니다. 다시 시도할 수 있습니다.";
        }
        catch (OperationCanceledException)
        {
            IsDownloading = false;
            StatusText = "다운로드를 취소했습니다.";
        }
        catch (Exception ex)
        {
            IsDownloading = false;
            StatusText = "다운로드 중 오류가 발생했습니다.";
            _logger.Warning("Update", $"다운로드 예외: {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>[취소] — 진행 중 다운로드 중단(정상 흐름).</summary>
    public void CancelDownload() => _downloadCts?.Cancel();

    /// <summary>[나중에] — 24h 억제 후 재안내.</summary>
    public async Task Later()
    {
        _onLater?.Invoke();
        await TryCloseAsync(false);
    }

    /// <summary>[이 버전 건너뛰기] — 해당 버전만 억제(상위 도착 시 재안내).</summary>
    public async Task Skip()
    {
        _onSkip?.Invoke(_info.Version.ToString());
        await TryCloseAsync(false);
    }

    /// <summary>창 X/ESC 닫기 = [나중에]와 동일(절대 스킵 아님, FR-U4.4).</summary>
    public override async Task<bool> CanCloseAsync(CancellationToken cancellationToken = default)
    {
        // 다운로드 중이면 먼저 취소. 닫기 = [나중에]와 동일(다운로드 중이어도 억제 저장, M4)
        _downloadCts?.Cancel();
        _onLater?.Invoke();
        return await base.CanCloseAsync(cancellationToken);
    }

    private void SetPromptState()
    {
        StatusText = $"새 버전 {NewVersion} 이(가) 있습니다. (현재 {CurrentVersionText})";
    }

    private void NotifyButtons()
    {
        NotifyOfPropertyChange(nameof(CanInstallNow));
        NotifyOfPropertyChange(nameof(ShowPromptButtons));
    }

    private static string SanitizeNotes(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "(릴리스 노트 없음)";
        // 제어문자 스트립(개행/탭 제외), 길이 상한 (FR-U4.2)
        var cleaned = new string(raw.Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t').ToArray());
        return cleaned.Length > MaxNotesLength ? cleaned[..MaxNotesLength] + "…" : cleaned;
    }

    /// <summary>동기 Report IProgress 구현.</summary>
    private sealed class DelegateProgress(System.Action<DownloadProgress> onReport) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => onReport(value);
    }
}

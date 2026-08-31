using Multiplatform_Downloader.Avalonia.Mvvm;
using Multiplatform_Downloader.Avalonia.Services;
using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Models;
using Multiplatform_Downloader.Core.Update;

namespace Multiplatform_Downloader.Avalonia.ViewModels;

/// <summary>
/// 새 버전 안내·다운로드 진행·설치 전환 다이얼로그(FR-U4) — WPF 헤드 이식.
/// 변경점: Core UpdateInstaller 직접 의존 → IUpdatePackageProvider(macOS: tar.gz + SHA256).
/// </summary>
public sealed class UpdateViewModel : Screen
{
    private const int MaxNotesLength = 8192; // FR-U4.2

    private readonly UpdateInfo _info;
    private readonly Version _currentVersion;
    private readonly IUpdatePackageProvider _installer;
    private readonly IAppLogger _logger;
    private readonly Func<string, Task<bool>> _onInstall;
    private readonly Action<string>? _onSkip;
    private readonly Action? _onLater;

    private CancellationTokenSource? _downloadCts;

    public UpdateViewModel(
        UpdateInfo info,
        Version currentVersion,
        IUpdatePackageProvider installer,
        Func<string, Task<bool>> onInstall,
        Action<string>? onSkip = null,
        Action? onLater = null,
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
        StatusText = $"새 버전 {NewVersion} 이(가) 있습니다. (현재 {CurrentVersionText})";
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
            var cached = _installer.FindCachedVerified(_info, _currentVersion);
            UpdateDownloadResult result;
            if (cached is not null)
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
                StatusText = "설치 후 자동으로 다시 시작됩니다…";
                return;
            }
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

    public void CancelDownload() => _downloadCts?.Cancel();

    public async Task Later()
    {
        _onLater?.Invoke();
        await TryCloseAsync(false);
    }

    public async Task Skip()
    {
        _onSkip?.Invoke(_info.Version.ToString());
        await TryCloseAsync(false);
    }

    /// <summary>창 X/ESC 닫기 = [나중에]와 동일(FR-U4.4).</summary>
    public override Task<bool> CanCloseAsync(CancellationToken cancellationToken = default)
    {
        _downloadCts?.Cancel();
        _onLater?.Invoke();
        return Task.FromResult(true);
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
        var cleaned = new string(raw.Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t').ToArray());
        return cleaned.Length > MaxNotesLength ? cleaned[..MaxNotesLength] + "…" : cleaned;
    }

    private sealed class DelegateProgress(Action<DownloadProgress> onReport) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => onReport(value);
    }
}

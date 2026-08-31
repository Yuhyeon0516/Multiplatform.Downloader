using System.Reflection;
using Multiplatform_Downloader.Avalonia.Mvvm;
using Multiplatform_Downloader.Avalonia.Services;

namespace Multiplatform_Downloader.Avalonia.ViewModels;

/// <summary>프로그램 정보(About) 창 — WPF 헤드 이식. 수동 업데이트 확인(FR-U6.2) 포함.</summary>
internal sealed class AboutViewModel : Screen
{
    private readonly UpdateCoordinator? _updateCoordinator;

    public AboutViewModel(UpdateCoordinator? updateCoordinator = null)
    {
        _updateCoordinator = updateCoordinator;
        DisplayName = "정보";
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        Version = v is not null ? $"v{v.ToString(3)}" : "v?";
    }

    private string _updateStatus = string.Empty;
    public string UpdateStatus { get => _updateStatus; private set { _updateStatus = value; NotifyOfPropertyChange(); } }

    private bool _isChecking;
    public bool IsChecking { get => _isChecking; private set { _isChecking = value; NotifyOfPropertyChange(); NotifyOfPropertyChange(nameof(CanCheckUpdate)); } }

    public bool CanCheckUpdate => !IsChecking && _updateCoordinator is not null;

    public async Task CheckUpdate()
    {
        if (_updateCoordinator is null)
            return;
        IsChecking = true;
        UpdateStatus = "확인 중…";
        try
        {
            var outcome = await _updateCoordinator.CheckManualAsync();
            UpdateStatus = outcome switch
            {
                ManualCheckOutcome.UpToDate => $"현재 최신 버전입니다 ({Version})",
                ManualCheckOutcome.UpdateAvailable => "새 버전 안내 창을 확인하세요",
                _ => "확인할 수 없습니다 (네트워크·요청 한도 확인)",
            };
        }
        finally
        {
            IsChecking = false;
        }
    }

    public string ProductName { get; } = "샤샤룽 다운로더";
    public string ProductNameEn { get; } = "Shyshyroong Downloader";
    public string Version { get; }
    public string SubTitle { get; } = "YouTube · Instagram · TikTok · 샤오홍슈 영상 다운로더";
    public string Developer { get; } = "라이프백패커 (Lifebackpacker)";

    public string EngineInfo { get; } =
        "yt-dlp (Unlicense) · FFmpeg / ffprobe (LGPL·GPL) · Deno (MIT)";

    public string UsageNotice { get; } =
        "개인적·합법적 용도로만 사용하세요. 저작권 및 각 플랫폼의 이용약관을 준수할 책임은 사용자에게 있습니다.";

    public void OpenLegalNotice()
    {
        var docs = Path.Combine(AppContext.BaseDirectory, "docs", "legal-notice.md");
        if (File.Exists(docs))
            Finder.OpenWithDefaultApp(docs);
    }

    public async Task Close() => await TryCloseAsync(true);
}

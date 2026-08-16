using Multiplatform_Downloader.Core.Settings;

namespace Multiplatform_Downloader.Services;

/// <summary>완료/실패 토스트 알림(FR-10). 트레이 아이콘의 풍선 알림을 사용하며, 설정으로 on/off.</summary>
public sealed class ToastNotificationService
{
    private readonly TrayIconService _tray;
    private readonly ISettingsService _settings;

    public ToastNotificationService(TrayIconService tray, ISettingsService settings)
    {
        _tray = tray;
        _settings = settings;
    }

    public void NotifyCompleted(string title)
    {
        if (_settings.Current.NotifyOnComplete)
            _tray.ShowNotification("다운로드 완료", title);
    }

    public void NotifyFailed(string title)
    {
        if (_settings.Current.NotifyOnComplete)
            _tray.ShowNotification("다운로드 실패", title);
    }
}

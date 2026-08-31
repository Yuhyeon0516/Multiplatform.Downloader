using Multiplatform_Downloader.Core.Settings;

namespace Multiplatform_Downloader.Avalonia.Services;

/// <summary>완료/실패 토스트 알림(FR-10) — 설정으로 on/off. WPF 헤드와 동일 계약.</summary>
public sealed class ToastNotificationService
{
    private readonly MacNotificationService _notifier;
    private readonly ISettingsService _settings;

    public ToastNotificationService(MacNotificationService notifier, ISettingsService settings)
    {
        _notifier = notifier;
        _settings = settings;
    }

    public void NotifyCompleted(string title)
    {
        if (_settings.Current.NotifyOnComplete)
            _notifier.Show("다운로드 완료", title);
    }

    public void NotifyFailed(string title)
    {
        if (_settings.Current.NotifyOnComplete)
            _notifier.Show("다운로드 실패", title);
    }
}

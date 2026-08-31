using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Multiplatform_Downloader.Avalonia.Services;

/// <summary>클립보드 접근 — Avalonia는 TopLevel 경유라 서비스로 감싼다(WPF Clipboard 정적 API 대응).</summary>
public sealed class ClipboardService
{
    private static TopLevel? Top =>
        (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<string?> GetTextAsync()
    {
        var clipboard = Top?.Clipboard;
        return clipboard is null ? null : await clipboard.GetTextAsync();
    }

    public async Task SetTextAsync(string text)
    {
        var clipboard = Top?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }
}

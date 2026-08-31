using Avalonia.Styling;
using Multiplatform_Downloader.Core.Settings;

namespace Multiplatform_Downloader.Avalonia.Services;

/// <summary>
/// 테마 적용(FR-06) — WPF ThemeService의 macOS 대응. 타이틀바 페인팅(DwmSetWindowAttribute 등)은
/// macOS에서 시스템이 처리하므로 전부 불필요하고, ThemeVariant 전환만 남는다.
/// </summary>
public static class ThemeController
{
    public static void Apply(AppTheme theme)
    {
        var app = global::Avalonia.Application.Current;
        if (app is null)
            return;
        app.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default, // System — OS 설정 추종
        };
    }

    /// <summary>현재 실효 테마가 라이트인지 — 셸의 1클릭 토글 아이콘 판정용.</summary>
    public static bool IsEffectiveLight(AppTheme theme) => theme switch
    {
        AppTheme.Light => true,
        AppTheme.Dark => false,
        _ => global::Avalonia.Application.Current?.ActualThemeVariant == ThemeVariant.Light,
    };
}

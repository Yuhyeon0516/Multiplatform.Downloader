using Microsoft.Win32;
using Multiplatform_Downloader.Core.Settings;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Wpf.Ui.Appearance;

namespace Multiplatform_Downloader.Services;

/// <summary>
/// 라이트/다크 테마 적용(FR-06 Theme). WPF-UI 테마·와이어프레임 팔레트 사전(Themes/Wireframe*.xaml)·
/// 시스템 타이틀바(DWM 다크 모드)를 함께 전환한다. <see cref="AppTheme.System"/>은 Windows 설정을 따른다.
/// </summary>
internal static class ThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeOld = 19; // Windows 10 1809 이하
    private const int DwmCaptionColor = 35; // Windows 11: 타이틀바 배경색 (COLORREF)
    private const int DwmTextColor = 36;    // Windows 11: 타이틀바 글자색 (COLORREF)

    // COLORREF(0x00BBGGRR) — 와이어프레임 Surface/Ink 토큰과 일치
    private const int DarkCaption = 0x2B231F;  // #1F232B
    private const int DarkText = 0xEEEAE8;     // #E8EAEE
    private const int LightCaption = 0xFFFFFF; // #FFFFFF
    private const int LightText = 0x211E1C;    // #1C1E21

    private static bool _isLight;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct WtaOptions
    {
        public uint Flags;
        public uint Mask;
    }

    [DllImport("uxtheme.dll")]
    private static extern int SetWindowThemeAttribute(IntPtr hwnd, int attribute, ref WtaOptions options, int size);

    private const int WtaNonClient = 1;
    private const uint WtncaNoDrawCaption = 0x1;
    private const uint WtncaNoDrawIcon = 0x2;

    /// <summary>Windows 11(빌드 22000+) 여부 — WTNCA 복원은 Win10에서 캡션 흰 박스 부작용(실기기 확인).</summary>
    private static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    // FRAMECHANGED | NOMOVE | NOSIZE | NOZORDER | NOACTIVATE — 논클라이언트(타이틀바) 강제 재그리기
    private const uint SwpFrameRefresh = 0x0020 | 0x0002 | 0x0001 | 0x0004 | 0x0010;

    static ThemeService()
    {
        // 이후에 열리는 창(설정·일괄추가 다이얼로그)도 타이틀바 테마를 따르게 한다
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window && !window.AllowsTransparency)
                    ApplyTitleBar(window);
            }));
    }

    /// <summary>
    /// 창 타이틀바를 현재 테마(다크=흰 글씨, 라이트=검은 글씨)로 맞춘다.
    /// <paramref name="restoreCaption"/>: WPF-UI가 라이브 Apply 중 꺼버린 캡션 그리기를 복원(WTNCA).
    /// 실측 트레이드오프 — Win11 라이브 전환엔 필수(없으면 캡션 실종 회귀 확인),
    /// Win10에선 캡션에 흰 하이라이트 박스 부작용(윈도우컨트롤바_버그.png) → <b>라이브 Apply 경로 + Win11에서만</b> 적용.
    /// 새로 열리는 창(Loaded 핸들러)은 WPF-UI가 손대지 않으므로 복원 불필요.
    /// </summary>
    private static void ApplyTitleBar(Window window, bool restoreCaption = false)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;
            var useDark = _isLight ? 0 : 1;
            // Windows 10 1903+ = 속성 20, 1809 등 구버전 = 속성 19 (실패 시 폴백)
            if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref useDark, sizeof(int)) != 0)
                _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeOld, ref useDark, sizeof(int));

            // 라이브 전환 시 DWM이 캡션 글자색을 갱신하지 않는 버그 —
            // Windows 11 전용 캡션 배경/글자색 지정(테마 토큰과 일치). Win10에선 조용히 무시됨.
            var caption = _isLight ? LightCaption : DarkCaption;
            var text = _isLight ? LightText : DarkText;
            _ = DwmSetWindowAttribute(handle, DwmCaptionColor, ref caption, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmTextColor, ref text, sizeof(int));

            if (restoreCaption && IsWindows11)
            {
                var options = new WtaOptions { Flags = 0, Mask = WtncaNoDrawCaption | WtncaNoDrawIcon };
                _ = SetWindowThemeAttribute(handle, WtaNonClient, ref options, Marshal.SizeOf<WtaOptions>());
            }

            _ = SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SwpFrameRefresh);
        }
        catch (Exception)
        {
            // 구버전 Windows 등 미지원 — 무시
        }
    }

    /// <summary>설정값에 따라 앱 전체 테마를 즉시 적용한다. UI 스레드에서 호출한다.</summary>
    public static void Apply(AppTheme theme)
    {
        var app = Application.Current;
        if (app is null)
            return;

        var useLight = theme switch
        {
            AppTheme.Light => true,
            AppTheme.Dark => false,
            _ => IsSystemLightTheme(),
        };
        _isLight = useLight;

        // ① WPF-UI 기본 컨트롤 테마 (ApplicationBackgroundBrush·TextFillColor* 등)
        //    ⚠ 기본 오버로드는 Mica 백드롭을 창에 적용해 시스템 캡션 텍스트가 사라진다 — 백드롭 없이 적용
        ApplicationThemeManager.Apply(
            useLight ? ApplicationTheme.Light : ApplicationTheme.Dark,
            Wpf.Ui.Controls.WindowBackdropType.None,
            updateAccent: false);

        // ② 와이어프레임 팔레트 사전 교체 (Wf* 브러시)
        var target = new Uri($"pack://application:,,,/Themes/Wireframe{(useLight ? "Light" : "Dark")}.xaml");
        var dictionaries = app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d =>
            d.Source is not null && d.Source.OriginalString.Contains("/Themes/Wireframe", StringComparison.OrdinalIgnoreCase));
        if (existing?.Source != target)
        {
            if (existing is not null)
                dictionaries.Remove(existing);
            dictionaries.Add(new ResourceDictionary { Source = target });
        }

        // WPF-UI Apply가 열린 창의 Background에 로컬 값을 써서 XAML DynamicResource를 덮는다 —
        // 와이어프레임 배경 참조를 다시 걸어준다. (스플래시 등 투명 창은 제외)
        foreach (Window window in app.Windows)
        {
            if (window.AllowsTransparency)
                continue;
            window.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "WfSurfaceBrush");
            // 라이브 Apply 경로 — WPF-UI가 캡션 그리기를 꺼버리므로 복원 필요(Win11 한정)
            ApplyTitleBar(window, restoreCaption: true);
        }
    }

    /// <summary>설정값 기준 실효 테마가 라이트인지 — 레일 토글(FR-U2.4)의 다음 대상 판정용.</summary>
    internal static bool IsEffectiveLight(AppTheme theme) => theme switch
    {
        AppTheme.Light => true,
        AppTheme.Dark => false,
        _ => IsSystemLightTheme(),
    };

    /// <summary>Windows 앱 테마가 라이트인지(레지스트리 AppsUseLightTheme). 판독 실패 시 다크.</summary>
    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

using Avalonia.Controls;
using Avalonia.Platform;

namespace Multiplatform_Downloader.Avalonia.Services;

/// <summary>
/// 메뉴바 상주 아이콘(FR-09) — WPF TrayIconService의 macOS 대응(Avalonia TrayIcon + NativeMenu).
/// macOS 메뉴바 아이콘은 더블클릭 개념이 없어 '열기'는 메뉴 항목으로만 제공한다.
/// </summary>
public sealed class TrayService : IDisposable
{
    private TrayIcon? _icon;
    private bool _disposed;

    public event EventHandler? OpenRequested;
    public event EventHandler? PauseResumeAllRequested;
    public event EventHandler? OpenFolderRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public string? LastError { get; private set; }

    public bool Initialize()
    {
        if (_disposed)
            return false;
        try
        {
            _icon?.Dispose();

            var icon = new TrayIcon { ToolTipText = "샤샤룽 다운로더" };
            try
            {
                using var stream = AssetLoader.Open(new Uri("avares://Multiplatform-Downloader.Avalonia/Assets/app.png"));
                icon.Icon = new WindowIcon(stream);
            }
            catch { /* 아이콘 로드 실패 시 기본 표시 */ }

            var menu = new NativeMenu();
            menu.Add(BuildItem("열기", () => OpenRequested?.Invoke(this, EventArgs.Empty)));
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(BuildItem("모두 일시정지 / 재개", () => PauseResumeAllRequested?.Invoke(this, EventArgs.Empty)));
            menu.Add(BuildItem("다운로드 폴더 열기", () => OpenFolderRequested?.Invoke(this, EventArgs.Empty)));
            menu.Add(BuildItem("설정", () => SettingsRequested?.Invoke(this, EventArgs.Empty)));
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(BuildItem("완전 종료", () => ExitRequested?.Invoke(this, EventArgs.Empty)));
            icon.Menu = menu;

            icon.Clicked += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
            icon.IsVisible = true;
            _icon = icon;
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    /// <summary>아이콘 부재 시 재생성 — WPF EnsureCreated와 동일 계약(창 닫기→숨김 가드).</summary>
    public bool EnsureCreated()
    {
        if (_disposed)
            return false;
        return _icon is not null || Initialize();
    }

    public void UpdateTooltip(string text)
    {
        if (_icon is not null)
            _icon.ToolTipText = text;
    }

    private static NativeMenuItem BuildItem(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    public void Dispose()
    {
        _disposed = true;
        try { _icon?.Dispose(); } catch { /* 무시 */ }
        _icon = null;
    }
}

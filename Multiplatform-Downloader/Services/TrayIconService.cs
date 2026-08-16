using H.NotifyIcon;
using System;
using System.Windows.Controls;

namespace Multiplatform_Downloader.Services;

/// <summary>트레이 상주 아이콘·메뉴(FR-09, WF-04). 더블클릭=열기, 메뉴로 정지/재개·폴더·완전 종료.</summary>
public sealed class TrayIconService : IDisposable
{
    private TaskbarIcon? _icon;

    public event EventHandler? OpenRequested;
    public event EventHandler? PauseResumeAllRequested;
    public event EventHandler? OpenFolderRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public void Initialize()
    {
        _icon = new TaskbarIcon { ToolTipText = "샤샤룽 다운로더" };
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                _icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
        }
        catch
        {
            // 아이콘 로드 실패 시 기본
        }
        _icon.TrayMouseDoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        var menu = new ContextMenu();
        menu.Items.Add(BuildItem("열기", () => OpenRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Separator());
        menu.Items.Add(BuildItem("모두 일시정지 / 재개", () => PauseResumeAllRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(BuildItem("다운로드 폴더 열기", () => OpenFolderRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(BuildItem("설정", () => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Separator());
        menu.Items.Add(BuildItem("완전 종료", () => ExitRequested?.Invoke(this, EventArgs.Empty)));
        _icon.ContextMenu = menu;

        _icon.ForceCreate();
    }

    public void UpdateTooltip(string text)
    {
        if (_icon is not null)
            _icon.ToolTipText = text;
    }

    public void ShowNotification(string title, string message)
    {
        try
        {
            _icon?.ShowNotification(title, message);
        }
        catch
        {
            // 알림 실패는 무시
        }
    }

    private static MenuItem BuildItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    public void Dispose() => _icon?.Dispose();
}

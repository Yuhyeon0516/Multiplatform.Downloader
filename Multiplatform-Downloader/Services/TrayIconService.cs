using H.NotifyIcon;
using System;
using System.Windows;
using System.Windows.Controls;

namespace Multiplatform_Downloader.Services;

/// <summary>트레이 상주 아이콘·메뉴(FR-09, WF-04). 더블클릭=열기, 메뉴로 정지/재개·폴더·설정·완전 종료.
/// H.NotifyIcon은 탐색기(Explorer) 재시작(TaskbarCreated) 시 아이콘을 자동 재등록한다.
/// 아이콘이 조용히 사라지지 않도록 생성 실패를 방어하고 재생성(EnsureCreated)을 지원한다.</summary>
public sealed class TrayIconService : IDisposable
{
    private TaskbarIcon? _icon;
    private bool _disposed;

    public event EventHandler? OpenRequested;
    public event EventHandler? PauseResumeAllRequested;
    public event EventHandler? OpenFolderRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    /// <summary>트레이 아이콘을 생성한다. 성공(아이콘 실존) 시 true. 실패는 던지지 않고 false 반환·<see cref="LastError"/> 기록.</summary>
    public bool Initialize()
    {
        if (_disposed)
            return false;
        try
        {
            // 재진입 시 기존 아이콘 정리(중복 아이콘 방지)
            try { _icon?.Dispose(); } catch { /* 무시 */ }

            var icon = new TaskbarIcon { ToolTipText = "샤샤룽 다운로더" };
            TrySetIcon(icon);

            icon.TrayMouseDoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

            var menu = new ContextMenu();
            menu.Items.Add(BuildItem("열기", () => OpenRequested?.Invoke(this, EventArgs.Empty)));
            menu.Items.Add(new Separator());
            menu.Items.Add(BuildItem("모두 일시정지 / 재개", () => PauseResumeAllRequested?.Invoke(this, EventArgs.Empty)));
            menu.Items.Add(BuildItem("다운로드 폴더 열기", () => OpenFolderRequested?.Invoke(this, EventArgs.Empty)));
            menu.Items.Add(BuildItem("설정", () => SettingsRequested?.Invoke(this, EventArgs.Empty)));
            menu.Items.Add(new Separator());
            menu.Items.Add(BuildItem("완전 종료", () => ExitRequested?.Invoke(this, EventArgs.Empty)));
            icon.ContextMenu = menu;

            icon.ForceCreate();
            _icon = icon;
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message; // 호출부가 IAppLogger로 기록
            return false;
        }
    }

    /// <summary>마지막 트레이 생성 실패 사유(성공 시 null). 호출부가 로그로 남긴다.</summary>
    public string? LastError { get; private set; }

    /// <summary>아이콘이 없으면(생성 실패·유실) 다시 생성한다. 아이콘이 존재/재생성되면 true. 트레이가 사라지지 않도록 방어적 재생성.</summary>
    public bool EnsureCreated()
    {
        if (_disposed)
            return false;
        if (_icon is null)
            return Initialize();
        try { _icon.ForceCreate(); LastError = null; return true; }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    private static void TrySetIcon(TaskbarIcon icon)
    {
        // 1) 앱 내장 리소스(Assets/app.ico) 우선 — 가장 안정적
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            var res = Application.GetResourceStream(uri);
            if (res?.Stream is { } stream)
            {
                using (stream)
                    icon.Icon = new System.Drawing.Icon(stream);
                return;
            }
        }
        catch { /* 리소스 로드 실패 시 폴백 */ }

        // 2) 실행 파일의 연결 아이콘 폴백
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
        }
        catch { /* 아이콘 없이 진행(H.NotifyIcon 기본 표시) */ }
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

    public void Dispose()
    {
        _disposed = true;
        try { _icon?.Dispose(); } catch { /* 무시 */ }
        _icon = null;
    }
}

using System.Diagnostics;

namespace Multiplatform_Downloader.Avalonia.Services;

/// <summary>
/// macOS 알림 센터 알림 — osascript display notification. 서명/번들 없이도 동작하는 최소 경로.
/// (UNUserNotificationCenter는 번들 앱 + 권한 요청이 필요해 .app 패키징 후에만 유효)
/// </summary>
public sealed class MacNotificationService
{
    public void Show(string title, string message)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/osascript")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add($"display notification \"{Escape(message)}\" with title \"{Escape(title)}\"");
            Process.Start(psi);
        }
        catch
        {
            // 알림 실패는 무시 (WPF 트레이 풍선과 동일 정책)
        }
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

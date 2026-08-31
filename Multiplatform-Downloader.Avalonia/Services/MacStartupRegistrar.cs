using System.Diagnostics;
using System.Text;

namespace Multiplatform_Downloader.Avalonia.Services;

/// <summary>
/// 로그인 시 자동 실행(FR-09) — WPF StartupRegistrar(HKCU Run 키)의 macOS 대응.
/// ~/Library/LaunchAgents 에 LaunchAgent plist를 쓰거나 지운다. 무서명 배포에서도 동작한다.
/// </summary>
public sealed class MacStartupRegistrar
{
    private const string Label = "com.shyshyroong.downloader";

    private static string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", Label + ".plist");

    public bool IsEnabled() => File.Exists(PlistPath);

    public void SetEnabled(bool enabled, bool startMinimized)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(PlistPath))
                    File.Delete(PlistPath);
                return;
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return;

            // .app 번들 안이면 open -a 로 번들을 실행(재배치·업데이트에 강함), 아니면 실행 파일 직접
            var bundle = FindBundlePath(exe);
            var sb = new StringBuilder();
            sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
            sb.AppendLine("""<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">""");
            sb.AppendLine("""<plist version="1.0"><dict>""");
            sb.AppendLine($"  <key>Label</key><string>{Label}</string>");
            sb.AppendLine("  <key>ProgramArguments</key><array>");
            if (bundle is not null)
            {
                sb.AppendLine("    <string>/usr/bin/open</string>");
                sb.AppendLine("    <string>-a</string>");
                sb.AppendLine($"    <string>{Escape(bundle)}</string>");
                if (startMinimized)
                {
                    sb.AppendLine("    <string>--args</string>");
                    sb.AppendLine("    <string>--minimized</string>");
                }
            }
            else
            {
                sb.AppendLine($"    <string>{Escape(exe)}</string>");
                if (startMinimized)
                    sb.AppendLine("    <string>--minimized</string>");
            }
            sb.AppendLine("  </array>");
            sb.AppendLine("  <key>RunAtLoad</key><true/>");
            sb.AppendLine("</dict></plist>");

            Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
            File.WriteAllText(PlistPath, sb.ToString());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MacStartupRegistrar] {ex.Message}");
        }
    }

    /// <summary>실행 파일 경로에서 상위 .app 번들 루트를 찾는다(없으면 null — dotnet run 등).</summary>
    private static string? FindBundlePath(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath);
        while (!string.IsNullOrEmpty(dir))
        {
            if (dir.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

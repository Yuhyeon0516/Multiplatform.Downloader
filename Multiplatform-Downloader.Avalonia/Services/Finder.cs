using System.Diagnostics;

namespace Multiplatform_Downloader.Avalonia.Services;

/// <summary>Finder/기본 앱 열기 헬퍼 — WPF의 explorer.exe 호출 대응.</summary>
public static class Finder
{
    /// <summary>Finder에서 파일 선택 표시(explorer /select 대응).</summary>
    public static void Reveal(string filePath) => Run("open", "-R", filePath);

    /// <summary>폴더를 Finder로 연다.</summary>
    public static void OpenFolder(string folder) => Run("open", folder);

    /// <summary>파일을 연결된 기본 앱으로 연다.</summary>
    public static void OpenWithDefaultApp(string path) => Run("open", path);

    private static void Run(string cmd, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(cmd) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            Process.Start(psi);
        }
        catch
        {
            // 열기 실패는 무시 (WPF와 동일 정책)
        }
    }
}

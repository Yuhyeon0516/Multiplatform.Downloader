using Avalonia;

namespace Multiplatform_Downloader.Avalonia;

internal static class Program
{
    // Avalonia 초기화 전에는 어떤 Avalonia API도 건드리지 않는다(공식 가이드).
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args, global::Avalonia.Controls.ShutdownMode.OnExplicitShutdown);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

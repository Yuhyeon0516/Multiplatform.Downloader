using Caliburn.Micro;
using System.Diagnostics;
using System.Reflection;

namespace Multiplatform_Downloader.ViewModels;

/// <summary>프로그램 정보(About) 창. 이름·버전·개발자·번들 엔진·사용 범위를 표시한다.</summary>
internal sealed class AboutViewModel : Screen
{
    public AboutViewModel()
    {
        DisplayName = "정보";
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        Version = v is not null ? $"v{v.ToString(3)}" : "v?";
    }

    public string ProductName { get; } = "샤샤룽 다운로더";
    public string ProductNameEn { get; } = "Shyshyroong Downloader";
    public string Version { get; }
    public string SubTitle { get; } = "YouTube · Instagram · TikTok · 샤오홍슈 영상 다운로더";
    public string Developer { get; } = "라이프백패커 (Lifebackpacker)";

    /// <summary>번들 서드파티 엔진과 라이선스.</summary>
    public string EngineInfo { get; } =
        "yt-dlp (Unlicense) · FFmpeg / ffprobe (LGPL·GPL) · Deno (MIT)";

    public string UsageNotice { get; } =
        "개인적·합법적 용도로만 사용하세요. 저작권 및 각 플랫폼의 이용약관을 준수할 책임은 사용자에게 있습니다.";

    public void OpenLegalNotice()
    {
        try
        {
            // 저장소/설치 폴더의 라이선스 고지 문서를 연다(있을 때만).
            var docs = System.IO.Path.Combine(System.AppContext.BaseDirectory, "docs", "legal-notice.md");
            if (System.IO.File.Exists(docs))
                Process.Start(new ProcessStartInfo(docs) { UseShellExecute = true });
        }
        catch
        {
            // 열기 실패는 무시
        }
    }

    public async System.Threading.Tasks.Task Close() => await TryCloseAsync(true);
}

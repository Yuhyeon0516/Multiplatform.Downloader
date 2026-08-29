using Multiplatform_Downloader.Core.Settings;

namespace Multiplatform_Downloader.Tests.Update;

/// <summary>NFR-U3 (Blocker) — 신규 설정 3필드가 Normalized() 복사 누락으로 조용히 리셋되지 않는지 검증.</summary>
public class UpdateSettingsRoundTripTests
{
    [Fact]
    public void should_preserve_auto_update_check_after_normalize()
    {
        var s = new AppSettings { AutoUpdateCheck = false };
        Assert.False(s.Normalized().AutoUpdateCheck); // 기본값 true로 리셋되면 실패
    }

    [Fact]
    public void should_preserve_skipped_version_after_normalize()
    {
        var s = new AppSettings { SkippedVersion = "2.12.8" };
        Assert.Equal("2.12.8", s.Normalized().SkippedVersion);
    }

    [Fact]
    public async Task should_preserve_update_fields_after_save_and_reload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mpdl-set-{Guid.NewGuid():N}.json");
        try
        {
            var svc = new JsonSettingsService(path);
            await svc.LoadAsync();
            svc.Current.AutoUpdateCheck = false;
            svc.Current.SkippedVersion = "2.13.5";
            await svc.SaveAsync();

            var reload = new JsonSettingsService(path);
            await reload.LoadAsync();
            Assert.False(reload.Current.AutoUpdateCheck);
            Assert.Equal("2.13.5", reload.Current.SkippedVersion);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task should_default_auto_update_on_when_field_absent_in_old_settings()
    {
        // 하위호환 — 구 settings.json에 AutoUpdateCheck 키가 없어도 기본 true
        var path = Path.Combine(Path.GetTempPath(), $"mpdl-old-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """{ "DownloadFolder": "C:\\D", "MaxConcurrent": 3 }""");
        try
        {
            var svc = new JsonSettingsService(path);
            await svc.LoadAsync();
            Assert.True(svc.Current.AutoUpdateCheck);
            Assert.Null(svc.Current.SkippedVersion);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

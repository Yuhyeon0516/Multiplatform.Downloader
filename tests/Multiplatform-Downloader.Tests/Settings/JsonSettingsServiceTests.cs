using System.IO;
using Multiplatform_Downloader.Core.Settings;

namespace Multiplatform_Downloader.Tests.Settings;

public class JsonSettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public JsonSettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mpdl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 정리 실패 무시 */ }
    }

    [Fact]
    public async Task should_persist_and_reload_when_saved()
    {
        var writer = new JsonSettingsService(_filePath);
        await writer.LoadAsync();
        writer.Current.DownloadFolder = @"D:\Videos";
        writer.Current.MaxConcurrent = 5;
        writer.Current.DefaultQuality = QualityPreference.Worst;
        await writer.SaveAsync();

        var reader = new JsonSettingsService(_filePath);
        await reader.LoadAsync();

        Assert.Equal(@"D:\Videos", reader.Current.DownloadFolder);
        Assert.Equal(5, reader.Current.MaxConcurrent);
        Assert.Equal(QualityPreference.Worst, reader.Current.DefaultQuality);
    }

    [Fact]
    public async Task should_return_defaults_when_file_missing()
    {
        var sut = new JsonSettingsService(_filePath);
        await sut.LoadAsync();

        Assert.Equal(3, sut.Current.MaxConcurrent);
        Assert.Equal(15, sut.Current.MaxQueueItems);
        Assert.True(sut.Current.LaunchOnStartup);
    }

    [Fact]
    public async Task should_recover_defaults_when_file_corrupt()
    {
        await File.WriteAllTextAsync(_filePath, "{ this is not valid json ]");
        var sut = new JsonSettingsService(_filePath);

        await sut.LoadAsync();

        Assert.Equal(3, sut.Current.MaxConcurrent);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 8)]
    [InlineData(5, 5)]
    public async Task should_clamp_max_concurrent_when_out_of_range(int input, int expected)
    {
        var writer = new JsonSettingsService(_filePath);
        await writer.LoadAsync();
        writer.Current.MaxConcurrent = input;
        await writer.SaveAsync();

        var reader = new JsonSettingsService(_filePath);
        await reader.LoadAsync();

        Assert.Equal(expected, reader.Current.MaxConcurrent);
    }

    [Fact]
    public async Task should_not_leave_temp_file_after_save()
    {
        var sut = new JsonSettingsService(_filePath);
        await sut.LoadAsync();
        await sut.SaveAsync();

        Assert.False(File.Exists(_filePath + ".tmp"));
        Assert.True(File.Exists(_filePath));
    }

    [Fact]
    public async Task should_default_download_folder_when_blank()
    {
        var writer = new JsonSettingsService(_filePath);
        await writer.LoadAsync();
        writer.Current.DownloadFolder = "   ";
        await writer.SaveAsync();

        var reader = new JsonSettingsService(_filePath);
        await reader.LoadAsync();

        Assert.False(string.IsNullOrWhiteSpace(reader.Current.DownloadFolder));
    }
}

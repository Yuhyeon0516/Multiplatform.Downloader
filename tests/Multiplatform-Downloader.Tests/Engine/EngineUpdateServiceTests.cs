using System.IO;
using System.Security.Cryptography;
using Multiplatform_Downloader.Core.Engine;

namespace Multiplatform_Downloader.Tests.Engine;

public class EngineUpdateServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _targetPath;

    public EngineUpdateServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mpdl-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _targetPath = Path.Combine(_tempDir, "yt-dlp.exe");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 무시 */ }
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [Fact]
    public async Task should_replace_binary_when_checksum_valid()
    {
        var newBytes = "new-binary"u8.ToArray();
        var service = new EngineUpdateService((_, _) => Task.FromResult(newBytes));
        var request = new EngineUpdateRequest("https://host/yt-dlp.exe", Sha256Hex(newBytes), _targetPath);

        var result = await service.UpdateAsync(request);

        Assert.True(result.Success);
        Assert.Equal(newBytes, await File.ReadAllBytesAsync(_targetPath));
    }

    [Fact]
    public async Task should_reject_and_keep_original_when_checksum_mismatch()
    {
        await File.WriteAllBytesAsync(_targetPath, "original"u8.ToArray());
        var service = new EngineUpdateService((_, _) => Task.FromResult("tampered"u8.ToArray()));
        var request = new EngineUpdateRequest("https://host/yt-dlp.exe", "0000deadbeef", _targetPath);

        var result = await service.UpdateAsync(request);

        Assert.False(result.Success);
        Assert.Equal("original"u8.ToArray(), await File.ReadAllBytesAsync(_targetPath)); // 원본 유지
    }

    [Fact]
    public async Task should_return_failed_when_fetch_throws() // M2 회귀
    {
        await File.WriteAllBytesAsync(_targetPath, "original"u8.ToArray());
        var service = new EngineUpdateService((_, _) => throw new InvalidOperationException("network down"));
        var request = new EngineUpdateRequest("https://host/x", "abc", _targetPath);

        var result = await service.UpdateAsync(request);

        Assert.False(result.Success);
        Assert.Equal("original"u8.ToArray(), await File.ReadAllBytesAsync(_targetPath)); // 원본 보존
        Assert.False(File.Exists(_targetPath + ".new"));
    }

    [Fact]
    public async Task should_not_leave_temp_or_backup_after_success()
    {
        await File.WriteAllBytesAsync(_targetPath, "old"u8.ToArray());
        var newBytes = "updated"u8.ToArray();
        var service = new EngineUpdateService((_, _) => Task.FromResult(newBytes));
        var request = new EngineUpdateRequest("https://host/x", Sha256Hex(newBytes), _targetPath);

        await service.UpdateAsync(request);

        Assert.False(File.Exists(_targetPath + ".new"));
        Assert.False(File.Exists(_targetPath + ".bak"));
    }
}

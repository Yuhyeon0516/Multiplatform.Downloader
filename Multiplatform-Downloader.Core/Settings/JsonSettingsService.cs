using System.Text.Json;
using System.Text.Json.Serialization;

namespace Multiplatform_Downloader.Core.Settings;

/// <summary>
/// 설정을 <c>%APPDATA%\Multiplatform-Downloader\settings.json</c>에 저장한다(FR-06).
/// 저장은 temp 파일에 쓴 뒤 교체하는 원자적 방식이며, 파일이 손상됐으면 기본값으로 복구한다.
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public JsonSettingsService(string? filePath = null)
    {
        _filePath = filePath ?? DefaultFilePath;
    }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Multiplatform-Downloader",
        "settings.json");

    public AppSettings Current { get; private set; } = new();

    public bool SettingsFileExisted { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        SettingsFileExisted = File.Exists(_filePath);
        if (!SettingsFileExisted)
        {
            Current = new AppSettings();
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer
                .DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            Current = (loaded ?? new AppSettings()).Normalized();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 손상·접근 불가 → 기본값 복구
            Current = new AppSettings();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Current = Current.Normalized();

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, Current, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        // 원자적 교체 — 쓰기 도중 크래시가 나도 기존 파일이 온전하게 남는다
        File.Move(tempPath, _filePath, overwrite: true);
    }
}

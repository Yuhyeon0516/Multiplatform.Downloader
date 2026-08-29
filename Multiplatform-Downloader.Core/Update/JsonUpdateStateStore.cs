using System.Text.Json;

namespace Multiplatform_Downloader.Core.Update;

/// <summary>
/// 업데이트 상태를 <c>%APPDATA%\Multiplatform-Downloader\update-state.json</c>에 저장한다(NFR-U4).
/// JsonSettingsService와 동일한 원자적 교체(temp→Move)·손상 복구 패턴을 따른다.
/// </summary>
public sealed class JsonUpdateStateStore : IUpdateStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _filePath;

    public JsonUpdateStateStore(string? filePath = null)
    {
        _filePath = filePath ?? DefaultFilePath;
    }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Multiplatform-Downloader",
        "update-state.json");

    public UpdateState Load()
    {
        if (!File.Exists(_filePath))
            return new UpdateState();
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<UpdateState>(json, JsonOptions) ?? new UpdateState();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new UpdateState();
        }
    }

    public void Save(UpdateState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 상태 저장 실패는 기능 저하가 아님(다음 체크에서 재시도) — 무시
        }
    }
}

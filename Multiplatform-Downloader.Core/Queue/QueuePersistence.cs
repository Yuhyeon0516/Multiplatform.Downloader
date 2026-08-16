using System.Text.Json;
using System.Text.Json.Serialization;

namespace Multiplatform_Downloader.Core.Queue;

/// <summary>
/// 미완료 큐 항목을 <c>queue-state.json</c>에 저장하고 복원한다(FR-11).
/// 원자적 저장, 손상·스키마 불일치 시 빈 목록 반환(NFR-16).
/// </summary>
public sealed class QueuePersistence
{
    // v2: 완료 항목 + OutputFilePath 저장(받음/안받음 구분 복원). v1 파일도 읽는다(경로만 없음).
    public const int CurrentSchemaVersion = 2;

    /// <summary>완료 항목 보존 상한 — 목록·파일 무한 증가 방지(오래된 완료부터 제외).</summary>
    public const int MaxCompletedSnapshots = 300;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public QueuePersistence(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <summary>전체 항목을 저장한다. 완료 항목은 최근 <see cref="MaxCompletedSnapshots"/>개까지 보존
    /// (재시작 후 받음/안받음 구분 — 사용자 요청).</summary>
    public async Task SaveAsync(IEnumerable<DownloadItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var all = items.ToList();
        // 완료 초과분은 목록 앞(오래된 등록)부터 제외한다
        var completedToSkip = Math.Max(0, all.Count(i => i.Status == DownloadStatus.Completed) - MaxCompletedSnapshots);
        var snapshots = new List<QueueItemSnapshot>(all.Count);
        foreach (var item in all)
        {
            if (item.Status == DownloadStatus.Completed && completedToSkip > 0)
            {
                completedToSkip--;
                continue;
            }
            snapshots.Add(ToSnapshot(item));
        }
        var snapshot = new QueueSnapshot(CurrentSchemaVersion, snapshots);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        File.Move(tempPath, _filePath, overwrite: true);
    }

    /// <summary>복원할 항목 스냅샷을 읽는다. 파일이 없거나 손상·스키마 불일치면 빈 목록.</summary>
    public async Task<IReadOnlyList<QueueItemSnapshot>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
            return [];

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var snapshot = await JsonSerializer
                .DeserializeAsync<QueueSnapshot>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            // v1(완료 미저장·경로 없음)도 그대로 읽는다 — OutputFilePath만 null이 된다
            if (snapshot is null || snapshot.SchemaVersion is not (1 or 2))
                return [];

            return snapshot.Items ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static QueueItemSnapshot ToSnapshot(DownloadItem item) => new(
        item.Id,
        item.OriginalUrl,
        item.Platform,
        item.ResolvedUrl,
        item.Title,
        item.ThumbnailPath,
        item.SelectedFormatId,
        item.Status,
        item.ExtractionRoute,
        item.OutputFilePath);
}

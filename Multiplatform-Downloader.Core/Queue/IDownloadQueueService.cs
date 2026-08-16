namespace Multiplatform_Downloader.Core.Queue;

/// <summary>Enqueue 결과. <see cref="SkippedOverCapacity"/>는 큐 용량(15) 초과로 제외된 수.</summary>
public sealed record EnqueueResult(
    IReadOnlyList<DownloadItem> Added,
    IReadOnlyList<string> Rejected,
    int SkippedOverCapacity);

/// <summary>다운로드 큐를 관리한다(FR-05). 이벤트는 lock 밖에서 발화된다(규칙 I-05).</summary>
public interface IDownloadQueueService
{
    IReadOnlyList<DownloadItem> Items { get; }
    event EventHandler<DownloadItem>? ItemChanged;

    /// <summary>URL을 큐에 등록하고 메타데이터 분석만 시작한다. 다운로드는 <see cref="Start"/> 호출 전까지 시작되지 않는다.</summary>
    EnqueueResult Enqueue(string urlsText);

    /// <summary>분석이 끝나 <c>Ready</c> 상태인 항목의 다운로드를 시작한다(FR-05 수동 시작).</summary>
    void Start(Guid id);

    /// <summary><c>Ready</c> 상태인 모든 항목의 다운로드를 시작한다.</summary>
    void StartAll();

    /// <summary>
    /// 다운로드 시작 전(Ready) 또는 실패/취소 항목의 선택 포맷(해상도/품질)을 바꾼다(FR-03).
    /// 항목이 없거나 진행 중이거나 <paramref name="formatId"/>가 항목의 포맷 목록에 없으면 무시한다.
    /// </summary>
    void ChangeFormat(Guid id, string formatId);

    void Cancel(Guid id);
    void Pause(Guid id);
    void Resume(Guid id);
    void Remove(Guid id);
    void Retry(Guid id);
    void PauseAll();
    void ResumeAll();

    /// <summary>기동 시 다운로드 폴더의 고아 조각 파일(이전 실패·크래시 잔여)을 정리한다.</summary>
    void SweepOrphanPartials();

    /// <summary>이전 세션의 완료 항목을 재분석 없이 복원한다(FR-D3.5 확장). 저장된 최종 파일이
    /// 폴더에 있으면 완료 카드로, 없으면(삭제/이동) 안내와 [재시도]가 있는 불가 상태로 복원한다.</summary>
    void RestoreCompleted(QueueItemSnapshot snapshot);
}

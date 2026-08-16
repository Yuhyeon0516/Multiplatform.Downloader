namespace Multiplatform_Downloader.Core.Queue;

/// <summary>항목이 어느 추출 경로로 처리됐는지(FR-13). UI에 표시한다.</summary>
public enum ExtractionRoute
{
    Unknown,
    YtDlp,
    XhsFallback,
}

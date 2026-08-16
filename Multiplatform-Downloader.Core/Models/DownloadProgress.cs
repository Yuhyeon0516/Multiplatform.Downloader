namespace Multiplatform_Downloader.Core.Models;

/// <summary>다운로드 진행 상태(FR-04). <see cref="IsIndeterminate"/>가 true면 퍼센트를 알 수 없는 단계(병합 등).</summary>
public sealed record DownloadProgress(double Percent, long? SpeedBytesPerSec, TimeSpan? Eta, bool IsIndeterminate = false);

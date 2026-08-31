namespace Multiplatform_Downloader.Core.Update;

/// <summary>
/// GitHub 최신 릴리스에서 추출한 업데이트 정보(FR-U2.2). 원격(신뢰 불가) 데이터이므로
/// 자산명·URL·릴리스노트는 사용 지점에서 검증·정규화한 뒤 쓴다.
/// </summary>
public sealed record UpdateInfo(
    string TagName,
    Version Version,
    string ReleaseNotes,
    string AssetName,
    string DownloadUrl,
    long AssetSize,
    string? ChecksumUrl = null); // macOS: 같은 릴리스의 "<AssetName>.sha256" 자산 URL(무결성 검증용)

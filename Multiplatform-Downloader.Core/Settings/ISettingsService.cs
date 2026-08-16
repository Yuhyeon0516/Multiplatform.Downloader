namespace Multiplatform_Downloader.Core.Settings;

/// <summary>설정을 로드/저장한다(FR-06). 저장은 원자적, 손상 시 기본값 복구.</summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    /// <summary>가장 최근 <see cref="LoadAsync"/> 시점에 설정 파일이 이미 존재했는지 —
    /// false면 첫 실행(인스톨러 직후). 시작 등록을 인스톨러 선택과 화해시키는 데 쓴다(FR-09).</summary>
    bool SettingsFileExisted { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
}

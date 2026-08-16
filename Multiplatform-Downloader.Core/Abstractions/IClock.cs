namespace Multiplatform_Downloader.Core.Abstractions;

/// <summary>
/// 시간 의존 코드를 테스트 가능하게 하는 추상화 (프로젝트 규칙 I-02).
/// 서비스에서 <see cref="System.DateTime.Now"/>를 직접 호출하지 않는다.
/// </summary>
public interface IClock
{
    DateTime Now { get; }
    DateTime UtcNow { get; }
}

/// <summary>프로덕션 구현 — 시스템 시계.</summary>
public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
    public DateTime UtcNow => DateTime.UtcNow;
}

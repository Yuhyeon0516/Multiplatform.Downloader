namespace Multiplatform_Downloader.Core.Engine;

/// <summary>
/// 병합 포맷(예: <c>137+140</c>)의 다중 스트림 진행률을 단일 0~100% 축으로 매핑한다.
/// <para>실측 근거: yt-dlp는 영상 스트림 0→100%, 이어서 오디오 스트림 0→100%를 순차 출력한다.
/// 원시 퍼센트를 그대로 바인딩하면 진행바가 두 번 왕복해 "갑자기 100%" UX가 된다.</para>
/// <para>매핑: 영상 0~85% · 오디오 85~99% · 이후(병합 등) 99% 유지 — 완료 시 큐가 100%로 확정한다.
/// 스트림 전환은 퍼센트 급락(50pt 초과 하락)으로 감지하고, 출력은 단조 증가를 보장한다.</para>
/// <para>스레드 안전하지 않음 — 다운로드 1건당 인스턴스 1개를 생성해 출력 콜백 스레드에서만 사용한다.</para>
/// </summary>
public sealed class MultiStreamProgressMapper
{
    private const double RestartDropThreshold = 50;
    private const double VideoPhaseWeight = 0.85;
    private const double AudioPhaseWeight = 0.14;

    private readonly int _expectedStreams;
    private int _streamIndex;
    private double _lastRaw;
    private double _lastMapped;

    public MultiStreamProgressMapper(string formatId)
    {
        // '+' 포함 포맷은 영상+오디오 2개 스트림을 순차 다운로드한다
        _expectedStreams = formatId is not null && formatId.Contains('+') ? 2 : 1;
    }

    /// <summary>원시 진행률(스트림별 0~100)을 전체 진행률로 변환한다.</summary>
    public double Map(double rawPercent)
    {
        if (rawPercent < _lastRaw - RestartDropThreshold)
            _streamIndex++;
        _lastRaw = rawPercent;

        var mapped = _expectedStreams <= 1
            ? rawPercent
            : _streamIndex switch
            {
                0 => rawPercent * VideoPhaseWeight,
                1 => 100 * VideoPhaseWeight + rawPercent * AudioPhaseWeight,
                _ => 99,
            };

        if (mapped < _lastMapped)
            mapped = _lastMapped;
        _lastMapped = mapped;
        return mapped;
    }
}

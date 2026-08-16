using Multiplatform_Downloader.Core.Engine;

namespace Multiplatform_Downloader.Tests.Engine;

/// <summary>
/// 병합 포맷 진행률 매핑 검증. 실측 근거: yt-dlp는 영상 0→100% 뒤 오디오 0→100%를
/// 순차 출력한다(2026-08-02, 244+bestaudio 실행 106라인 채집).
/// </summary>
public class MultiStreamProgressMapperTests
{
    [Fact]
    public void should_pass_through_when_single_stream_format()
    {
        var mapper = new MultiStreamProgressMapper("best");

        Assert.Equal(0, mapper.Map(0));
        Assert.Equal(42.3, mapper.Map(42.3));
        Assert.Equal(100, mapper.Map(100));
    }

    [Fact]
    public void should_map_video_phase_to_lower_band_when_merged_format()
    {
        var mapper = new MultiStreamProgressMapper("137+140");

        Assert.Equal(0, mapper.Map(0));
        Assert.Equal(50 * 0.85, mapper.Map(50), 3);
        Assert.Equal(85, mapper.Map(100), 3);
    }

    [Fact]
    public void should_continue_rising_when_audio_stream_restarts_at_zero()
    {
        // 실측 시퀀스: 영상 …→100% 직후 오디오가 0%부터 다시 시작 — 바가 왕복하면 안 된다
        var mapper = new MultiStreamProgressMapper("137+140");
        mapper.Map(100); // 영상 완료 → 85

        var afterRestart = mapper.Map(0);
        Assert.Equal(85, afterRestart, 3); // 하락 없이 유지

        Assert.Equal(85 + 50 * 0.14, mapper.Map(50), 3);
        Assert.Equal(99, mapper.Map(100), 3); // 완료 100%는 큐가 확정
    }

    [Fact]
    public void should_never_decrease_when_raw_percent_jitters()
    {
        var mapper = new MultiStreamProgressMapper("137+140");
        mapper.Map(60);

        // 50pt 이하의 하락은 스트림 전환이 아님 — 단조 증가 유지
        Assert.Equal(60 * 0.85, mapper.Map(20), 3);
    }

    [Fact]
    public void should_hold_at_99_when_third_stream_appears()
    {
        var mapper = new MultiStreamProgressMapper("a+b+c");
        mapper.Map(100); // 스트림1
        mapper.Map(100); // 여전히 스트림1 (하락 없음)
        mapper.Map(0);   // 스트림2 전환
        mapper.Map(100);
        mapper.Map(0);   // 스트림3 전환

        Assert.Equal(99, mapper.Map(70), 3);
    }

    [Fact]
    public void should_pass_through_resumed_download_when_single_stream()
    {
        // -c 이어받기: 중간 퍼센트부터 시작 — 하락이 없으므로 그대로 통과
        var mapper = new MultiStreamProgressMapper("best");
        Assert.Equal(63.2, mapper.Map(63.2));
        Assert.Equal(80, mapper.Map(80));
    }
}

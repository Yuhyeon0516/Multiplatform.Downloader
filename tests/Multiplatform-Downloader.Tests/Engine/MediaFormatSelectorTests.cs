using Multiplatform_Downloader.Core.Engine;
using Multiplatform_Downloader.Core.Models;
using Multiplatform_Downloader.Core.Settings;

namespace Multiplatform_Downloader.Tests.Engine;

public class MediaFormatSelectorTests
{
    private readonly MediaFormatSelector _sut = new();

    private static IReadOnlyList<MediaFormat> SampleFormats() =>
    [
        new() { FormatId = "137", Height = 1080, ApproxSize = 12_000_000, VideoCodec = "avc1", IsVideoOnly = true },
        new() { FormatId = "136", Height = 720,  ApproxSize = 8_000_000,  VideoCodec = "avc1", IsVideoOnly = true },
        new() { FormatId = "247", Height = 720,  ApproxSize = 7_000_000,  VideoCodec = "vp9",  IsVideoOnly = true }, // 720p 중복
        new() { FormatId = "18",  Height = 360,  ApproxSize = 5_000_000,  VideoCodec = "avc1", AudioCodec = "mp4a" },
        new() { FormatId = "140", ApproxSize = 3_000_000, AudioCodec = "mp4a", IsAudioOnly = true },
        new() { FormatId = "sb3", Height = 27, VideoCodec = null }, // storyboard(비영상) — 제외돼야 함
    ];

    [Fact]
    public void should_exclude_storyboard_and_non_video_formats()
    {
        var options = _sut.BuildOptions(SampleFormats());

        // storyboard(height 27)는 옵션에 없어야 함
        Assert.DoesNotContain(options, o => o.Height == 27);
        Assert.DoesNotContain(options, o => o.FormatId == "sb3");
    }

    [Fact]
    public void should_list_distinct_heights_in_descending_order()
    {
        var options = _sut.BuildOptions(SampleFormats());

        var videoHeights = options.Where(o => !o.IsAudioOnly).Select(o => o.Height).ToList();
        Assert.Equal(new int?[] { 1080, 720, 360 }, videoHeights);
    }

    [Fact]
    public void should_pick_larger_format_when_height_duplicated()
    {
        var options = _sut.BuildOptions(SampleFormats());

        var option720 = options.Single(o => o.Height == 720);
        Assert.Equal("136", option720.FormatId); // 8MB > 7MB
    }

    [Fact]
    public void should_include_audio_only_option_when_present()
    {
        var options = _sut.BuildOptions(SampleFormats());

        var audio = options.Single(o => o.IsAudioOnly);
        Assert.Equal("140", audio.FormatId);
        Assert.Null(audio.Height);
    }

    [Fact]
    public void should_select_highest_when_preference_best()
    {
        var options = _sut.BuildOptions(SampleFormats());

        var selected = _sut.SelectByPreference(options, QualityPreference.Best);

        Assert.NotNull(selected);
        Assert.Equal(1080, selected!.Height);
    }

    [Fact]
    public void should_select_lowest_video_when_preference_worst()
    {
        var options = _sut.BuildOptions(SampleFormats());

        var selected = _sut.SelectByPreference(options, QualityPreference.Worst);

        Assert.NotNull(selected);
        Assert.Equal(360, selected!.Height);
    }

    [Fact]
    public void should_select_audio_when_preference_audio_only()
    {
        var options = _sut.BuildOptions(SampleFormats());

        var selected = _sut.SelectByPreference(options, QualityPreference.AudioOnly);

        Assert.NotNull(selected);
        Assert.True(selected!.IsAudioOnly);
    }

    [Fact]
    public void should_return_null_when_no_options()
    {
        var selected = _sut.SelectByPreference([], QualityPreference.Best);
        Assert.Null(selected);
    }

    [Fact]
    public void should_return_empty_when_no_video_or_audio_formats()
    {
        var options = _sut.BuildOptions([]);
        Assert.Empty(options);
    }
}

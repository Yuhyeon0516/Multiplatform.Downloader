using Multiplatform_Downloader.Core.Models;
using Multiplatform_Downloader.Core.Settings;

namespace Multiplatform_Downloader.Core.Engine;

/// <summary>사용자에게 보여줄 해상도 옵션. <see cref="Height"/>가 null이면 오디오 전용.</summary>
public sealed record ResolutionOption(int? Height, string Label, string FormatId, bool IsAudioOnly);

/// <summary>포맷 목록을 해상도 옵션으로 정규화하고 기본 품질을 선택한다(FR-03).</summary>
public sealed class MediaFormatSelector
{
    /// <summary>영상 포맷을 해상도별로 묶어 내림차순 옵션 목록으로 만들고, 오디오 전용 옵션을 1개 덧붙인다.</summary>
    public IReadOnlyList<ResolutionOption> BuildOptions(IReadOnlyList<MediaFormat> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);

        var options = formats
            // 실제 비디오 코덱이 있는 포맷만 — storyboard(vcodec=none) 등 비영상 포맷 제외
            .Where(f => f.VideoCodec is not null && f.Height is > 0)
            .GroupBy(f => f.Height!.Value)
            .OrderByDescending(group => group.Key)
            .Select(group =>
            {
                var representative = group.OrderByDescending(f => f.ApproxSize ?? 0).First();
                return new ResolutionOption(group.Key, $"{group.Key}p", representative.FormatId, IsAudioOnly: false);
            })
            .ToList();

        var bestAudio = formats
            .Where(f => f.IsAudioOnly)
            .OrderByDescending(f => f.ApproxSize ?? 0)
            .FirstOrDefault();
        if (bestAudio is not null)
            options.Add(new ResolutionOption(Height: null, "오디오만", bestAudio.FormatId, IsAudioOnly: true));

        return options;
    }

    /// <summary>기본 품질 설정에 따라 옵션 하나를 선택한다. 옵션이 없으면 null.</summary>
    public ResolutionOption? SelectByPreference(IReadOnlyList<ResolutionOption> options, QualityPreference preference)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count == 0)
            return null;

        var videoOptions = options.Where(o => !o.IsAudioOnly).ToList();

        return preference switch
        {
            QualityPreference.Best =>
                videoOptions.OrderByDescending(o => o.Height).FirstOrDefault() ?? options[0],
            QualityPreference.Worst =>
                videoOptions.OrderBy(o => o.Height).FirstOrDefault() ?? options[0],
            QualityPreference.AudioOnly =>
                options.FirstOrDefault(o => o.IsAudioOnly) ?? options[^1],
            _ => options[0],
        };
    }
}

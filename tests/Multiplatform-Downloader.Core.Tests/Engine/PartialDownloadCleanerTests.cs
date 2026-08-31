using Multiplatform_Downloader.Core.Engine;

namespace Multiplatform_Downloader.Tests.Engine;

/// <summary>
/// 실패 시 남는 yt-dlp 조각 파일 판정 검증. 실측(2026-08-02) 파일명 기반:
/// [id].f313.webm.part / .fhls-288.mp4.ytdl / .part-Frag692. 등.
/// 브라우저의 일반 name.ext.part 는 오삭제하지 않아야 한다.
/// </summary>
public class PartialDownloadCleanerTests
{
    [Theory]
    [InlineData("title [r3KaCZD1s4o].f313.webm.part")]
    [InlineData("title [2083700941870788608].fhls-288.mp4.part")]
    [InlineData("title [2083700941870788608].fhls-288.mp4.ytdl")]
    [InlineData("title [2083700941870788608].fhls-audio-128000-Audio.mp4.part-Frag692.")]
    [InlineData("clip.part-Frag12.ts")]
    public void should_detect_ytdlp_artifacts(string name)
    {
        Assert.True(PartialDownloadCleaner.IsYtDlpArtifact(name));
    }

    [Theory]
    [InlineData("Big Buck Bunny [aqz-KE-bpKQ].mp4")]        // 완성 파일
    [InlineData("document.pdf")]
    [InlineData("browser-download.zip.part")]                // 브라우저 일반 .part (format 조각 아님)
    [InlineData("photo.jpg")]
    public void should_not_flag_normal_or_browser_files(string name)
    {
        Assert.False(PartialDownloadCleaner.IsYtDlpArtifact(name));
    }

    [Fact]
    public void should_match_artifact_of_specific_id()
    {
        Assert.True(PartialDownloadCleaner.IsArtifactOf("v [abc123].f313.webm.part", "abc123"));
        Assert.False(PartialDownloadCleaner.IsArtifactOf("v [other].f313.webm.part", "abc123"));
        Assert.False(PartialDownloadCleaner.IsArtifactOf("v [abc123].mp4", "abc123")); // 완성 파일은 아님
    }

    [Fact]
    public void should_not_match_when_id_empty()
    {
        Assert.False(PartialDownloadCleaner.IsArtifactOf("v [abc].f1.mp4.part", ""));
    }
}

using System.Text.RegularExpressions;

namespace Multiplatform_Downloader.Core.Engine;

/// <summary>
/// 다운로드 실패·취소·중단 시 yt-dlp가 남긴 조각/임시 파일을 판정·정리한다.
/// <para>실측(2026-08-02) 잔여 아티팩트: <c>[id].f313.webm.part</c>, <c>.f...mp4.ytdl</c>,
/// <c>.part-Frag692.</c> 등. 브라우저의 일반 <c>name.ext.part</c>와 구분하기 위해 yt-dlp 특유의
/// 시그니처(<c>.ytdl</c> / <c>.part-Frag</c> / <c>.f&lt;format&gt;.&lt;ext&gt;.part</c>)만 대상으로 한다.</para>
/// </summary>
public static class PartialDownloadCleaner
{
    // .f313.webm.part / .fhls-288.mp4.part / .fhls-audio-...mp4.part 등
    private static readonly Regex PerFormatPart = new(
        @"\.f[\w-]+\.[\w]+\.part$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>파일명이 yt-dlp 조각/임시 아티팩트인지(일반 파일·브라우저 .part 오삭제 방지).</summary>
    public static bool IsYtDlpArtifact(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        return fileName.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".part-Frag", StringComparison.OrdinalIgnoreCase)
            || PerFormatPart.IsMatch(fileName);
    }

    /// <summary>파일명이 특정 영상 id의 조각인지 — <c>[id]</c> 포함 + 아티팩트.</summary>
    public static bool IsArtifactOf(string fileName, string sourceId)
    {
        if (string.IsNullOrEmpty(sourceId)) return false;
        return fileName.Contains("[" + sourceId + "]", StringComparison.Ordinal)
            && IsYtDlpArtifact(fileName);
    }
}

using Multiplatform_Downloader.Core.Models;

namespace Multiplatform_Downloader.Core.Engine;

/// <summary>
/// yt-dlp로 다운로드를 실행한다(FR-04). 진행률 라인을 파싱해 <see cref="IProgress{T}"/>로 보고하고,
/// 최종 파일 경로(<c>--print after_move:filepath</c> 출력)를 수집한다.
/// 취소는 <see cref="IProcessRunner"/>가 프로세스를 종료하는 방식으로 처리한다(일시정지=중단, 재개=-c).
/// </summary>
public sealed class DownloadEngine : IDownloadEngine
{
    private readonly IProcessRunner _processRunner;
    private readonly string _ytDlpPath;

    public DownloadEngine(IProcessRunner processRunner, string ytDlpPath)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        ArgumentException.ThrowIfNullOrWhiteSpace(ytDlpPath);
        _ytDlpPath = ytDlpPath;
    }

    public async Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var arguments = YtDlpArgumentBuilder.BuildDownload(request);
        string? lastFilePathCandidate = null;
        // 병합 포맷은 영상→오디오 순으로 스트림별 0~100%가 두 번 나온다 — 단일 축으로 매핑
        var progressMapper = new MultiStreamProgressMapper(request.FormatId);

        var result = await _processRunner.RunAsync(
            _ytDlpPath,
            arguments,
            onOutputLine: line =>
            {
                var parsed = YtDlpOutputParser.ParseProgressLine(line);
                if (parsed is not null)
                {
                    progress?.Report(parsed with { Percent = progressMapper.Map(parsed.Percent) });
                }
                else if (LooksLikePath(line))
                {
                    // --print after_move:filepath 가 출력한 최종 경로 후보(진행률이 아닌 라인)
                    lastFilePathCandidate = line.Trim();
                }
            },
            cancellationToken).ConfigureAwait(false);

        var success = result.ExitCode == 0;
        // 실패 시 진단을 위해 stderr 마지막 ERROR 라인을 실어 보낸다(그냥 '종료 코드 1'은 원인 불명).
        var errorLine = success ? null : ExtractionErrorClassifier.ExtractLastErrorLine(result.StandardError);
        return new DownloadResult(success, success ? lastFilePathCandidate : null, result.ExitCode, errorLine);
    }

    private static bool LooksLikePath(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var trimmed = line.Trim();
        // yt-dlp 로그 접두([download], [info], ERROR 등)는 제외하고, 루트 경로 형태만 후보로(M6)
        if (trimmed.StartsWith('[')
            || trimmed.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        try { return Path.IsPathRooted(trimmed); }
        catch (ArgumentException) { return false; }
    }
}

namespace Multiplatform_Downloader.Core.Engine;

/// <summary>
/// 외부 프로세스(yt-dlp/FFmpeg 등) 실행 추상화 — 테스트에서 mock하는 지점.
/// 인자는 반드시 <see cref="System.Collections.Generic.IReadOnlyList{T}"/>로 받아
/// <c>ProcessStartInfo.ArgumentList</c>에 전달한다(문자열 연결 금지, NFR-03).
/// </summary>
public interface IProcessRunner
{
    /// <param name="fileName">실행 파일 경로.</param>
    /// <param name="arguments">인자 목록 — 각 요소가 개별 인자로 전달된다.</param>
    /// <param name="onOutputLine">표준 출력 라인 콜백(진행률 파싱 등). null이면 무시.</param>
    /// <param name="cancellationToken">취소 시 프로세스 트리를 종료한다.</param>
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default);
}

/// <summary>프로세스 실행 결과.</summary>
public sealed record ProcessResult(
    int ExitCode,
    IReadOnlyList<string> StandardOutput,
    IReadOnlyList<string> StandardError);

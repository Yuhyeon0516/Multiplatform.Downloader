using System.Diagnostics;
using System.Text;

namespace Multiplatform_Downloader.Core.Engine;

/// <summary>
/// <see cref="IProcessRunner"/>의 프로덕션 구현.
/// 인자는 <c>ProcessStartInfo.ArgumentList</c>로 전달해 셸 해석·명령 주입을 차단한다(NFR-03).
/// 취소 시 프로세스 트리를 종료한다(NFR-07). 출력 캡처는 라인 상한을 두어 로그 폭주로 인한
/// 메모리 소진을 방지하고, 별도 스레드에서 오는 출력 이벤트를 lock으로 직렬화한다(규칙 I-05).
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private const int MaxCapturedLines = 10_000;

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // yt-dlp(Python)는 파이프 리다이렉트 시 로캘 인코딩(CP949)으로 출력하며
            // 표현 불가 문자(간체자 등)를 '버린다' — Destination 경로가 훼손돼
            // OutputFilePath ≠ 실제 파일명이 된다(2026-08-03 인앱 재생 실패 근본 원인).
            // 자식은 UTF-8로 내보내고(.NET도 UTF-8로 읽는다) 전 구간 무손실을 보장한다.
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var gate = new object();
        var standardOutput = new List<string>();
        var standardError = new List<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (gate)
            {
                if (standardOutput.Count < MaxCapturedLines)
                    standardOutput.Add(e.Data);
            }
            // 콜백은 lock 밖에서 호출 — 재진입 데드락 방지(I-05)
            onOutputLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (gate)
            {
                if (standardError.Count < MaxCapturedLines)
                    standardError.Add(e.Data);
            }
        };

        cancellationToken.ThrowIfCancellationRequested(); // 이미 취소됐으면 프로세스를 띄우지 않는다(L1)
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            throw;
        }

        lock (gate)
        {
            return new ProcessResult(process.ExitCode, standardOutput.ToArray(), standardError.ToArray());
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // best-effort 정리 — 이미 종료됐거나 접근 불가한 경우 무시
        }
    }
}

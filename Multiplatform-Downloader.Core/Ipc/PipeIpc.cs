using System.IO.Pipes;

namespace Multiplatform_Downloader.Core.Ipc;

/// <summary>
/// 단일 인스턴스 간 URL 전달용 Named Pipe 서버(FR-08). 한 줄 메시지를 수신해 <see cref="MessageReceived"/>로 발화한다.
/// 메시지 길이를 제한해 과도한 입력을 방지한다(NFR-11).
/// </summary>
public sealed class PipeIpcServer : IDisposable
{
    private const int MaxMessageLength = 4096;

    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;

    public PipeIpcServer(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
    }

    public event EventHandler<string>? MessageReceived;

    public void Start()
    {
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                using var reader = new StreamReader(server);
                var buffer = new char[MaxMessageLength];
                var read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                var message = new string(buffer, 0, read).Trim();

                if (message.Length > 0)
                    MessageReceived?.Invoke(this, message);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // 파이프 오류(IO/권한/기타) — 리스너는 죽지 않고 다음 연결을 계속 대기한다.
                // UnauthorizedAccessException 등 예기치 못한 예외가 리스너 태스크를 중단시켜
                // '침묵 사망'하거나 프로세스를 크래시시키는 것을 방지한다. 타이트 루프 방지용 짧은 대기.
                try { await Task.Delay(200, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listenTask?.Wait(TimeSpan.FromSeconds(1)); } catch { /* 종료 대기 실패 무시 */ }
        _cts.Dispose();
    }
}

/// <summary>실행 중인 인스턴스에 URL을 보내는 클라이언트(FR-08). 서버가 없으면 false.</summary>
public static class PipeIpcClient
{
    public static async Task<bool> TrySendAsync(
        string pipeName,
        string message,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        // maxInstances=1 서버는 한 번에 한 연결만 처리하고 매 연결 후 파이프를 재생성한다. 확장으로
        // 빠르게 연속 다운로드하면 2차 인스턴스들이 동시에 접속하려다 경쟁에 밀려(접근 거부/타임아웃)
        // URL이 조용히 유실될 수 있다. 짧은 재시도로 서버가 다음 파이프를 열 때까지 몇 번 더 시도한다.
        const int maxAttempts = 3;
        var perAttempt = TimeSpan.FromMilliseconds(Math.Max(300, timeout.TotalMilliseconds / maxAttempts));

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                await client.ConnectAsync((int)perAttempt.TotalMilliseconds, cancellationToken).ConfigureAwait(false);

                await using var writer = new StreamWriter(client) { AutoFlush = true };
                await writer.WriteAsync(message.AsMemory(), cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception)
            {
                // 단일 인스턴스 URL 전달은 best-effort다. 서버 부재·연결 타임아웃(TimeoutException)·
                // 파이프 IO 오류(IOException)·권한 거부(UnauthorizedAccessException, 무결성 불일치나
                // maxInstances=1 동시 접속 경쟁 시 발생)·취소 등 어떤 예외도 프로세스를 죽여선 안 된다.
                // 실측(2026-08-30): 확장으로 빠르게 연속 다운로드 시 이 연결이 UnauthorizedAccessException을
                // 던졌고, async void OnStartup에서 미처리되어 앱이 통째로 크래시했다.
                if (attempt < maxAttempts - 1)
                {
                    try { await Task.Delay(120, cancellationToken).ConfigureAwait(false); }
                    catch { return false; } // 취소 시 조용히 중단(throw 금지)
                }
            }
        }
        return false;
    }
}

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
            catch (IOException)
            {
                // 파이프 오류 — 다음 연결 대기
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
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await client.ConnectAsync((int)timeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);

            await using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteAsync(message.AsMemory(), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}

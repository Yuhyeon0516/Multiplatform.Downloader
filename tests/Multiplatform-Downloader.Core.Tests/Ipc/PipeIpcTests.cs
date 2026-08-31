using Multiplatform_Downloader.Core.Ipc;

namespace Multiplatform_Downloader.Tests.Ipc;

public class PipeIpcTests
{
    [Fact]
    public async Task should_send_url_to_running_instance()
    {
        var pipeName = "mpdl-test-" + Guid.NewGuid().ToString("N");
        using var server = new PipeIpcServer(pipeName);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.MessageReceived += (_, message) => received.TrySetResult(message);
        server.Start();

        var sent = await PipeIpcClient.TrySendAsync(
            pipeName, "mpdl://add?url=abc", TimeSpan.FromSeconds(2));

        Assert.True(sent);
        var completed = await Task.WhenAny(received.Task, Task.Delay(2000));
        Assert.Same(received.Task, completed);
        Assert.Equal("mpdl://add?url=abc", await received.Task);
    }

    [Fact]
    public async Task should_return_false_when_no_server_running()
    {
        var pipeName = "mpdl-test-none-" + Guid.NewGuid().ToString("N");

        var sent = await PipeIpcClient.TrySendAsync(
            pipeName, "x", TimeSpan.FromMilliseconds(300));

        Assert.False(sent);
    }

    // 회귀 방지(2026-08-30): 확장으로 빠르게 연속 다운로드하면 mpdl 2차 인스턴스들이 maxInstances=1
    // 파이프에 동시에 접속하려다 경쟁으로 UnauthorizedAccessException을 던졌고, 미처리되어 앱이 크래시했다.
    // 다수 클라이언트가 동시에 접속해도 어떤 예외도 던지지 않고 bool로 완주해야 한다.
    [Fact]
    public async Task should_not_throw_when_many_clients_race_on_single_instance_server()
    {
        var pipeName = "mpdl-race-" + Guid.NewGuid().ToString("N");
        using var server = new PipeIpcServer(pipeName);
        server.Start();

        var tasks = Enumerable.Range(0, 40)
            .Select(i => PipeIpcClient.TrySendAsync(pipeName, $"mpdl://add?url=race{i}", TimeSpan.FromSeconds(1)))
            .ToArray();

        // 예외를 던지면 WhenAll이 재던져 테스트가 실패한다 — 즉 '크래시 없음'을 검증한다.
        // (경쟁으로 일부는 false일 수 있으나 최소 하나는 전달 성공해야 서버가 정상 동작함을 확인)
        var results = await Task.WhenAll(tasks);
        Assert.Equal(40, results.Length);
        Assert.Contains(true, results);
    }

    // 회귀 방지: 어떤 파이프 오류든(권한 거부 포함) 클라이언트는 절대 예외를 던지지 않는다.
    [Fact]
    public async Task should_return_false_and_not_throw_when_send_fails_repeatedly()
    {
        var pipeName = "mpdl-none-" + Guid.NewGuid().ToString("N");

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => PipeIpcClient.TrySendAsync(pipeName, "x", TimeSpan.FromMilliseconds(150)))
            .ToArray();

        var results = await Task.WhenAll(tasks); // 예외 없이 완주
        Assert.All(results, r => Assert.False(r));
    }
}

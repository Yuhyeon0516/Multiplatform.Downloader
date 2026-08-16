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
}

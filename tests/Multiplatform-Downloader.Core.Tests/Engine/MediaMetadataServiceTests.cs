using Multiplatform_Downloader.Core.Engine;

namespace Multiplatform_Downloader.Tests.Engine;

public class MediaMetadataServiceTests
{
    private const string ValidJson = """{ "id": "abc", "title": "제목", "formats": [] }""";

    [Fact]
    public async Task should_return_media_info_when_engine_succeeds()
    {
        var runner = new FakeProcessRunner(new ProcessResult(0, new[] { ValidJson }, Array.Empty<string>()));
        var sut = new MediaMetadataService(runner, "yt-dlp.exe");

        var info = await sut.FetchAsync("https://www.youtube.com/watch?v=abc");

        Assert.Equal("abc", info.Id);
        Assert.Equal("제목", info.Title);
    }

    [Fact]
    public async Task should_pass_ignore_config_and_separator_when_fetching()
    {
        var runner = new FakeProcessRunner(new ProcessResult(0, new[] { ValidJson }, Array.Empty<string>()));
        var sut = new MediaMetadataService(runner, "yt-dlp.exe");

        await sut.FetchAsync("https://www.youtube.com/watch?v=abc");

        Assert.Contains("--ignore-config", runner.LastArguments!);
        // URL 바로 앞에 "--" 구분자 (인자 인젝션 방어)
        var separatorIndex = runner.LastArguments!.ToList().IndexOf("--");
        var urlIndex = runner.LastArguments!.ToList().IndexOf("https://www.youtube.com/watch?v=abc");
        Assert.True(separatorIndex >= 0 && separatorIndex == urlIndex - 1);
    }

    [Fact]
    public async Task should_throw_when_engine_exits_nonzero()
    {
        var runner = new FakeProcessRunner(new ProcessResult(1, Array.Empty<string>(), new[] { "ERROR: unavailable" }));
        var sut = new MediaMetadataService(runner, "yt-dlp.exe");

        await Assert.ThrowsAsync<MetadataFetchException>(
            () => sut.FetchAsync("https://www.youtube.com/watch?v=x"));
    }

    [Fact]
    public async Task should_throw_when_output_malformed()
    {
        var runner = new FakeProcessRunner(new ProcessResult(0, new[] { "{ not json" }, Array.Empty<string>()));
        var sut = new MediaMetadataService(runner, "yt-dlp.exe");

        await Assert.ThrowsAsync<MetadataFetchException>(
            () => sut.FetchAsync("https://www.youtube.com/watch?v=x"));
    }

    [Fact]
    public async Task should_throw_metadata_exception_when_timeout()
    {
        var runner = new FakeProcessRunner(
            new ProcessResult(0, new[] { ValidJson }, Array.Empty<string>()),
            delay: TimeSpan.FromSeconds(30));
        var sut = new MediaMetadataService(runner, "yt-dlp.exe", timeout: TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<MetadataFetchException>(
            () => sut.FetchAsync("https://www.youtube.com/watch?v=x"));
    }

    [Fact]
    public async Task should_cancel_when_token_triggered()
    {
        var runner = new FakeProcessRunner(
            new ProcessResult(0, new[] { ValidJson }, Array.Empty<string>()),
            delay: TimeSpan.FromSeconds(30));
        var sut = new MediaMetadataService(runner, "yt-dlp.exe");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.FetchAsync("https://www.youtube.com/watch?v=x", cts.Token));
    }

    /// <summary>미리 정한 결과를 (선택적 지연 후) 반환하고 마지막 인자를 기록하는 테스트 러너.</summary>
    private sealed class FakeProcessRunner(ProcessResult result, TimeSpan? delay = null) : IProcessRunner
    {
        private readonly TimeSpan _delay = delay ?? TimeSpan.Zero;
        public IReadOnlyList<string>? LastArguments { get; private set; }

        public async Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            Action<string>? onOutputLine = null,
            CancellationToken cancellationToken = default)
        {
            LastArguments = arguments;
            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            return result;
        }
    }
}

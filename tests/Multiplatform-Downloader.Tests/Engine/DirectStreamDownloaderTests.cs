using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Multiplatform_Downloader.Core.Engine;
using Multiplatform_Downloader.Core.Models;

namespace Multiplatform_Downloader.Tests.Engine;

public class DirectStreamDownloaderTests : IDisposable
{
    private readonly string _tempDir;

    public DirectStreamDownloaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mpdl-stream-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 무시 */ }
    }

    [Fact]
    public async Task should_download_stream_to_file()
    {
        var content = Encoding.UTF8.GetBytes(new string('x', 5000));
        using var downloader = new DirectStreamDownloader(new FakeContentHandler(content));
        var outputPath = Path.Combine(_tempDir, "out.mp4");

        await downloader.DownloadAsync("https://cdn.xhscdn.com/v.mp4", outputPath);

        Assert.True(File.Exists(outputPath));
        Assert.Equal(content.Length, new FileInfo(outputPath).Length);
    }

    [Fact]
    public async Task should_report_progress_when_content_length_known()
    {
        var content = Encoding.UTF8.GetBytes(new string('y', 200_000));
        using var downloader = new DirectStreamDownloader(new FakeContentHandler(content));
        var reports = new List<DownloadProgress>();
        var progress = new SyncProgress(reports);

        await downloader.DownloadAsync("https://cdn.xhscdn.com/v.mp4", Path.Combine(_tempDir, "out.mp4"), progress);

        Assert.NotEmpty(reports);
        Assert.Equal(100, reports[^1].Percent, precision: 0);
    }

    private sealed class SyncProgress(List<DownloadProgress> sink) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => sink.Add(value);
    }

    private sealed class FakeContentHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
                RequestMessage = request,
            };
            return Task.FromResult(response);
        }
    }
}

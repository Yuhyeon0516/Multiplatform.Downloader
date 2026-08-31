using System.Diagnostics;
using System.Net;
using Multiplatform_Downloader.Core.Models;
using Multiplatform_Downloader.Core.Update;

namespace Multiplatform_Downloader.Tests.Update;

public class UpdateInstallerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"mpdl-upd-{Guid.NewGuid():N}");

    // 유효한 PE FileVersion을 갖는 실제 파일이 필요하므로 테스트 어셈블리 자신을 페이로드로 쓴다.
    private static readonly string SelfPath = typeof(UpdateInstallerTests).Assembly.Location;
    private static readonly byte[] SelfBytes = File.ReadAllBytes(SelfPath);
    private static readonly Version SelfVersion =
        Version.Parse(FileVersionInfo.GetVersionInfo(SelfPath).FileVersion ?? "1.0.0.0");

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* best-effort */ }
    }

    private static UpdateInfo Info(Version version, long size, string url =
        "https://github.com/ghlee0786/Multiplatform.Downloader/releases/download/v2.13.0/ShyshyroongDownloader_Setup_v2.13.0.0.exe")
        => new($"v{version}", version, "notes", "ShyshyroongDownloader_Setup_v2.13.0.0.exe", url, size);

    [Fact] // UP-B14 / ISS-03 — http 다운그레이드 거부
    public async Task should_fail_when_url_not_https()
    {
        using var sut = new UpdateInstaller(updatesFolder: _folder, handler: new StubHandler([]));
        var r = await sut.DownloadAsync(Info(SelfVersion, 10, "http://github.com/x/setup.exe"), new Version(0, 1, 0, 0));
        Assert.False(r.Success);
    }

    [Fact] // ISS-02 — 허용 목록 밖 호스트 거부
    public async Task should_fail_when_host_not_allowed()
    {
        using var sut = new UpdateInstaller(updatesFolder: _folder, handler: new StubHandler([]));
        var r = await sut.DownloadAsync(Info(SelfVersion, 10, "https://evil.example.com/setup.exe"), new Version(0, 1, 0, 0));
        Assert.False(r.Success);
    }

    [Fact] // UP-B01 — 정상 다운로드 + 크기 대조 + 진행률 + 버전 검증 통과
    public async Task should_download_and_report_progress_when_valid()
    {
        var current = new Version(0, 0, 1, 0); // Self보다 확실히 낮게
        using var sut = new UpdateInstaller(updatesFolder: _folder, handler: new StubHandler(SelfBytes));
        var progress = new CollectingProgress();
        var r = await sut.DownloadAsync(Info(SelfVersion, SelfBytes.Length), current, progress);

        Assert.True(r.Success, r.Error);
        Assert.True(File.Exists(r.InstallerPath));
        Assert.Equal(100, progress.Last, 0);
        Assert.DoesNotContain(Directory.GetFiles(_folder), f => f.EndsWith(".tmp"));
    }

    [Fact] // UP-B08 — 크기 불일치 시 거부·정리
    public async Task should_fail_when_size_mismatch()
    {
        using var sut = new UpdateInstaller(updatesFolder: _folder, handler: new StubHandler(SelfBytes));
        var r = await sut.DownloadAsync(Info(SelfVersion, SelfBytes.Length + 1), new Version(0, 0, 1, 0));
        Assert.False(r.Success);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact] // ISS-04 — 롤백 방지: 파일 버전이 현재보다 상위가 아니면 거부
    public async Task should_reject_when_file_version_not_higher_than_current()
    {
        var current = new Version(99, 0, 0, 0); // Self보다 확실히 높게 → 롤백 취급
        using var sut = new UpdateInstaller(updatesFolder: _folder, handler: new StubHandler(SelfBytes));
        var r = await sut.DownloadAsync(Info(SelfVersion, SelfBytes.Length), current);
        Assert.False(r.Success);
        Assert.Empty(Directory.GetFiles(_folder)); // 검증 실패 시 삭제
    }

    [Fact] // ISS-04 — 광고 버전과 실제 파일 버전 불일치 시 거부(VerifyInstaller 직접)
    public void should_reject_when_advertised_version_mismatch()
    {
        using var sut = new UpdateInstaller(updatesFolder: _folder);
        var r = sut.VerifyInstaller(SelfPath, advertised: new Version(99, 9, 9, 9), currentVersion: new Version(0, 1, 0, 0));
        Assert.False(r.Success);
    }

    [Fact] // 커버리지 갭 — 리다이렉트 최종 호스트가 allowlist 밖이면 거부(response.RequestMessage.RequestUri 검증)
    public async Task should_fail_when_final_redirect_host_not_allowed()
    {
        // 최초 URL은 allowlist(github.com)지만 최종 응답의 RequestUri를 허용 밖 호스트로 위장
        using var sut = new UpdateInstaller(updatesFolder: _folder,
            handler: new RedirectedHostHandler(SelfBytes, "https://evil.example.com/final.exe"));
        var r = await sut.DownloadAsync(Info(SelfVersion, SelfBytes.Length), new Version(0, 0, 1, 0));
        Assert.False(r.Success);
    }

    [Fact] // 고아 .tmp 정리
    public void should_sweep_stale_tmp_files()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "ShyshyroongDownloader_Setup_v2.13.0.0.exe.tmp"), "x");
        using var sut = new UpdateInstaller(updatesFolder: _folder);
        sut.SweepStaleDownloads(new Version(2, 12, 7, 0));
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    private sealed class StubHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(payload),
            });
    }

    /// <summary>200을 반환하되 최종 RequestUri를 다른(허용 밖) 호스트로 위장 — 리다이렉트 후 재검증 테스트.</summary>
    private sealed class RedirectedHostHandler(byte[] payload, string finalUrl) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri = new Uri(finalUrl); // 자동 리다이렉트가 최종 URL을 갱신한 것처럼 위장
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(payload),
            });
        }
    }

    private sealed class CollectingProgress : IProgress<DownloadProgress>
    {
        public double Last { get; private set; }
        public void Report(DownloadProgress value) => Last = value.Percent;
    }
}

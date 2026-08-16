using Multiplatform_Downloader.Core.Models;
using Multiplatform_Downloader.Core.Settings;

namespace Multiplatform_Downloader.Core.Engine;

/// <summary>
/// yt-dlp <c>-J</c>로 메타데이터를 조회한다(FR-02). 비동기·취소 가능·타임아웃 적용(NFR-02).
/// 인자 인젝션 방어를 위해 <c>--ignore-config</c>와 위치 인자 앞 <c>--</c> 구분자를 강제한다(NFR-12).
/// 로그인 필요 콘텐츠 분석을 위해 현재 쿠키 설정(<paramref name="cookieProvider"/>)을 매 조회에 반영한다.
/// </summary>
public sealed class MediaMetadataService : IMediaMetadataService
{
    private readonly IProcessRunner _processRunner;
    private readonly string _ytDlpPath;
    private readonly TimeSpan _timeout;
    private readonly Func<CookieOptions>? _cookieProvider;

    public MediaMetadataService(IProcessRunner processRunner, string ytDlpPath, TimeSpan? timeout = null, Func<CookieOptions>? cookieProvider = null)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        ArgumentException.ThrowIfNullOrWhiteSpace(ytDlpPath);
        _ytDlpPath = ytDlpPath;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        _cookieProvider = cookieProvider;
    }

    public async Task<MediaInfo> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(_timeout);

        // NFR-12: --ignore-config·"--" 구분자는 YtDlpArgumentBuilder가 강제
        var arguments = YtDlpArgumentBuilder.BuildMetadata(url, cookies: _cookieProvider?.Invoke());

        ProcessResult result;
        try
        {
            result = await _processRunner
                .RunAsync(_ytDlpPath, arguments, onOutputLine: null, linkedCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 외부 취소가 아니면 타임아웃 — 네트워크 계열로 분류해 자동 재시도 대상(FR-D2.1)
            throw new MetadataFetchException(new ExtractionFailure(
                ExtractionFailureKind.Network,
                $"메타데이터 조회 시간 초과({_timeout.TotalSeconds:0}초) — 네트워크를 확인하세요",
                string.Empty));
        }

        if (result.ExitCode != 0)
        {
            // FR-D2.3: 분류 정보는 stderr의 마지막 ERROR 라인에만 존재(실측) — 앞줄은 WARNING 재시도 로그
            var failure = ExtractionErrorClassifier.ClassifyStderr(result.StandardError);
            throw new MetadataFetchException(failure);
        }

        var json = string.Join("\n", result.StandardOutput);
        try
        {
            return YtDlpOutputParser.ParseInfo(json);
        }
        catch (FormatException ex)
        {
            throw new MetadataFetchException($"메타데이터 파싱 실패: {ex.Message}", ex);
        }
    }
}

using System.Net;
using System.Net.Sockets;
using Multiplatform_Downloader.Core.Net;

namespace Multiplatform_Downloader.Core.Platforms;

/// <summary>
/// 단축/공유 URL(xhslink.com 등)의 HTTP 리다이렉트를 선해석해 정식 URL로 정규화한다(FR-01).
/// 자동 리다이렉트를 끄고 매 단계를 <see cref="SsrfGuard"/>로 재검증한다(NFR-10). 방어 3중:
/// ① 스킴/호스트 검사 ② 실제 연결 IP를 ConnectCallback에서 <see cref="SsrfGuard.IsBlockedAddress"/>로 검증(DNS 리바인딩·대체 표기 차단)
/// ③ 리다이렉트 수·HTTPS 다운그레이드·총 타임아웃 제한.
/// </summary>
public sealed class ShortUrlResolver : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHandler;
    private readonly int _maxRedirects;
    private readonly TimeSpan _totalTimeout;

    /// <param name="handler">테스트용 주입. null이면 SSRF ConnectCallback이 적용된 기본 핸들러 사용.</param>
    /// <param name="maxRedirects">최대 리다이렉트 횟수. 초과 시 예외.</param>
    /// <param name="totalTimeout">전체 리졸브 작업의 총 데드라인(기본 30초).</param>
    public ShortUrlResolver(HttpMessageHandler? handler = null, int maxRedirects = 10, TimeSpan? totalTimeout = null)
    {
        _maxRedirects = maxRedirects;
        _totalTimeout = totalTimeout ?? TimeSpan.FromSeconds(30);
        _ownsHandler = handler is null;
        var effectiveHandler = handler ?? SsrfGuard.CreateGuardedHandler(allowAutoRedirect: false);
        _httpClient = new HttpClient(effectiveHandler, disposeHandler: _ownsHandler)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
    }

    /// <summary>리다이렉트를 따라 최종 URL을 반환한다. 리다이렉트가 없으면 입력을 그대로 반환.</summary>
    /// <exception cref="ArgumentException">URL이 절대 URI가 아닐 때.</exception>
    /// <exception cref="SsrfBlockedException">내부망 대상·다운그레이드로 판정될 때.</exception>
    /// <exception cref="InvalidOperationException">리다이렉트 한도 초과 또는 Location 누락.</exception>
    public async Task<string> ResolveAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var current))
            throw new ArgumentException("유효하지 않은 절대 URL입니다.", nameof(url));

        SsrfGuard.EnsureSafe(current);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(_totalTimeout);
        var token = linkedCts.Token;

        for (var hop = 0; hop < _maxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, current);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);

            if (!IsRedirect((int)response.StatusCode))
                return current.ToString();

            var location = response.Headers.Location
                ?? throw new InvalidOperationException("리다이렉트 응답에 Location 헤더가 없습니다.");

            var next = location.IsAbsoluteUri ? location : new Uri(current, location);

            if (current.Scheme == Uri.UriSchemeHttps && next.Scheme == Uri.UriSchemeHttp)
                throw new SsrfBlockedException("HTTPS→HTTP 다운그레이드 리다이렉트 차단");

            SsrfGuard.EnsureSafe(next); // 매 리다이렉트 단계 재검증
            current = next;
        }

        throw new InvalidOperationException($"리다이렉트가 최대 {_maxRedirects}회를 초과했습니다.");
    }

    private static bool IsRedirect(int statusCode) => statusCode is >= 300 and < 400;

    public void Dispose() => _httpClient.Dispose();
}

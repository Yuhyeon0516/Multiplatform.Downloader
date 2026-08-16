using System.Text;

namespace Multiplatform_Downloader.Core.Net;

/// <summary>cookies.txt 한 줄에 해당하는 쿠키 레코드. <paramref name="ExpiresUnix"/>는 세션 쿠키면 0.</summary>
public sealed record CookieRecord(
    string Domain,
    string Path,
    bool Secure,
    long ExpiresUnix,
    string Name,
    string Value);

/// <summary>
/// yt-dlp가 읽는 Netscape cookies.txt 형식 직렬화(FR-L4). 순수 함수 — 파일 I/O는 호출자 몫.
/// <para>필드: domain, includeSubdomains('.'로 시작하는 도메인=TRUE), path, secure, expiry(unix, 세션=0), name, value.</para>
/// <para>보안: 쿠키 값은 로그에 남기지 않는다(NFR-L1) — 이 클래스는 로깅하지 않는다.</para>
/// </summary>
public static class CookieFileWriter
{
    public const string Header = "# Netscape HTTP Cookie File";

    public static string Serialize(IEnumerable<CookieRecord> cookies)
    {
        ArgumentNullException.ThrowIfNull(cookies);
        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');
        foreach (var c in cookies)
        {
            if (string.IsNullOrWhiteSpace(c.Domain) || string.IsNullOrEmpty(c.Name))
                continue; // 도메인/이름 없는 레코드는 yt-dlp 파서를 깨뜨린다 — 제외
            var includeSubdomains = c.Domain.StartsWith('.') ? "TRUE" : "FALSE";
            var path = string.IsNullOrEmpty(c.Path) ? "/" : c.Path;
            var expires = c.ExpiresUnix < 0 ? 0 : c.ExpiresUnix;
            sb.Append(c.Domain).Append('\t')
              .Append(includeSubdomains).Append('\t')
              .Append(path).Append('\t')
              .Append(c.Secure ? "TRUE" : "FALSE").Append('\t')
              .Append(expires).Append('\t')
              .Append(c.Name).Append('\t')
              .Append(c.Value).Append('\n');
        }
        return sb.ToString();
    }
}

using System.IO;
using System.Text.RegularExpressions;

namespace Multiplatform_Downloader.Tests.Fixtures;

/// <summary>
/// 통합 테스트용 프록시 URL 로더(SETUP-06 · NFR-08).
/// <para>이 프록시는 <b>테스트 전용</b>이며 프로덕션 앱 기능이 아니다. 자격증명은 로그에 남기지 않는다(<see cref="Mask"/>).</para>
/// <para>로드 우선순위(<c>.env.example</c> 규약):
/// ① OS 환경변수 <c>MPDL_TEST_PROXY</c> → ② <c>.env</c>의 <c>MPDL_TEST_PROXY</c> →
/// ③ <c>.env</c>의 <c>WEBSHARE_*</c> 조각값 자동 조립 → 없으면 <c>null</c>(프록시 없이 직접 연결).</para>
/// </summary>
public static partial class TestProxyLoader
{
    private static readonly string[] Keys =
        ["MPDL_TEST_PROXY", "WEBSHARE_USER_BASE", "WEBSHARE_PASS", "WEBSHARE_PORT", "WEBSHARE_ROTATE"];

    /// <summary>실제 환경(OS 환경변수 + 리포 루트 <c>.env</c>)에서 프록시 URL을 해석한다. 없으면 null.</summary>
    public static string? Resolve()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // .env 파일(있으면) → 낮은 우선순위
        var envPath = FindEnvFile();
        if (envPath is not null)
        {
            foreach (var (k, v) in ParseEnv(File.ReadAllText(envPath)))
                values[k] = v;
        }

        // OS 환경변수 → 높은 우선순위(덮어씀)
        foreach (var key in Keys)
        {
            var v = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrEmpty(v))
                values[key] = v;
        }

        return Assemble(values);
    }

    /// <summary>순수 함수: 병합된 값 맵에서 프록시 URL을 조립한다(테스트 결정성).</summary>
    public static string? Assemble(IReadOnlyDictionary<string, string?> values)
    {
        // ① 완성 URL 직접 지정 우선
        if (values.TryGetValue("MPDL_TEST_PROXY", out var direct) && !string.IsNullOrWhiteSpace(direct))
            return direct.Trim();

        // ② WEBSHARE_* 조각값 조립 — 사용자/비밀번호가 없으면 프록시 미사용
        var user = Get(values, "WEBSHARE_USER_BASE");
        var pass = Get(values, "WEBSHARE_PASS");
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            return null;

        var port = Get(values, "WEBSHARE_PORT");
        if (string.IsNullOrWhiteSpace(port))
            port = "80";

        var rotate = string.Equals(Get(values, "WEBSHARE_ROTATE"), "true", StringComparison.OrdinalIgnoreCase);
        var userPart = rotate ? $"{user}-rotate" : user;
        return $"http://{userPart}:{pass}@p.webshare.io:{port}";
    }

    /// <summary>로그·오류 메시지에 안전하게 남길 수 있도록 자격증명(user:pass)을 마스킹한다.</summary>
    public static string Mask(string? proxyUrl)
    {
        if (string.IsNullOrEmpty(proxyUrl))
            return "(직접 연결 · 프록시 없음)";
        var m = CredentialRegex().Match(proxyUrl);
        return m.Success ? $"{m.Groups[1].Value}***:***@{m.Groups[4].Value}" : proxyUrl;
    }

    /// <summary><c>KEY=VALUE</c> 형식의 <c>.env</c>를 파싱한다. <c>#</c> 주석·빈 줄·따옴표를 처리한다.</summary>
    public static Dictionary<string, string?> ParseEnv(string content)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim().Trim('"', '\'');
            result[key] = value;
        }
        return result;
    }

    private static string? Get(IReadOnlyDictionary<string, string?> values, string key)
        => values.TryGetValue(key, out var v) ? v : null;

    /// <summary>테스트 실행 위치에서 상위로 올라가며 리포 루트의 <c>.env</c>를 찾는다.</summary>
    private static string? FindEnvFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    [GeneratedRegex(@"^(\w+://)([^:@/]+):([^@/]+)@(.+)$")]
    private static partial Regex CredentialRegex();
}

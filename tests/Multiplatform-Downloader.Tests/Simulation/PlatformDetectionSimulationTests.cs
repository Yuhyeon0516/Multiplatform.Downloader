using System.Text.Json;
using Multiplatform_Downloader.Core.Platforms;

namespace Multiplatform_Downloader.Tests.Simulation;

/// <summary>
/// FR-P1 시나리오 시뮬레이션 러너(228케이스, 실측 프로브 2026-08-02 기반).
/// docs/analyses/simulation/fr-p1-scenarios.json 전체를 PlatformDetector에 돌리고
/// 라운드 로그를 남긴다. 실패 목록은 로그 파일에 — PRD 이슈 반영 근거.
/// </summary>
public class PlatformDetectionSimulationTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed record Scenario(string id, string platform, string url, string expectDetect);

    [Fact]
    public void should_detect_all_p1_scenarios_per_measured_matrix()
    {
        var simDir = Path.Combine(RepoRoot, "docs", "analyses", "simulation");
        var scenarios = JsonSerializer.Deserialize<List<Scenario>>(
            File.ReadAllText(Path.Combine(simDir, "fr-p1-scenarios.json")))!;
        Assert.True(scenarios.Count >= 100, "요구: 시나리오 100개 이상");

        // 전 플랫폼 구현 완료(v2.10.0) — 모든 시나리오가 통과해야 한다(시뮬 2차 목표: 248/248).
        var implemented = new HashSet<string>
        {
            "YouTube", "Instagram", "TikTok", "Xiaohongshu", "Threads",
            "Facebook", "X", "Douyin", "Reddit", "Pinterest", "Unknown",
        };
        var detector = new PlatformDetector();
        var failures = new List<string>();
        var pending = new List<string>();
        foreach (var s in scenarios)
        {
            var actual = detector.Detect(s.url).ToString();
            if (actual == s.expectDetect)
                continue;
            var line = $"{s.id} [{s.platform}] expect={s.expectDetect} actual={actual} url={s.url}";
            if (implemented.Contains(s.expectDetect))
                failures.Add("FAIL " + line);
            else
                pending.Add("PENDING " + line); // 확장 PRD 승인 후 구현 대상
        }

        // 라운드 로그(1차: 구현 전 갭 실측 / 2차+: 회귀 방지)
        var round = Directory.GetFiles(simDir, "fr-p1-sim-round*.log").Length + 1;
        var log = new List<string>
        {
            $"# FR-P1 시뮬레이션 {round}차 — {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"# 시나리오 {scenarios.Count}건, 구현 플랫폼 FAIL={failures.Count}, 확장PRD PENDING={pending.Count}",
            "",
        };
        log.AddRange(failures);
        log.AddRange(pending);
        File.WriteAllLines(Path.Combine(simDir, $"fr-p1-sim-round{round}.log"), log);

        Assert.True(failures.Count == 0,
            $"구현 플랫폼 감지 실패 {failures.Count}건 — fr-p1-sim-round{round}.log 참조. 첫 실패: {failures.FirstOrDefault()}");
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Multiplatform_Downloader.Services;

/// <summary>
/// 기동 시 방어 장치(2026-08-30 크래시 수정).
/// ① 관리자 권한으로 떴다면 일반 권한으로 자기 재실행 — 자동 업데이트(/AUTORELAUNCH)가
///    `runascurrentuser`에도 불구하고 앱을 관리자 권한으로 재기동시키면, 일반 권한 크롬이 띄우는
///    mpdl 2차 인스턴스가 파이프/뮤텍스 무결성 불일치로 접속 거부되어 확장 연동이 끊기고
///    미처리 예외로 크래시했다(실측). 항상 일반 권한으로 강등해 이를 근본 차단한다(앱은 admin 불필요).
/// ② 전역 예외 핸들러 — <b>기록</b>이 주목적이다. 실제 크래시 방지는 예외 발생 지점(IPC 파이프/단일
///    인스턴스 코드)의 try/catch가 담당한다. AppDomain 핸들러는 프로세스 종료를 막지 못하고(마지막 기록만),
///    UI(Dispatcher) 스레드 예외만 Handled=true로 앱을 유지시킬 수 있다. 아래 각 핸들러 주석 참조.
/// </summary>
public static class StartupGuards
{
    // explorer 재실행은 인자를 전달하지 못하므로 마커 인자로 루프를 막을 수 없다.
    // 대신 임시 마커 파일의 최신성 + 시도 횟수로 무한 재실행을 차단한다.
    private static readonly string MarkerPath =
        Path.Combine(Path.GetTempPath(), "mpdl_deelevate.marker");
    private const int MarkerFreshSeconds = 30;
    private const int MaxRelaunchAttempts = 2;

    /// <summary>현재 프로세스가 관리자(상위 무결성) 권한인가.</summary>
    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false; // 판정 실패 시 강등 시도 안 함
        }
    }

    /// <summary>
    /// 관리자 권한이면 explorer.exe(일반 권한 셸) 경유로 자기 자신을 재실행하고 true를 반환한다(호출부는 즉시 종료).
    /// 일반 권한·mpdl 실행·재실행 불가 시 false(그대로 진행). 무한 루프는 마커 파일(최신성+시도횟수)로 차단.
    /// </summary>
    public static bool TryRelaunchUnelevated(string[] args)
    {
        // mpdl:// 프로토콜 실행은 강등하지 않는다 — explorer 재실행은 인자를 못 넘겨 URL이 유실된다.
        // (그런 2차 인스턴스는 어차피 기존 인스턴스로 전달 후 종료하므로 강등이 불필요하다.)
        if (args is not null && args.Any(a => a.StartsWith("mpdl://", StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!IsElevated())
            return false;

        // 무한 재실행 방지: 최근(30s) 마커의 시도 횟수가 상한을 넘으면 강등을 포기하고 관리자 권한으로 진행.
        var attempt = 0;
        try
        {
            if (File.Exists(MarkerPath)
                && (DateTime.UtcNow - File.GetLastWriteTimeUtc(MarkerPath)).TotalSeconds < MarkerFreshSeconds)
            {
                attempt = ReadAttempt(MarkerPath);
                if (attempt >= MaxRelaunchAttempts)
                    return false; // 이미 여러 번 시도(셸까지 관리자 세션 등) — 루프 차단
            }
        }
        catch { /* 마커 판독 실패 → attempt=0 */ }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return false;

        // 마커를 '성공적으로' 기록하지 못하면 재실행하지 않는다 — 마커가 루프 차단의 유일 근거이므로 필수.
        // (기록 실패 시 다음 세대가 시도 횟수를 못 읽어 무한 스폰될 수 있으므로 여기서 중단한다.)
        if (!TryWriteMarker(MarkerPath, attempt + 1))
            return false;

        try
        {
            // explorer.exe는 기존 일반 권한 셸에 실행을 위임하므로 대상 앱이 일반 권한으로 뜬다.
            var explorer = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Process.Start(new ProcessStartInfo
            {
                FileName = explorer,
                Arguments = "\"" + exe + "\"",
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false; // 재실행 실패 시 관리자 권한으로라도 계속 실행(크래시 금지)
        }
    }

    private static int ReadAttempt(string path)
    {
        try { return int.TryParse(File.ReadAllText(path).Trim(), out var n) ? n : 0; }
        catch { return 0; }
    }

    private static bool TryWriteMarker(string path, int attempt)
    {
        try { File.WriteAllText(path, attempt.ToString()); return true; }
        catch { return false; }
    }

    /// <summary>
    /// 전역 미처리 예외 핸들러 등록. 주목적은 <b>진단 기록</b>이다. 실제 크래시 방지는 예외 발생 지점의
    /// try/catch(IPC/단일 인스턴스)가 담당한다. 여기서 새로운 fire-and-forget 백그라운드 코드가 안전해지는
    /// 것은 아니다 — 백그라운드 스레드의 진짜 미처리 예외는 여전히 프로세스를 종료시킨다(기록만 남음).
    /// </summary>
    public static void RegisterGlobalExceptionHandlers()
    {
        // 백그라운드/파이널라이저 스레드의 미처리 예외 — .NET에서 프로세스 종료는 막을 수 없다(마지막 기록만).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteFatal("AppDomain", e.ExceptionObject as Exception, terminating: e.IsTerminating);

        // 관측되지 않은 Task 예외 — 최신 .NET은 기본적으로 프로세스를 죽이지 않는다. 기록만.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteFatal("UnobservedTask", e.Exception, terminating: false);
            e.SetObserved();
        };

        // UI(Dispatcher) 스레드 예외 — 유일하게 종료를 실제로 막을 수 있는 지점(Handled=true).
        var app = Application.Current;
        if (app is not null)
            app.DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteFatal("Dispatcher", e.Exception, terminating: false);
        // UI 스레드 예외로 앱을 죽이지 않는다 — 소비자 앱은 크래시보다 유지가 낫다. 단, 상태 일관성 위험이
        // 있으므로 fatal.log에 반드시 남겨 사후 진단이 가능하게 한다(향후 토스트 노출 검토 — v1 수용).
        e.Handled = true;
    }

    private static void WriteFatal(string source, Exception? ex, bool terminating)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Multiplatform-Downloader", "logs");
            Directory.CreateDirectory(dir);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [FATAL:{source}] terminating={terminating} {ex}"
                + Environment.NewLine;
            // 다중 프로세스 동시 기록 시 공유 위반을 완화(실패해도 무해).
            using var fs = new FileStream(Path.Combine(dir, "fatal.log"),
                FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var sw = new StreamWriter(fs);
            sw.Write(line);
        }
        catch { /* 로깅 실패는 무시 */ }
    }
}

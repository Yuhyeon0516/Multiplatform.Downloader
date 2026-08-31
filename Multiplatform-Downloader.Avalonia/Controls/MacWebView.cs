using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Multiplatform_Downloader.Avalonia.Interop;
using Multiplatform_Downloader.Core.Net;

namespace Multiplatform_Downloader.Avalonia.Controls;

/// <summary>
/// WKWebView를 NativeControlHost로 임베드한다(macOS 전용) — 로그인 창(FR-L3)·인앱 플레이어(§9)의 뷰 엔진.
/// 기본 WKWebsiteDataStore(영속)를 쓰므로 로그인 세션이 앱 재시작 후에도 유지된다(WPF WebView2 프로필 대응).
/// </summary>
public sealed class MacWebView : NativeControlHost
{
    private IntPtr _webView;
    private string? _pendingUrl;
    private (string Html, string ReadRoot)? _pendingHtml;

    public bool IsReady => _webView != IntPtr.Zero;

    /// <summary>네이티브 뷰 생성 실패 시 사유(성공 시 null).</summary>
    public string? LastError { get; private set; }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsMacOS())
            return base.CreateNativeControlCore(parent);
        try
        {
            // WebKit 프레임워크를 먼저 로드해야 WKWebView 클래스가 등록된다
            NativeLibrary.Load("/System/Library/Frameworks/WebKit.framework/WebKit");

            var config = ObjC.Send(ObjC.Alloc("WKWebViewConfiguration"), ObjC.Sel("init"));
            _webView = ObjC.Send(ObjC.Alloc("WKWebView"),
                ObjC.Sel("initWithFrame:configuration:"),
                new ObjC.CGRect(0, 0, 100, 100), config);

            if (_pendingUrl is not null)
            {
                Navigate(_pendingUrl);
                _pendingUrl = null;
            }
            if (_pendingHtml is { } html)
            {
                LoadHtmlFile(html.Html, html.ReadRoot);
                _pendingHtml = null;
            }
            return new PlatformHandle(_webView, "NSView");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _webView = IntPtr.Zero;
            return base.CreateNativeControlCore(parent);
        }
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_webView != IntPtr.Zero)
        {
            ObjC.Send(_webView, ObjC.Sel("release"));
            _webView = IntPtr.Zero;
        }
        else
        {
            base.DestroyNativeControlCore(control);
        }
    }

    /// <summary>URL 내비게이트 — 뷰 생성 전 호출되면 생성 시점에 재시도한다.</summary>
    public void Navigate(string url)
    {
        if (_webView == IntPtr.Zero)
        {
            _pendingUrl = url;
            return;
        }
        var nsUrl = ObjC.Send(ObjC.objc_getClass("NSURL"), ObjC.Sel("URLWithString:"), ObjC.NSString(url));
        if (nsUrl == IntPtr.Zero)
            return;
        var request = ObjC.Send(ObjC.objc_getClass("NSURLRequest"), ObjC.Sel("requestWithURL:"), nsUrl);
        ObjC.Send(_webView, ObjC.Sel("loadRequest:"), request);
    }

    /// <summary>임시 HTML 파일을 만들어 로드한다(플레이어용). readRoot 이하 file:// 접근을 허용한다.</summary>
    public void LoadHtmlFile(string html, string readRoot = "/")
    {
        if (_webView == IntPtr.Zero)
        {
            _pendingHtml = (html, readRoot);
            return;
        }
        var htmlPath = Path.Combine(Path.GetTempPath(), $"mpdl-player-{Guid.NewGuid():N}.html");
        File.WriteAllText(htmlPath, html);

        var fileUrl = ObjC.Send(ObjC.objc_getClass("NSURL"), ObjC.Sel("fileURLWithPath:"), ObjC.NSString(htmlPath));
        var rootUrl = ObjC.Send(ObjC.objc_getClass("NSURL"), ObjC.Sel("fileURLWithPath:"), ObjC.NSString(readRoot));
        ObjC.Send(_webView, ObjC.Sel("loadFileURL:allowingReadAccessToURL:"), fileUrl, rootUrl);
    }

    // ── 쿠키 열람(FR-L4) — WKHTTPCookieStore.getAllCookies → CookieRecord ──

    private sealed class CookieRequest
    {
        public TaskCompletionSource<IReadOnlyList<CookieRecord>> Tcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>프로필의 모든 쿠키를 반환한다(도메인 무제한 — YouTube 로그인은 google.com 쿠키도 필요).</summary>
    public unsafe Task<IReadOnlyList<CookieRecord>> GetAllCookiesAsync()
    {
        if (_webView == IntPtr.Zero)
            return Task.FromResult<IReadOnlyList<CookieRecord>>([]);

        var config = ObjC.Send(_webView, ObjC.Sel("configuration"));
        var dataStore = ObjC.Send(config, ObjC.Sel("websiteDataStore"));
        var cookieStore = ObjC.Send(dataStore, ObjC.Sel("httpCookieStore"));

        var request = new CookieRequest();
        var handle = GCHandle.Alloc(request);
        var block = ObjC.CreateBlock(&OnCookies, handle);
        try
        {
            ObjC.Send(cookieStore, ObjC.Sel("getAllCookies:"), block);
        }
        finally
        {
            // WebKit이 호출 중 블록을 힙 복사하므로 원본은 즉시 해제해도 된다
            Marshal.FreeHGlobal(block);
        }
        return request.Tcs.Task;
    }

    [UnmanagedCallersOnly]
    private static void OnCookies(IntPtr blockPtr, IntPtr nsArrayCookies)
    {
        var handle = ObjC.BlockContext(blockPtr);
        try
        {
            var request = (CookieRequest)handle.Target!;
            var result = new List<CookieRecord>();
            if (nsArrayCookies != IntPtr.Zero)
            {
                var count = ObjC.SendNUInt(nsArrayCookies, ObjC.Sel("count"));
                for (nuint i = 0; i < count; i++)
                {
                    var cookie = ObjC.SendIndex(nsArrayCookies, ObjC.Sel("objectAtIndex:"), i);
                    var name = ObjC.FromNSString(ObjC.Send(cookie, ObjC.Sel("name"))) ?? string.Empty;
                    var value = ObjC.FromNSString(ObjC.Send(cookie, ObjC.Sel("value"))) ?? string.Empty;
                    var domain = ObjC.FromNSString(ObjC.Send(cookie, ObjC.Sel("domain"))) ?? string.Empty;
                    var path = ObjC.FromNSString(ObjC.Send(cookie, ObjC.Sel("path"))) ?? "/";
                    var secure = ObjC.SendBool(cookie, ObjC.Sel("isSecure"));
                    var expires = ObjC.Send(cookie, ObjC.Sel("expiresDate"));
                    long expiresUnix = 0; // 세션 쿠키 = 0 (cookies.txt 규약)
                    if (expires != IntPtr.Zero)
                    {
                        var seconds = ObjC.SendDouble(expires, ObjC.Sel("timeIntervalSince1970"));
                        if (seconds > 0)
                            expiresUnix = (long)seconds;
                    }
                    result.Add(new CookieRecord(domain, path, secure, expiresUnix, name, value));
                }
            }
            request.Tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            try { ((CookieRequest)handle.Target!).Tcs.TrySetException(ex); } catch { /* 무시 */ }
        }
        finally
        {
            handle.Free();
        }
    }
}

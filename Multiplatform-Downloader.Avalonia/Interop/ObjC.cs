using System.Runtime.InteropServices;

namespace Multiplatform_Downloader.Avalonia.Interop;

/// <summary>
/// Objective-C 런타임 최소 브리지 — WKWebView 임베드(로그인/플레이어)에 필요한 만큼만.
/// 별도 바인딩 라이브러리 없이 objc_msgSend P/Invoke로 호출한다. macOS 전용.
/// </summary>
internal static partial class ObjC
{
    private const string LibObjC = "/usr/lib/libobjc.dylib";

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr objc_getClass(string name);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr sel_registerName(string name);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr Send(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg0);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg0, IntPtr arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr Send(IntPtr receiver, IntPtr selector, CGRect frame, IntPtr arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static partial nuint SendNUInt(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendIndex(IntPtr receiver, IntPtr selector, nuint index);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SendBool(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static partial double SendDouble(IntPtr receiver, IntPtr selector);

    [StructLayout(LayoutKind.Sequential)]
    internal struct CGRect
    {
        public double X, Y, Width, Height;
        public CGRect(double x, double y, double w, double h) { X = x; Y = y; Width = w; Height = h; }
    }

    // ── 헬퍼 ──

    internal static IntPtr Sel(string name) => sel_registerName(name);

    internal static IntPtr Alloc(string className) =>
        Send(objc_getClass(className), Sel("alloc"));

    /// <summary>C# 문자열 → NSString (autoreleased).</summary>
    internal static IntPtr NSString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return Send(objc_getClass("NSString"), Sel("stringWithUTF8String:"), utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    /// <summary>NSString → C# 문자열 (null 허용).</summary>
    internal static string? FromNSString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero)
            return null;
        var utf8 = Send(nsString, Sel("UTF8String"));
        return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
    }

    // ── Objective-C 블록(완료 핸들러) ──
    // 캡처 없는 정적 invoke + GCHandle 컨텍스트 1개를 담는 최소 블록 리터럴.
    // WebKit이 _Block_copy로 힙 복사해도 비트 복사라 컨텍스트가 보존된다.

    [StructLayout(LayoutKind.Sequential)]
    internal struct BlockLiteral
    {
        public IntPtr Isa;
        public int Flags;
        public int Reserved;
        public IntPtr Invoke;
        public IntPtr Descriptor;
        public IntPtr Context; // GCHandle
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BlockDescriptor
    {
        public ulong Reserved;
        public ulong Size;
    }

    private static readonly IntPtr _concreteStackBlock = LoadStackBlockIsa();
    private static readonly IntPtr _descriptorPtr = CreateDescriptor();

    private static IntPtr LoadStackBlockIsa()
    {
        var lib = NativeLibrary.Load("/usr/lib/libSystem.dylib");
        return NativeLibrary.GetExport(lib, "_NSConcreteStackBlock");
    }

    private static IntPtr CreateDescriptor()
    {
        var descriptor = new BlockDescriptor { Reserved = 0, Size = (ulong)Marshal.SizeOf<BlockLiteral>() };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<BlockDescriptor>());
        Marshal.StructureToPtr(descriptor, ptr, false);
        return ptr;
    }

    /// <summary>1인자 완료 핸들러 블록을 힙에 만든다. invoke는 (blockPtr, arg) 시그니처의
    /// UnmanagedCallersOnly 함수 포인터. 컨텍스트 GCHandle 해제는 invoke 쪽 책임.</summary>
    internal static unsafe IntPtr CreateBlock(delegate* unmanaged<IntPtr, IntPtr, void> invoke, GCHandle context)
    {
        var block = new BlockLiteral
        {
            Isa = _concreteStackBlock,
            Flags = 0,
            Reserved = 0,
            Invoke = (IntPtr)invoke,
            Descriptor = _descriptorPtr,
            Context = GCHandle.ToIntPtr(context),
        };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<BlockLiteral>());
        Marshal.StructureToPtr(block, ptr, false);
        return ptr;
    }

    internal static GCHandle BlockContext(IntPtr blockPtr)
    {
        var block = Marshal.PtrToStructure<BlockLiteral>(blockPtr);
        return GCHandle.FromIntPtr(block.Context);
    }
}

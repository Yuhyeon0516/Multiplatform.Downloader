using Microsoft.Win32;
using System;

namespace Multiplatform_Downloader.Services;

/// <summary>
/// <c>mpdl://</c> 커스텀 프로토콜을 HKCU에 등록한다(FR-08). 관리자 권한 불필요.
/// 스키마를 주입 가능하게 해 테스트에서 임시 스키마로 검증한다.
/// </summary>
public sealed class ProtocolRegistrar
{
    private readonly string _scheme;

    public ProtocolRegistrar(string scheme = "mpdl")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        _scheme = scheme;
    }

    public void Register()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return;

        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{_scheme}");
        key.SetValue(string.Empty, $"URL:{_scheme} Protocol");
        key.SetValue("URL Protocol", string.Empty);
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue(string.Empty, $"\"{exe}\" \"%1\"");
    }

    public void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{_scheme}", throwOnMissingSubKey: false);
    }

    public bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{_scheme}");
        return key is not null;
    }
}

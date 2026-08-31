using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Multiplatform_Downloader.Avalonia.Mvvm;

/// <summary>
/// Caliburn.Micro 호환 최소 INPC 베이스 — WPF 헤드의 ViewModel 코드를 거의 그대로
/// 이식하기 위해 NotifyOfPropertyChange 시그니처를 유지한다.
/// </summary>
public abstract class PropertyChangedBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>빈 문자열을 넘기면 INPC 규약대로 "모든 속성 변경"으로 처리된다.</summary>
    public void NotifyOfPropertyChange([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
}

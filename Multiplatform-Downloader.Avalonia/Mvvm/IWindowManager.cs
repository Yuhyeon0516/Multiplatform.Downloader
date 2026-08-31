namespace Multiplatform_Downloader.Avalonia.Mvvm;

/// <summary>Caliburn.Micro IWindowManager 호환 축소판 — UpdateCoordinator 등 이식 코드가 그대로 쓴다.</summary>
public interface IWindowManager
{
    /// <summary>모달 다이얼로그 — VM 이름 규약(XxxViewModel→XxxView)으로 창을 만들어 띄운다.</summary>
    Task<bool?> ShowDialogAsync(object viewModel);

    /// <summary>비모달 창.</summary>
    Task ShowWindowAsync(object viewModel);
}

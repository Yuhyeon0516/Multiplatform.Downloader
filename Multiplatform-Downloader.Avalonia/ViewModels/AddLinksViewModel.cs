using Multiplatform_Downloader.Avalonia.Mvvm;
using Multiplatform_Downloader.Core.Queue;

namespace Multiplatform_Downloader.Avalonia.ViewModels;

/// <summary>일괄 추가 다이얼로그(WF-02, FR-05) — WPF 헤드 이식(무수정).</summary>
internal sealed class AddLinksViewModel : Screen
{
    private readonly BatchUrlParser _parser;
    private string _linksText = string.Empty;

    public AddLinksViewModel(BatchUrlParser parser)
    {
        _parser = parser;
        DisplayName = "여러 링크 한 번에 추가";
    }

    public string? Result { get; private set; }

    public string LinksText
    {
        get => _linksText;
        set
        {
            _linksText = value;
            NotifyOfPropertyChange();
            UpdatePreview();
        }
    }

    public string PreviewSummary { get; private set; } = "URL을 입력하세요.";
    public bool CanConfirm { get; private set; }

    private void UpdatePreview()
    {
        var parsed = _parser.Parse(LinksText);
        CanConfirm = parsed.Valid.Count > 0;
        PreviewSummary = parsed.Valid.Count == 0 && parsed.Rejected.Count == 0
            ? "URL을 입력하세요."
            : $"유효 {parsed.Valid.Count}건 · 제외 {parsed.Rejected.Count}건";
        NotifyOfPropertyChange(nameof(PreviewSummary));
        NotifyOfPropertyChange(nameof(CanConfirm));
    }

    public async Task Confirm()
    {
        Result = LinksText;
        await TryCloseAsync(true);
    }

    public async Task Cancel()
    {
        Result = null;
        await TryCloseAsync(false);
    }
}

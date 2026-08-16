namespace Multiplatform_Downloader.Core.Queue;

/// <summary>실패 원인 분류(FR-13 지원 계약·NFR-06). UI가 사용자에게 맞는 안내를 하도록 구분한다.</summary>
public enum ErrorCategory
{
    Unknown,
    LoginRequired,
    Captcha,
    GeoBlocked,
    UnsupportedContent,
    Network,
    EngineFailure,
}

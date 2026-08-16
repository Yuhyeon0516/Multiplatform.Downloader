namespace Multiplatform_Downloader.Core.Media;

/// <summary>스니핑된 이미지 종류(FR-D1.3). <see cref="NotImage"/>는 HTML 차단 페이지 등 비이미지.</summary>
public enum SniffedImageKind
{
    NotImage,
    Jpeg,
    Png,
    Gif,
    Bmp,
    /// <summary>WPF 기본 디코더 미지원 — 변환 폴백 필요(FR-D1.2).</summary>
    WebP,
}

/// <summary>
/// 바이트 매직 시그니처로 이미지 종류를 판정한다(FR-D1.3).
/// 실측 근거: rednote 정적 .png URL이 403 <c>&lt;!DOCTYPE HTML</c>을 반환 — 확장자/Content-Type만으로는 판정 불가.
/// </summary>
public static class ImageSniffer
{
    public static SniffedImageKind Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return SniffedImageKind.Jpeg;
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return SniffedImageKind.Png;
        if (bytes.Length >= 4 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == '8')
            return SniffedImageKind.Gif;
        if (bytes.Length >= 2 && bytes[0] == 'B' && bytes[1] == 'M')
            return SniffedImageKind.Bmp;
        if (bytes.Length >= 12
            && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
            && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
            return SniffedImageKind.WebP;
        return SniffedImageKind.NotImage;
    }

    /// <summary>WPF <c>BitmapImage</c>가 변환 없이 디코드할 수 있는 종류인지.</summary>
    public static bool IsWpfDecodable(SniffedImageKind kind)
        => kind is SniffedImageKind.Jpeg or SniffedImageKind.Png or SniffedImageKind.Gif or SniffedImageKind.Bmp;
}

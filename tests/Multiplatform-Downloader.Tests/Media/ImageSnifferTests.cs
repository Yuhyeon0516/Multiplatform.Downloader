using Multiplatform_Downloader.Core.Media;
using System.Text;

namespace Multiplatform_Downloader.Tests.Media;

/// <summary>FR-D1.3: 실측 매직바이트(JPEG JFIF·RIFF/WEBP·HTML 차단 페이지)를 픽스처로 검증.</summary>
public class ImageSnifferTests
{
    [Fact]
    public void should_sniff_jpeg_from_jfif_magic()
    {
        // 실측: IG 썸네일 ff d8 ff e0 ... 'JFIF'
        byte[] bytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F'];
        Assert.Equal(SniffedImageKind.Jpeg, ImageSniffer.Sniff(bytes));
        Assert.True(ImageSniffer.IsWpfDecodable(SniffedImageKind.Jpeg));
    }

    [Fact]
    public void should_sniff_webp_from_riff_magic()
    {
        // 실측: XHS 썸네일 52 49 46 46 .... 57 45 42 50 (RIFF....WEBP)
        byte[] bytes = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 0x24, 0x47, 0x00, 0x00,
                        (byte)'W', (byte)'E', (byte)'B', (byte)'P', (byte)'V', (byte)'P', (byte)'8', (byte)'X'];
        Assert.Equal(SniffedImageKind.WebP, ImageSniffer.Sniff(bytes));
        Assert.False(ImageSniffer.IsWpfDecodable(SniffedImageKind.WebP)); // 변환 폴백 필요
    }

    [Fact]
    public void should_reject_html_block_page_as_not_image()
    {
        // 실측: rednote 정적 .png가 403 '<!DOCTYPE HTML' 반환
        var bytes = Encoding.ASCII.GetBytes("<!DOCTYPE HTML PUBLIC ...");
        Assert.Equal(SniffedImageKind.NotImage, ImageSniffer.Sniff(bytes));
    }

    [Fact]
    public void should_sniff_png_gif_bmp()
    {
        Assert.Equal(SniffedImageKind.Png, ImageSniffer.Sniff([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A]));
        Assert.Equal(SniffedImageKind.Gif, ImageSniffer.Sniff([(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a']));
        Assert.Equal(SniffedImageKind.Bmp, ImageSniffer.Sniff([(byte)'B', (byte)'M', 0x36, 0x00]));
    }

    [Fact]
    public void should_return_not_image_for_short_or_empty()
    {
        Assert.Equal(SniffedImageKind.NotImage, ImageSniffer.Sniff([]));
        Assert.Equal(SniffedImageKind.NotImage, ImageSniffer.Sniff([0xFF]));
    }
}

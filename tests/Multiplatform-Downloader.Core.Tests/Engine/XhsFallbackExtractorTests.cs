using Multiplatform_Downloader.Core.Engine;

namespace Multiplatform_Downloader.Tests.Engine;

public class XhsFallbackExtractorTests
{
    private readonly XhsFallbackExtractor _sut = new();

    // ⚠ 실제 XHS 페이지 구조 추정 픽스처 — VER-08 검증 후 실제 캡처로 교체 필요
    private const string SampleHtml = """
    <html><body>
    <script>window.__INITIAL_STATE__={"note":{"noteDetailMap":{"abc123":{"note":{
      "title":"XHS 여행 영상",
      "cover":"https://sns-img.xhscdn.com/cover.jpg",
      "video":{"media":{"stream":{"h264":[{"masterUrl":"https://sns-video.xhscdn.com/stream/abc.mp4"}]}}}
    }}}}}</script>
    </body></html>
    """;

    [Fact]
    public void should_extract_stream_url_from_initial_state()
    {
        var result = _sut.Extract(SampleHtml);
        Assert.Equal("https://sns-video.xhscdn.com/stream/abc.mp4", result.StreamUrl);
    }

    [Fact]
    public void should_extract_title_and_thumbnail()
    {
        var result = _sut.Extract(SampleHtml);
        Assert.Equal("XHS 여행 영상", result.Title);
        Assert.Equal("https://sns-img.xhscdn.com/cover.jpg", result.ThumbnailUrl);
    }

    [Fact]
    public void should_throw_when_initial_state_missing()
    {
        Assert.Throws<XhsExtractionException>(() => _sut.Extract("<html><body>no state here</body></html>"));
    }

    [Fact]
    public void should_throw_when_no_stream_url()
    {
        var html = """<script>window.__INITIAL_STATE__={"note":{"title":"제목만"}}</script>""";
        Assert.Throws<XhsExtractionException>(() => _sut.Extract(html));
    }

    [Fact]
    public void should_fallback_to_mp4_url_when_no_master_url_key()
    {
        var html = """<script>window.__INITIAL_STATE__={"data":{"link":"https://cdn.xhscdn.com/v/xyz.mp4"}}</script>""";
        var result = _sut.Extract(html);
        Assert.Equal("https://cdn.xhscdn.com/v/xyz.mp4", result.StreamUrl);
    }

    [Fact]
    public void should_scope_to_note_not_generic_page_title()
    {
        // 실측 재현(2026-08-02): 페이지 상단에 검색 UI "title":"搜索小红书" 가 먼저 나오고,
        // 실제 노트는 noteDetailMap.<id>.note 안에 있으며 커버는 imageList[0].urlDefault 다.
        // 추출기는 generic 값이 아니라 노트 스코프에서 제목·커버를 잡아야 한다.
        const string html = """
        <script>window.__INITIAL_STATE__={
          "search":{"title":"搜索小红书"},
          "note":{"noteDetailMap":{"6a617ad2":{"note":{
            "type":"normal","title":"游泳女教练，想学游泳的快来","desc":"",
            "imageList":[{"urlDefault":"http://sns-webpic-qc.xhscdn.com/cover.jpg","stream":{"h264":[{"masterUrl":"https://sns-video.xhscdn.com/live/abc.mp4"}]}}]
          }}}}
        }</script>
        """;
        var result = _sut.Extract(html);
        Assert.Equal("游泳女教练，想学游泳的快来", result.Title);          // 검색 UI 제목 아님
        Assert.Equal("http://sns-webpic-qc.xhscdn.com/cover.jpg", result.ThumbnailUrl); // imageList 커버
        Assert.Equal("https://sns-video.xhscdn.com/live/abc.mp4", result.StreamUrl);    // 라이브포토 stream
    }

    [Fact]
    public void should_parse_when_state_contains_js_undefined()
    {
        // 실측(2026-08-02): XHS __INITIAL_STATE__ 는 값에 JS undefined 를 포함 → JSON 파서 'u' 실패.
        // 값 위치 undefined 를 null 로 정규화해 파싱 성공해야 한다.
        var html = """<script>window.__INITIAL_STATE__={"note":{"cover":undefined,"masterUrl":"https://sns-video.xhscdn.com/stream/a.mp4","backupUrls":undefined},"user":undefined}</script>""";
        var result = _sut.Extract(html);
        Assert.Equal("https://sns-video.xhscdn.com/stream/a.mp4", result.StreamUrl);
    }
}

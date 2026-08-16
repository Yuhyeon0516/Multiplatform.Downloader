// 샤샤룽 다운로더 — 단일 콘텐츠 URL 판정 패턴 (FR-P2)
// 근거: 2026-08-02 플랫폼 실측 프로브(yt-dlp 실호출로 검증된 URL 형태만 등재).
// 배지 표시("다운로드 가능 모드")와 아이콘 클릭 전송 판정에 사용한다.
// service worker에서 importScripts('content-patterns.js') 로 로드.

const MPDL_CONTENT_PATTERNS = [
  // YouTube
  /^https?:\/\/(www\.|m\.)?youtube\.com\/(watch\?(.*&)?v=[\w-]+|shorts\/[\w-]+|live\/[\w-]+)/i,
  /^https?:\/\/youtu\.be\/[\w-]+/i,
  // Instagram (사진 게시물도 매치되지만 앱이 '영상 없음'을 안내 — 실측 분류기 존재)
  /^https?:\/\/(www\.)?(instagram\.com|instagr\.am)\/([\w.]+\/)?(p|reel|tv)\/[\w-]+/i,
  // TikTok (+ 단축: vm/vt 링크는 개별 영상 공유 링크)
  /^https?:\/\/(www\.)?tiktok\.com\/@[\w.\-]+\/video\/\d+/i,
  /^https?:\/\/v[mt]\.tiktok\.com\/[\w-]+/i,
  // Xiaohongshu / rednote
  /^https?:\/\/(www\.)?(xiaohongshu|rednote)\.com\/(explore|discovery\/item)\/[0-9a-f]+/i,
  /^https?:\/\/xhslink\.com\/[\w/]+/i,
  // Facebook (실측: /<page>/videos/<id>는 앱이 watch?v=로 정규화 폴백)
  /^https?:\/\/(www\.|m\.|web\.)?facebook\.com\/(watch\/?\?(.*&)?v=\d+|reel\/\d+|[^/?#]+\/videos\/(?:[^/?#]+\/)?\d+|video\.php\?v=\d+|share\/[vr]\/[\w-]+)/i,
  /^https?:\/\/fb\.watch\/[\w-]+/i,
  // X / Twitter
  /^https?:\/\/(www\.|mobile\.)?(x|twitter)\.com\/([\w]{1,15}\/status(es)?|i\/web\/status|i\/status)\/\d+/i,
  // Douyin (실측: 쿠키 필요 — 판정과 무관, v.douyin 단축은 개별 영상 공유. 모달 URL은 앱이 /video/로 정규화)
  /^https?:\/\/(www\.)?douyin\.com\/(video|note)\/\d+/i,
  /^https?:\/\/(www\.)?douyin\.com\/[^?#]*[?&]modal_id=\d+/i,
  /^https?:\/\/v\.douyin\.com\/[\w-]+/i,
  /^https?:\/\/(www\.)?iesdouyin\.com\/share\/video\/\d+/i,
  // Reddit
  /^https?:\/\/(www\.|old\.|new\.|np\.)?reddit\.com\/(r\/[^/]+\/(comments\/[a-z0-9]+|s\/[A-Za-z0-9]+)|comments\/[a-z0-9]+|user\/[^/]+\/comments\/[a-z0-9]+)/i,
  /^https?:\/\/v\.redd\.it\/[a-z0-9]+/i,
  /^https?:\/\/(www\.)?redd\.it\/[a-z0-9]+/i,
  // Pinterest (지역 서브도메인/슬러그 포함 — 실측)
  /^https?:\/\/([\w-]+\.)?pinterest\.([a-z.]{2,6})\/pin\/[\w-]*\d+/i,
  // Threads (자체 폴백 추출 — 실측 FR-N1.8)
  /^https?:\/\/(www\.)?threads\.(net|com)\/@[\w.]+\/post\/[\w-]+/i,
  /^https?:\/\/(www\.)?threads\.(net|com)\/t\/[\w-]+/i,
];

// eslint-disable-next-line no-unused-vars
function mpdlIsSingleContent(url) {
  if (!url || !/^https?:/i.test(url)) return false;
  return MPDL_CONTENT_PATTERNS.some(p => p.test(url));
}

// Node 시뮬레이터에서 재사용(확장 SW에서는 무시됨)
if (typeof module !== "undefined") {
  module.exports = { mpdlIsSingleContent, MPDL_CONTENT_PATTERNS };
}

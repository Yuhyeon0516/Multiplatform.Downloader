// 샤샤룽 다운로더 — Chrome MV3 백그라운드 서비스 워커 (FR-08)
//
// 컨텍스트 메뉴 클릭 시 커스텀 프로토콜 mpdl://add?url=<encoded> 를 연다.
// 구현 제약(PRD FR-08, NFR):
//   - contextMenus.onClicked 핸들러에서 chrome.tabs.update를 "동기적으로" 호출한다.
//     (비동기 경유 시 사용자 제스처 컨텍스트가 소실되어 외부 프로토콜 실행이 차단될 수 있음)
//   - 새 탭/새 창 생성이 아니라 현재 탭을 갱신한다. 현재 탭의 오리진이 유지되어
//     Chrome의 "항상 허용" 선택이 플랫폼 도메인 단위로 기억된다.
//   - 링크 컨텍스트는 linkUrl, 그 외에는 pageUrl을 사용한다. srcUrl(blob:/CDN 직접 URL)은
//     플랫폼 게시물 URL과 다르므로 v1에서 사용하지 않는다(PRD P2-02).

const MENU_ID = "mpdl-download";

// 단일 콘텐츠 판정 패턴(FR-N2) — 실측 URL 형태만. service worker 전역에 로드.
try { importScripts("content-patterns.js"); } catch (e) { /* 판정 불가 시 배지만 생략 */ }

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({
    id: MENU_ID,
    title: "샤샤룽 다운로더로 다운로드",
    contexts: ["page", "link", "video"],
  });
});

// ── 다운로드 가능 모드 표시(FR-N2.2): 활성 탭이 단일 콘텐츠 페이지면 아이콘을 초록 '다운로드'
//    아이콘으로 교체하고, 아니면 기본 프로그램 아이콘으로 되돌린다(사용자 요청). ──
const ICON_DEFAULT = { 16: "icon16.png", 48: "icon48.png", 128: "icon128.png" };
const ICON_DOWNLOAD = { 16: "icon16-dl.png", 48: "icon48-dl.png", 128: "icon128-dl.png" };

function updateBadge(tabId, url) {
  const downloadable =
    typeof mpdlIsSingleContent === "function" && mpdlIsSingleContent(url);
  if (downloadable) {
    chrome.action.setIcon({ tabId, path: ICON_DOWNLOAD });
    chrome.action.setTitle({ tabId, title: "이 영상을 샤샤룽 다운로더로 받기" });
  } else {
    chrome.action.setIcon({ tabId, path: ICON_DEFAULT });
    chrome.action.setTitle({ tabId, title: "현재 페이지를 샤샤룽 다운로더로 보내기" });
  }
}

// SPA 내비게이션(유튜브/인스타/X history push) 포함 — url 변경 시마다 갱신(FR-N2.4)
chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.url || changeInfo.status === "complete") {
    updateBadge(tabId, changeInfo.url || (tab && tab.url) || "");
  }
});

chrome.tabs.onActivated.addListener(({ tabId }) => {
  chrome.tabs.get(tabId, (tab) => {
    if (!chrome.runtime.lastError && tab) updateBadge(tabId, tab.url || "");
  });
});

// 툴바 아이콘 클릭(FR-D4.1): 현재 탭 URL을 전송 — 우클릭 불필요.
// 실측 근거: TikTok은 영상 요소의 우클릭을 preventDefault로 차단해 컨텍스트 메뉴가 뜨지 않는다.
//
// 틱톡 피드 보정(실측: 피드에서 주소창이 홈 URL(tiktok.com/ko-KR/)로 남는 경우가 있음):
// URL에 /video/ 가 없으면 페이지에서 "현재 화면의 영상 URL"을 추출 시도:
//   canonical → 뷰포트 중앙 video 요소의 조상 id에서 영상 ID(실측: xgwrapper-0-<ID>)
//   → 뷰포트 중앙 /video/ 앵커. 실패 시 탭 URL 그대로 전송(앱이 안내 메시지 표시).
// '@_' 더미 작성자 형식은 yt-dlp가 실제 작성자로 해석함(2026-08-02 실측).
function extractTikTokVideoUrl() {
  if (/\/video\//.test(location.href)) {
    return location.href;
  }
  const canon = document.querySelector('link[rel="canonical"]');
  if (canon && /\/video\//.test(canon.href)) {
    return canon.href;
  }
  const centerY = innerHeight / 2;
  // 피드 화면: 중앙에 재생 중인 video 요소의 조상 id에 영상 ID가 박혀 있다(실측)
  for (const v of document.querySelectorAll("video")) {
    const r = v.getBoundingClientRect();
    if (r.top <= centerY && r.bottom >= centerY) {
      for (let n = v, depth = 0; n && depth < 10; n = n.parentElement, depth++) {
        const m = (n.id || "").match(/\d{15,}/);
        if (m) {
          return "https://www.tiktok.com/@_/video/" + m[0];
        }
      }
      break;
    }
  }
  // 프로필 그리드 등: 중앙에 가장 가까운 /video/ 링크
  let best = null;
  let bestDist = Infinity;
  for (const a of document.querySelectorAll('a[href*="/video/"]')) {
    const r = a.getBoundingClientRect();
    if (!r.height || r.bottom < 0 || r.top > innerHeight) continue;
    const d = Math.abs((r.top + r.bottom) / 2 - centerY);
    if (d < bestDist) { bestDist = d; best = a.href; }
  }
  return best;
}

// 페이스북 피드 보정: 피드/홈 URL이면(개별 영상 URL이 아니면) 뷰포트 중앙에 가장 가까운
// 영상 permalink 앵커(/reel/<id>, /watch/?v=<id>, /<user>/videos/<id>)를 추출한다. 실패 시 폴백.
function extractFacebookVideoUrl() {
  if (/\/reel\/\d+|\/videos\/\d+|[?&]v=\d+/.test(location.href)) {
    return location.href;
  }
  const centerY = innerHeight / 2;
  let best = null;
  let bestDist = Infinity;
  const sel = 'a[href*="/reel/"], a[href*="/videos/"], a[href*="/watch/?v="], a[href*="watch?v="]';
  for (const a of document.querySelectorAll(sel)) {
    if (!/\/reel\/\d+|\/videos\/\d+|[?&]v=\d+/.test(a.href)) continue;
    const r = a.getBoundingClientRect();
    if (!r.height || r.bottom < 0 || r.top > innerHeight) continue;
    const d = Math.abs((r.top + r.bottom) / 2 - centerY);
    if (d < bestDist) { bestDist = d; best = a.href; }
  }
  return best;
}

chrome.action.onClicked.addListener(async (tab) => {
  let target = tab && tab.url;
  if (!target || !/^https?:/i.test(target) || tab.id === undefined) {
    return;
  }
  const host = new URL(target).hostname;

  // 틱톡 피드: /video/ 없으면 중앙 영상 추출
  if (/(^|\.)tiktok\.com/i.test(host) && !/\/video\//.test(target)) {
    target = (await tryExtract(tab.id, extractTikTokVideoUrl)) || target;
  }
  // 페이스북 피드: 개별 영상 URL이 아니면 중앙 영상 permalink 추출
  else if (/(^|\.)facebook\.com/i.test(host) &&
           !/\/reel\/\d+|\/videos\/\d+|[?&]v=\d+|fb\.watch/.test(target)) {
    target = (await tryExtract(tab.id, extractFacebookVideoUrl)) || target;
  }

  chrome.tabs.update(tab.id, { url: "mpdl://add?url=" + encodeURIComponent(target) });
});

async function tryExtract(tabId, func) {
  try {
    const [res] = await chrome.scripting.executeScript({ target: { tabId }, func });
    return res && res.result;
  } catch (e) {
    return null; // 권한/페이지 상태 실패 → 폴백
  }
}

chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId !== MENU_ID) {
    return;
  }

  // 링크 컨텍스트 우선, 아니면 현재 페이지 URL (srcUrl 직접 미디어 URL은 제외)
  const target = info.linkUrl || info.pageUrl || (tab && tab.url);
  if (!target || !tab || tab.id === undefined) {
    return;
  }

  const protocolUrl = "mpdl://add?url=" + encodeURIComponent(target);

  // 동기 호출 — 제스처 보존 + 현재 탭 오리진 유지
  chrome.tabs.update(tab.id, { url: protocolUrl });
});

# 샤샤룽 다운로더 (Shyshyroong Downloader)

YouTube · Instagram · TikTok · 샤오홍슈(RedNote) · Threads · Facebook · X(Twitter) · 도우인 · Reddit · Pinterest —
**10개 플랫폼**의 영상을 내려받는 Windows 데스크톱 앱.
카드형 UI로 썸네일·제목·플랫폼·진행률을 보여주고, 여러 URL을 한 번에 등록해 해상도를 골라 받는다.
받은 영상은 **인앱 플레이어로 바로 재생**할 수 있다.

> **개인 사용 전제.** 저작권·각 플랫폼 이용약관을 준수해서 사용해야 한다. [사용 범위·라이선스 고지](#사용-범위--라이선스) 참고.

---

## 주요 기능

| 기능 | 설명 |
|------|------|
| **10개 플랫폼** | YouTube · Instagram · TikTok · 샤오홍슈(xiaohongshu/rednote/xhslink) · Threads · Facebook(fb.watch) · X(twitter) · 도우인(douyin) · Reddit · Pinterest(pin.it) |
| **분석 우선 · 수동 다운로드** | 등록하면 먼저 **분석**(제목·썸네일·해상도)만 하고, 카드의 **[받기]**(또는 **모두 받기 / 선택 받기**)를 눌러야 내려받는다. 설정의 **자동 다운로드**를 켜면 분석 직후 바로 받는다 |
| **해상도 선택** | URL별 사용 가능한 화질 중 선택. 영상만 있는 포맷은 최적 오디오를 자동 병합 |
| **인앱 재생** | 완료 카드의 **[재생]**으로 앱 안에서 바로 재생(전체화면 지원) |
| **로그인 / 봇 확인 해결** | 로그인·봇 확인으로 막힌 항목은 카드의 **[로그인]** → 앱 내 브라우저 창에서 실제 로그인/확인 → 쿠키 자동 저장 → 자동 재시도 (연령 제한 YouTube·도우인 등) |
| **받음/안받음 상태 유지** | 재시작해도 받은 항목은 완료로 복원(다운로드 폴더의 파일 존재 대조). 파일을 지웠으면 "파일 없음" 안내 + [재시도]로 다시 받기 |
| **자체 폴백 추출** | yt-dlp가 못 받는 Threads는 자체 추출기로, 샤오홍슈는 다층 폴백으로 스트림을 직접 파싱해 다운로드 |
| **일괄 등록** | 여러 줄 URL 붙여넣기 → 한 번에 큐 등록(기본 최대 15개) |
| **카드형 진행률 UI** | 썸네일 · 플랫폼 배지 · 실시간 진행률/속도/ETA · 준비 중 스피너 · 상태별 액션(받기/일시정지/재개/취소/재시도/재생/폴더/삭제) |
| **다크/라이트 테마** | 시스템 추종 또는 수동 선택. 확인 대화상자까지 테마 일치 |
| **실패 조각 자동 정리** | 실패·취소 시 남는 `.part`/`.ytdl` 조각 파일을 자동 삭제(기동 시 고아 조각 일괄 정리 포함) |
| **트레이 상주** | 창 닫기 = 트레이로 숨김(설정 가능), 트레이에서 열기/일괄 정지·재개/폴더/완전 종료 |
| **Windows 시작 등록** | 부팅 시 자동 실행(`--minimized`) 옵션 — 인스톨러 체크박스와 연동 |
| **Chrome 확장 연동** | 우클릭 "샤샤룽 다운로더로 다운로드" 또는 **툴바 아이콘 클릭**(단일 영상 페이지에서 초록 다운로드 아이콘으로 변신). TikTok/Facebook 피드에서는 화면 중앙 영상을 자동 인식. 전송하면 앱 창이 앞으로 나타난다 |

---

## 요구 사항

- **Windows 10/11 (x64)**
- **WebView2 런타임** — 로그인 창·인앱 재생에 사용(Win11 기본 포함, 없으면 해당 기능만 안내 후 앱은 계속 동작)
- 번들 엔진: `yt-dlp` · `ffmpeg` · `ffprobe` · `deno` — `tools/`에 포함(별도 설치 불필요)
- 소스 빌드 시에만: .NET 10 SDK (인스톨러는 런타임 자체 포함)

---

## 설치 / 실행

**설치 파일(권장)**: `Installer/Output/ShyshyroongDownloader_Setup_v<버전>.exe` 실행.
이전 버전이 있으면 실행 중인 앱을 종료하고 **구버전 제거 → 신버전 설치**가 진행바로 표시되며,
설정·다운로드 기록·로그인 쿠키는 `%APPDATA%`에 있어 업그레이드 후에도 유지된다.

```powershell
# 소스 빌드
dotnet build Multiplatform-Downloader/Multiplatform-Downloader.csproj -c Release

# 실행
./Multiplatform-Downloader/bin/Release/net10.0-windows/Multiplatform-Downloader.exe
```

Chrome 확장 설치는 [`docs/chrome-extension-guide.md`](docs/chrome-extension-guide.md) 참고.

### macOS 설치

**터미널 한 줄 설치(권장)** — Gatekeeper 경고 없이 설치·실행된다:

```bash
curl -fsSL https://raw.githubusercontent.com/Yuhyeon0516/Multiplatform.Downloader/main/install.sh | bash
```

Apple Silicon(arm64)·Intel(x64)을 자동 감지해 `/Applications`에 설치한다.

**.dmg 설치(보조)**: 릴리스의 `ShyshyroongDownloader-macos-<아키텍처>.dmg`를 받아 앱을 Applications로 끌어놓는다.
현재 무서명(ad-hoc) 배포라 **브라우저로 내려받은 경우** 첫 실행 시
시스템 설정 → 개인정보 보호 및 보안 → **"그래도 열기"**를 한 번 눌러야 한다.

```bash
# macOS 소스 빌드/실행 (Avalonia 헤드)
dotnet run --project Multiplatform-Downloader.Avalonia
# 배포 번들 빌드 (.app + .tar.gz + .dmg)
./Installer/macos/make-app.sh osx-arm64
```

> macOS용 번들 엔진은 `Multiplatform-Downloader.Avalonia/tools/`에 확장자 없는
> `yt-dlp`/`ffmpeg`/`ffprobe`/`deno`를 배치한다(CI가 자동 수행).

---

## 사용법

1. 상단 입력창에 영상 URL을 붙여넣고 **추가**(여러 줄이면 **일괄 추가**) — 또는 Chrome에서 우클릭/확장 아이콘으로 전송.
2. 카드가 **분석 중 → 대기(Ready)** 로 바뀌며 썸네일·제목·해상도가 표시된다.
3. 카드의 **[받기]**(또는 상단 **모두 받기 / 선택 받기**)를 눌러 내려받는다.
   설정에서 **자동 다운로드**를 켜면 이 단계가 생략된다.
4. 로그인·봇 확인으로 막히면 카드에 **[로그인]** 버튼이 나타난다 — 창에서 로그인하고 [완료]를 누르면 자동 재시도.
5. 완료 후에는 **[재생]**(인앱 플레이어) · **폴더** · **삭제**가 가능하다. 진행 중에는 **일시정지/재개/취소**.
6. 앱을 껐다 켜도 받은 항목은 완료로 남는다(폴더에서 파일을 지웠으면 "파일 없음" 표시 + [재시도]).
7. 다운로드 폴더·동시 수·기본 화질·테마·자동 다운로드는 **설정**에서 변경한다.

---

## 아키텍처

```
Multiplatform-Downloader.Core   순수 로직(net10.0, WPF 비의존, 테스트 대상)
  Engine     yt-dlp 인자/출력 파서 · 오류 분류기 · 진행률 매퍼 · Threads/샤오홍슈 폴백 추출 · 조각 정리
  Queue      다운로드 큐 오케스트레이터 · 상태머신 · 영속화(완료 포함 v2, 재시작 폴더 대조 복원)
  Platforms  10개 플랫폼 감지 · URL 정규화(FB watch/도우인 modal 등)
  Net        SSRF 방어 가드(DNS 리바인딩 포함) · cookies.txt 직렬화
  Ipc        단일 인스턴스 · mpdl:// 프로토콜 파서 · Named Pipe
  Settings   JSON 설정(원자적 저장·손상 복구)
Multiplatform-Downloader        WPF(.NET 10) + Caliburn.Micro 4 + Autofac 8
  ViewModels/Views  Shell(카드 큐) · Player(인앱 재생) · LoginBrowser(로그인/봇확인) ·
                    Settings · AddLinks · ConfirmDialog · About · Splash
  Services          트레이 · 시작 등록 · mpdl:// 프로토콜 · 테마 · 토스트
chrome-extension/               Chrome MV3 확장(우클릭·아이콘 클릭 → mpdl://, 다운로드 가능 모드 아이콘)
Installer/                      Inno Setup(자체 포함 배포 + 자동 업그레이드)
tools/                          번들 엔진(yt-dlp/ffmpeg/ffprobe/deno)
```

---

## 개발 / 테스트

```powershell
# 단위·통합 테스트 (xUnit, 338개)
dotnet test tests/Multiplatform-Downloader.Tests/Multiplatform-Downloader.Tests.csproj -v minimal

# 포맷 검사
dotnet format
```

통합 테스트에서 IP 밴 방지를 위해 **테스트 전용** 프록시를 쓸 수 있다(선택). 프로덕션 앱 기능이 아니다 —
설정은 [`docs/testing-guide.md`](docs/testing-guide.md)와 [`.env.example`](.env.example) 참고.

---

## 사용 범위 / 라이선스

- 이 앱은 **개인적·합법적 용도**로만 사용한다. 저작권이 있는 콘텐츠의 무단 배포·상업적 이용을 하지 않는다.
- 각 플랫폼의 **이용약관과 저작권법**을 준수할 책임은 사용자에게 있다.
- 번들 서드파티 도구는 각자의 라이선스를 따른다:
  - **yt-dlp** — [Unlicense](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE)
  - **FFmpeg / ffprobe** — LGPL/GPL ([ffmpeg.org/legal.html](https://ffmpeg.org/legal.html))
  - **Deno** — MIT
- 자세한 고지는 [`docs/legal-notice.md`](docs/legal-notice.md) 참고.

---

*상세 문서: [`docs/Manual.md`](docs/Manual.md) · 변경 이력: [`CHANGELOG.md`](CHANGELOG.md) · 문서 색인: [`docs/INDEX.md`](docs/INDEX.md)*

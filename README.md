# 샤샤룽 다운로더 (Shashalung Downloader)

YouTube · Instagram · TikTok · 샤오홍슈(RedNote) 영상을 내려받는 Windows 데스크톱 앱.
카드형 UI로 썸네일·제목·플랫폼·진행률을 보여주고, 여러 URL을 한 번에 등록해 해상도를 골라 다운로드한다.

> **개인 사용 전제.** 저작권·각 플랫폼 이용약관을 준수해서 사용해야 한다. [사용 범위·라이선스 고지](#사용-범위--라이선스) 참고.

---

## 주요 기능

| 기능 | 설명 |
|------|------|
| **4개 플랫폼** | YouTube · Instagram · TikTok · 샤오홍슈(xiaohongshu / rednote / xhslink) |
| **분석 우선 · 수동 다운로드** | 등록하면 먼저 **분석**(제목·썸네일·해상도)만 하고, **"다운로드" 버튼**을 눌러야 실제 내려받는다. `모두 받기`로 일괄 시작 |
| **해상도 선택** | URL별 사용 가능한 화질 중 선택. 영상만 있는 포맷은 최적 오디오를 자동 병합 |
| **일괄 등록** | 여러 줄 URL 붙여넣기 → 한 번에 큐 등록(기본 최대 15개) |
| **카드형 진행률 UI** | 썸네일 · 플랫폼 배지 · 상태색 진행률 · 속도/ETA · 상태별 액션(다운로드/일시정지/재개/취소/재시도/폴더/삭제) |
| **샤오홍슈 다층 폴백** | yt-dlp 실패 시 자체 추출기로 스트림 URL을 직접 파싱해 다운로드(FR-13) |
| **폴더 영속화** | 다운로드 폴더·동시 실행 수·기본 화질·시작 옵션을 저장 |
| **트레이 상주** | 창 닫기 = 트레이로 숨김, 트레이에서 열기/일괄 정지·재개/폴더/완전 종료 |
| **Windows 시작 등록** | 부팅 시 자동 실행(`--minimized`) 옵션 |
| **Chrome 우클릭 연동** | 확장에서 링크/페이지 우클릭 → `mpdl://`로 앱에 전달 → 큐 추가 |

---

## 요구 사항

- **Windows 10/11 (x64)**
- **.NET 10 데스크톱 런타임** (자체 포함 배포 시 불필요)
- 번들 엔진: `yt-dlp` · `ffmpeg` · `ffprobe` · `deno` — `tools/`에 포함되어 실행 시 `PATH` 앞에 추가된다

---

## 설치 / 실행

```powershell
# 빌드
dotnet build Multiplatform-Downloader/Multiplatform-Downloader.csproj -c Release

# 실행
./Multiplatform-Downloader/bin/Release/net10.0-windows/Multiplatform-Downloader.exe
```

Chrome 확장 설치는 [`docs/chrome-extension-guide.md`](docs/chrome-extension-guide.md) 참고.

---

## 사용법

1. 상단 입력창에 영상 URL을 붙여넣고 **추가**(여러 줄이면 **일괄 추가**).
2. 카드가 **분석 중 → 대기(Ready)** 로 바뀌며 썸네일·제목·해상도가 표시된다.
3. 카드의 **다운로드** 버튼(또는 상단 **모두 받기**)을 눌러 내려받는다.
4. 진행 중에는 **일시정지/재개/취소**, 완료 후에는 **폴더/삭제**가 가능하다.
5. 다운로드 폴더·동시 수·기본 화질은 **설정**에서 변경한다.

---

## 아키텍처

```
Multiplatform-Downloader.Core   순수 로직(net10.0, WPF 비의존, 테스트 대상)
  Engine    yt-dlp 인자/출력 파서 · 다운로드 엔진 · 샤오홍슈 폴백 · 직접 스트림
  Queue     다운로드 큐 오케스트레이터 · 상태머신 · 영속화
  Net       SSRF 방어 가드(DNS 리바인딩 포함)
  Settings  JSON 설정(원자적 저장·손상 복구)
  ...
Multiplatform-Downloader        WPF(.NET 10) + Caliburn.Micro 4 + Autofac 8
  ViewModels/Views  Shell(카드 큐) · Settings · AddLinks
  Services          트레이 · 시작 등록 · mpdl:// 프로토콜 · 토스트
chrome-extension/               Chrome MV3 확장(우클릭 → mpdl://)
tools/                          번들 엔진(yt-dlp/ffmpeg/ffprobe/deno)
```

---

## 개발 / 테스트

```powershell
# 단위·통합 테스트 (xUnit)
dotnet test tests/Multiplatform-Downloader.Tests/Multiplatform-Downloader.Tests.csproj -v minimal

# 포맷 검사
dotnet format
```

통합 테스트에서 IP 밴 방지를 위해 **테스트 전용** 프록시를 쓸 수 있다(선택). 프로덕션 앱 기능이 아니다 —
설정은 [`docs/testing-guide.md`](docs/testing-guide.md)와 [`.env.example`](.env.example) 참고.

---

## 사용 범위 / 라이선스

- 이 앱은 **개인적·합법적 용도**로만 사용한다. 저작권이 있는 콘텐츠의 무단 배포·상업적 이용을 하지 않는다.
- 각 플랫폼(YouTube/Instagram/TikTok/샤오홍슈)의 **이용약관과 저작권법**을 준수할 책임은 사용자에게 있다.
- 번들 서드파티 도구는 각자의 라이선스를 따른다:
  - **yt-dlp** — [Unlicense](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE)
  - **FFmpeg / ffprobe** — LGPL/GPL ([ffmpeg.org/legal.html](https://ffmpeg.org/legal.html))
  - **Deno** — MIT
- 자세한 고지는 [`docs/legal-notice.md`](docs/legal-notice.md) 참고.

---

*상세 문서: [`docs/Manual.md`](docs/Manual.md) · 문서 색인: [`docs/INDEX.md`](docs/INDEX.md)*

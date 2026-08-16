# 샤샤룽 다운로더 — 설치기 빌드 (Inno Setup)

Windows용 단일 `Setup.exe` 설치 프로그램을 만든다. **self-contained**(.NET 10 런타임 포함)이라
대상 PC에 별도 런타임 설치가 필요 없다. 번들 엔진(yt-dlp/ffmpeg/ffprobe/deno)도 함께 포함된다.

## 사전 준비
- [Inno Setup 6.3+](https://jrsoftware.org/isdl.php) 설치 (`winget install JRSoftware.InnoSetup`)
  - 한글 UI: `Languages\Korean.isl`이 포함된 배포판 사용(최신 배포에 동봉).
- .NET 10 SDK

## 원클릭 빌드
```bat
cd Installer
build.bat            :: csproj <Version> 사용
build.bat 2.9.0      :: 버전 오버라이드
```

산출물: `Installer/Output/ShashalungDownloader_Setup_v2.8.5.0.exe`

## 수동 빌드 (배치가 안 될 때)
```powershell
$proj = "..\Multiplatform-Downloader\Multiplatform-Downloader.csproj"
dotnet publish $proj -c Release -r win-x64 --self-contained true -p:DebugType=None -o publish
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" ShashalungDownloader.iss
```
> ISCC는 `.iss` 파일 위치(Installer/)를 기준으로 `publish\`, `Assets\`, `Output\` 상대경로를 해석한다.

## 구조
```
Installer/
├── ShashalungDownloader.iss   # 메인 스크립트 ([Setup][Files][Icons][Registry])
├── build.bat                  # publish → ISCC 원클릭
├── Assets/
│   ├── app.ico                # 설치기 + 바로가기 아이콘
│   ├── wizard-large.png       # 마법사 좌측 세로 배너 (docs/marketing에서 생성)
│   └── wizard-small.png       # 내부 페이지 우상단 로고
├── publish/                   # dotnet publish 출력 (gitignore)
└── Output/                    # 완성된 Setup.exe (gitignore)
```

## 설치기가 하는 일
- `{autopf}\Shashalung Downloader`에 앱 + tools + .NET 런타임 설치
- 시작 메뉴 · (선택) 바탕화면 바로가기 생성
- `mpdl://` URL 프로토콜을 HKCR에 등록 (앱도 실행 시 HKCU에 self-heal 등록)
- 제거 시 tools/ 및 설치 파일 정리 (사용자 다운로드·설정은 보존)

## 이미지 갱신
마법사 배너는 `docs/marketing/*.html`을 브라우저 렌더로 PNG화한 것이다.
디자인을 바꾸려면 해당 HTML 수정 → PNG 재생성 → `Assets/`로 복사.

---
개발자: **라이프백패커** (Lifebackpacker)

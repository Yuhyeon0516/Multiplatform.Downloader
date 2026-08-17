; ShyshyroongDownloader.iss — 샤샤룽 다운로더 설치 스크립트
; Inno Setup 6.3+ (PNG 위저드 이미지 사용)

#define MyAppName "샤샤룽 다운로더"
#define MyAppNameEn "Shyshyroong Downloader"
#define MyAppExeName "Multiplatform-Downloader.exe"
#define MyAppExePath "publish\" + MyAppExeName

#ifndef MyAppVersion
  #define MyAppVersion GetVersionNumbersString(MyAppExePath)
#endif

#define MyAppPublisher "샤샤룽컴퍼니 (Shyshyroong Company)"
#define MyAppScheme "mpdl"

[Setup]
AppId={{8F3A6C21-9D4B-4E77-B2A5-3C1E9F60D8A4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoDescription={#MyAppNameEn} Setup
DefaultDirName={autopf}\Shyshyroong Downloader
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=Output
OutputBaseFilename=ShyshyroongDownloader_Setup_v{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
SetupIconFile=Assets\app.ico
; 업그레이드: 실행 중인 앱은 재시작 관리자로 자동 종료(파일 잠금 방지)
CloseApplications=yes
RestartApplications=no
WizardStyle=modern
WizardImageFile=Assets\wizard-large.png
WizardSmallImageFile=Assets\wizard-small.png
WizardSizePercent=140,120

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "Windows 시작 시 자동 실행 (트레이로 최소화된 상태로 시작)"; GroupDescription: "추가 옵션:"

[Files]
; 앱 퍼블리시 출력물 전체 (tools\ 번들 엔진 포함 — csproj가 publish에 복사)
Source: "publish\*"; DestDir: "{app}"; Excludes: "*.pdb,*.xml"; \
    Flags: ignoreversion recursesubdirs createallsubdirs
; 바로가기 아이콘
Source: "Assets\app.ico"; DestDir: "{app}"; Flags: ignoreversion
; Chrome 확장 (chrome://extensions → '압축해제된 확장 로드'로 이 폴더 선택)
Source: "..\chrome-extension\*"; DestDir: "{app}\chrome-extension"; Flags: ignoreversion recursesubdirs createallsubdirs
; 특징 소개 이미지 2장 (커스텀 페이지에서 임시 추출해 표시)
Source: "Assets\features-1.bmp"; Flags: dontcopy
Source: "Assets\features-2.bmp"; Flags: dontcopy

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; WorkingDir: "{app}"
Name: "{group}\Chrome 확장 폴더 열기"; Filename: "{app}\chrome-extension"; Comment: "chrome://extensions에서 이 폴더를 '압축해제된 확장 로드'로 선택하세요"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; mpdl:// URL 프로토콜 (머신 전역 HKCR). 앱도 실행 시 HKCU에 self-heal 등록한다.
Root: HKCR; Subkey: "{#MyAppScheme}"; ValueType: string; ValueName: ""; ValueData: "URL:{#MyAppScheme} Protocol"; Flags: uninsdeletekey
Root: HKCR; Subkey: "{#MyAppScheme}"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCR; Subkey: "{#MyAppScheme}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKCR; Subkey: "{#MyAppScheme}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

; Windows 시작 시 자동 실행 (startup 태스크 선택 시) — 앱의 StartupRegistrar와 동일한 값 이름 사용
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "MultiplatformDownloader"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; \
    Flags: uninsdeletevalue; Tasks: startup

[Run]
; runascurrentuser: 설치 직후 첫 실행이 관리자 토큰으로 돌면 UIPI가 일반 권한 앱(캡컷·탐색기)으로의
; 파일 드래그를 조용히 차단한다(FR-DG5) — 반드시 현재 사용자 권한으로 실행
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
    Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallDelete]
; 번들 엔진·확장 등 런타임 잔여물 정리 (사용자 다운로드/설정은 보존)
Type: filesandordirs; Name: "{app}\tools"
Type: filesandordirs; Name: "{app}\chrome-extension"
Type: dirifempty; Name: "{app}"

[Code]
// ── 이전 버전 자동 제거(업그레이드) ─────────────────────────────
// 같은 AppId로 설치된 이전 버전이 있으면 조용히 제거한 뒤 새 버전을 설치한다.
// 사용자 데이터(설정·큐·쿠키)는 %APPDATA% 에 있어 제거 시에도 보존된다.
const
  UninstallRegKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F3A6C21-9D4B-4E77-B2A5-3C1E9F60D8A4}_is1';

function GetPreviousUninstaller(): String;
var
  S: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM, UninstallRegKey, 'UninstallString', S) then
    Result := S
  else if RegQueryStringValue(HKCU, UninstallRegKey, 'UninstallString', S) then
    Result := S;
end;

// 실행 중인 앱을 강제 종료한다. 트레이 앱이라 CloseApplications만으로는 부족 —
// 앱이 살아 있으면 구버전 언인스톨러가 잠긴 exe/dll을 못 지워 '제거 안 됨'이 된다(사용자 보고).
procedure KillRunningApp();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM "{#MyAppExeName}" /F', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(800); // 파일 핸들 해제 대기
end;

// 설치 최초 단계(엘리베이션 직후)에서 앱을 종료 — 파일 잠금으로 인한 설치 롤백/구버전 잔존 방지.
function InitializeSetup(): Boolean;
begin
  KillRunningApp();
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Uninstaller: String;
  ResultCode, Waited: Integer;
begin
  Result := '';

  // 이전 버전 유무와 무관하게 먼저 실행 중인 앱을 종료(파일 잠금 해제)
  KillRunningApp();

  Uninstaller := GetPreviousUninstaller();
  if Uninstaller = '' then
    Exit; // 첫 설치 — 제거할 이전 버전 없음

  Uninstaller := RemoveQuotes(Uninstaller);
  // /SILENT: 제거 진행바 창을 "보이게" 표시(사용자 요청 — 티나게). /VERYSILENT 였다면 숨김.
  // /SUPPRESSMSGBOXES: 확인·완료 대화상자는 생략(자동 진행). SW_SHOW 로 창을 표시.
  if not Exec(Uninstaller, '/SILENT /SUPPRESSMSGBOXES /NORESTART', '',
              SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    Exit; // 제거 실패 → 같은 폴더 덮어쓰기 업그레이드로 계속(치명 아님)

  // 언인스톨러는 종료 후에도 잠시 정리 작업을 한다 — 레지스트리 키가 사라질 때까지 대기(최대 15초)
  Waited := 0;
  while (GetPreviousUninstaller() <> '') and (Waited < 15000) do
  begin
    Sleep(250);
    Waited := Waited + 250;
  end;
  // 언인스톨러가 자기 폴더를 정리할 시간을 조금 더 준다(비동기 temp 재실행 대비)
  Sleep(1000);
end;

// 원본 1040x660 이미지를 페이지 폭에 맞춰 비율 유지하며 중앙 배치하는 특징 페이지를 만든다.
function AddFeaturePage(AfterId: Integer; Caption, Desc, BmpName: String): Integer;
var
  Page: TWizardPage;
  Img: TBitmapImage;
  W, H: Integer;
begin
  Page := CreateCustomPage(AfterId, Caption, Desc);
  ExtractTemporaryFile(BmpName);

  Img := TBitmapImage.Create(Page);
  Img.Parent := Page.Surface;
  Img.Bitmap.LoadFromFile(ExpandConstant('{tmp}\' + BmpName));
  Img.Stretch := True;

  W := Page.SurfaceWidth;
  H := (W * 660) div 1040;
  if H > Page.SurfaceHeight then
  begin
    H := Page.SurfaceHeight;
    W := (H * 1040) div 660;
  end;
  Img.Width := W;
  Img.Height := H;
  Img.Left := (Page.SurfaceWidth - W) div 2;
  Img.Top := (Page.SurfaceHeight - H) div 2;

  Result := Page.ID;
end;

// 환영 페이지 다음에 특징 소개 2페이지를 삽입한다.
procedure InitializeWizard;
var
  Id: Integer;
begin
  Id := AddFeaturePage(wpWelcome,
    '샤샤룽 다운로더 소개',
    'YouTube · Instagram · TikTok · 샤오홍슈 영상을 한 곳에서.', 'features-1.bmp');
  AddFeaturePage(Id,
    '주요 기능',
    '붙여넣고, 골라 받고, 알아서 정리까지.', 'features-2.bmp');
end;

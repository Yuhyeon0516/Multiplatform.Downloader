; _preview.iss — 위저드 화면(특징 페이지 2장) 미리보기 전용. 실제 설치 안 함.
; 용도: 이미지/문구 교체 후 ISCC로 컴파일해 Output\_WizardPreview.exe 실행 → 페이지만 확인하고 취소.
#define MyAppName "샤샤룽 다운로더 (미리보기)"

[Setup]
AppId={{00000000-0000-0000-0000-000000000000}
AppName={#MyAppName}
AppVersion=0.0.0
DefaultDirName={tmp}\_wizpreview
OutputDir=Output
OutputBaseFilename=_WizardPreview
Compression=none
PrivilegesRequired=lowest
WizardStyle=modern
WizardImageFile=Assets\wizard-large.png
WizardSmallImageFile=Assets\wizard-small.png
WizardSizePercent=140,120
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Files]
Source: "Assets\features-1.bmp"; Flags: dontcopy
Source: "Assets\features-2.bmp"; Flags: dontcopy

[Code]
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

procedure InitializeWizard;
var
  Id: Integer;
begin
  Id := AddFeaturePage(wpWelcome,
    '샤샤룽 다운로더 소개',
    'YouTube · TikTok · Instagram 등 10개 플랫폼 영상을 한 곳에서.', 'features-1.bmp');
  AddFeaturePage(Id,
    '주요 기능',
    '붙여넣고, 골라 받고, 바로 재생까지.', 'features-2.bmp');
end;

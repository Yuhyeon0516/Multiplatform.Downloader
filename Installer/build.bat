@echo off
setlocal
:: 샤샤룽 다운로더 설치기 빌드 (원클릭)
:: 사용법: build.bat            (csproj <Version> 사용)
::         build.bat 2.9.0     (버전 오버라이드)
:: 반드시 Installer\ 폴더 안에서 실행할 것

set VERSION=%~1

:: ── ISCC(Inno 컴파일러) 경로 자동 탐색 ──
set ISCC=
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set ISCC="%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"   set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if exist "C:\Program Files\Inno Setup 6\ISCC.exe"         set ISCC="C:\Program Files\Inno Setup 6\ISCC.exe"
if not defined ISCC ( echo [ERROR] Inno Setup 6 를 찾을 수 없습니다. 설치 후 다시 시도하세요. & exit /b 1 )

set PROJECT=..\Multiplatform-Downloader\Multiplatform-Downloader.csproj
set PUBLISH_DIR=publish

:: [1/2] 앱 퍼블리시 — Release, win-x64, self-contained (.NET 런타임 포함, tools\ 번들)
echo [1/2] dotnet publish (Release, win-x64, self-contained)...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
dotnet publish %PROJECT% -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o %PUBLISH_DIR%
if %ERRORLEVEL% neq 0 ( echo [ERROR] publish 실패 & exit /b 1 )

:: [2/2] Inno Setup 컴파일 (publish 이후여야 버전 자동 추출 가능)
echo [2/2] Inno Setup 컴파일...
if "%VERSION%"=="" ( %ISCC% ShashalungDownloader.iss ) else ( %ISCC% /DMyAppVersion="%VERSION%" ShashalungDownloader.iss )
if %ERRORLEVEL% neq 0 ( echo [ERROR] 컴파일 실패 & exit /b 1 )

echo.
echo === 완료 ===
dir /b Output\*.exe
endlocal

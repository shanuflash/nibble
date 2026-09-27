@echo off
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist bin mkdir bin
if not exist obj mkdir obj
rem csc fails on very long TEMP paths; keep it short.
set TMP=%~dp0obj
set TEMP=%~dp0obj

rem Version comes from NIBBLE_VERSION (the release workflow sets it), default 0.3.0.
if "%NIBBLE_VERSION%"=="" set NIBBLE_VERSION=0.3.0
> obj\Version.cs echo [assembly: System.Reflection.AssemblyVersion("%NIBBLE_VERSION%.0")]
>> obj\Version.cs echo [assembly: System.Reflection.AssemblyFileVersion("%NIBBLE_VERSION%.0")]

set OPTS=/nologo /target:winexe /optimize+ /unsafe+ /platform:anycpu /r:System.Windows.Forms.dll /r:System.Drawing.dll

rem Pass 1: build a helper to render the app icon, pass 2: build with the icon embedded.
"%CSC%" %OPTS% /out:obj\iconmaker.exe /recurse:src\*.cs obj\Version.cs || exit /b 1
obj\iconmaker.exe --make-icon obj\app.ico || exit /b 1
"%CSC%" %OPTS% /win32icon:obj\app.ico /out:bin\Nibble.exe /recurse:src\*.cs obj\Version.cs || exit /b 1
echo Built bin\Nibble.exe (v%NIBBLE_VERSION%)

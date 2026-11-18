@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Compilador C# do .NET Framework 4 nao encontrado.
    exit /b 1
)

if not exist bin mkdir bin

set "REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.ServiceProcess.dll /r:System.Xml.dll"
set "OPTS=/nologo /codepage:65001 /optimize+ /platform:anycpu /warn:4"

echo Compilando DesinstaladorCompleto.exe ...
"%CSC%" %OPTS% /target:winexe /win32manifest:app.manifest /win32icon:app.ico /out:bin\DesinstaladorCompleto.exe %REFS% src\*.cs
if errorlevel 1 exit /b 1

if /i "%~1"=="test" (
    echo Compilando ScanTest.exe ^(varredura somente leitura^) ...
    "%CSC%" %OPTS% /target:exe /main:ScanTest /out:bin\ScanTest.exe %REFS% src\*.cs tests\ScanTest.cs
    if errorlevel 1 exit /b 1
    echo Compilando E2ETest.exe ^(teste de ponta a ponta com programa falso^) ...
    "%CSC%" %OPTS% /target:exe /main:E2ETest /out:bin\E2ETest.exe %REFS% src\*.cs tests\E2ETest.cs
    if errorlevel 1 exit /b 1
)

echo.
echo Pronto: bin\DesinstaladorCompleto.exe
endlocal

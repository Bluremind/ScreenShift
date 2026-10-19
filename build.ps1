# Builds dist\ScreenShift.exe from source with the C# compiler that ships with Windows.
# No SDK, Visual Studio or NuGet needed.
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $out | Out-Null

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc (.NET Framework 4.x is required)." }

& $csc /nologo /target:winexe /platform:anycpu /optimize+ `
    /out:"$out\ScreenShift.exe" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    "$root\ScreenShift.cs" "$root\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw "Build failed (csc exit code $LASTEXITCODE)." }

$hash = (Get-FileHash "$out\ScreenShift.exe" -Algorithm SHA256).Hash
"Built $out\ScreenShift.exe"
"SHA256 $hash"

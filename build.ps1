# Builds dist\ScreenShift.exe from source with the C# compiler that ships with Windows.
# No SDK, Visual Studio or NuGet needed.
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
param(
    [switch]$Sign,
    [string]$CertificateThumbprint,
    [string]$SignToolPath,
    [string]$TimestampUrl,
    [switch]$MachineStore
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root 'dist'
if (-not $Sign -and ($CertificateThumbprint -or $SignToolPath -or $TimestampUrl -or $MachineStore)) {
    throw 'Signing options require -Sign. No build was performed.'
}
if ($Sign) {
    if ($CertificateThumbprint -notmatch '^[a-fA-F0-9]{40}$') { throw 'Provide the signing certificate SHA-1 thumbprint (40 hexadecimal characters).' }
    $timestamp = $null
    if (-not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$timestamp) -or $timestamp.Scheme -notin @('http','https')) {
        throw 'Provide your certificate provider RFC 3161 timestamp URL with -TimestampUrl.'
    }
    if (-not $SignToolPath) {
        $tool = Get-Command signtool.exe -ErrorAction SilentlyContinue
        if ($tool) { $SignToolPath = $tool.Source }
    }
    if (-not $SignToolPath -or -not (Test-Path -LiteralPath $SignToolPath -PathType Leaf)) {
        throw 'Install the Windows SDK Signing Tools and provide -SignToolPath or add signtool.exe to PATH.'
    }
    $store = if ($MachineStore) { 'LocalMachine' } else { 'CurrentUser' }
    $cert = Get-Item -LiteralPath "Cert:\$store\My\$CertificateThumbprint" -ErrorAction Stop
    if (-not $cert.HasPrivateKey -or $cert.NotAfter -le (Get-Date) -or $cert.NotBefore -gt (Get-Date)) {
        throw 'The certificate must be currently valid and have an accessible private key.'
    }
    if (@($cert.EnhancedKeyUsageList | Where-Object { $_.ObjectId.Value -eq '1.3.6.1.5.5.7.3.3' }).Count -eq 0) {
        throw 'The certificate does not have the Code Signing extended key usage.'
    }
}
New-Item -ItemType Directory -Force $out | Out-Null
if (Test-Path -LiteralPath "$out\ScreenShift.exe.sha256") { Remove-Item -LiteralPath "$out\ScreenShift.exe.sha256" }

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc (.NET Framework 4.x is required)." }

& $csc /nologo /target:winexe /platform:anycpu /optimize+ `
    /out:"$out\ScreenShift.exe" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    "$root\ScreenShift.cs" "$root\UiText.cs" "$root\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw "Build failed (csc exit code $LASTEXITCODE)." }

if ($Sign) {
    $signArgs = @('sign', '/sha1', $CertificateThumbprint, '/s', 'My', '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256')
    if ($MachineStore) { $signArgs += '/sm' }
    & $SignToolPath @signArgs "$out\ScreenShift.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Signing or timestamping failed. Do not distribute this build.' }
    & $SignToolPath verify /pa /all /tw "$out\ScreenShift.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Signature verification failed. Do not distribute this build.' }
    $signature = Get-AuthenticodeSignature -LiteralPath "$out\ScreenShift.exe"
    if ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate -or $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint) {
        throw 'Expected a valid timestamped signature from the selected certificate.'
    }
} else {
    Write-Warning 'Local build is unsigned. Use -Sign with a trusted code-signing certificate for a signed build.'
}
$hash = (Get-FileHash "$out\ScreenShift.exe" -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$out\ScreenShift.exe.sha256", "$hash  ScreenShift.exe`r`n", [Text.Encoding]::ASCII)
"Built $out\ScreenShift.exe"
"SHA256 $hash"

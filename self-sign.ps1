# Local development signing only. Never installs a trusted root certificate.
param([string]$Publisher = 'Hao')
$ErrorActionPreference = 'Stop'
if ($Publisher -notmatch '^[A-Za-z0-9 ._-]{1,64}$') { throw 'Use a simple publisher name.' }
$inputFile = Join-Path $PSScriptRoot 'dist\ScreenShift.exe'
if (-not (Test-Path -LiteralPath $inputFile)) { throw 'Run build.ps1 first.' }
$subject = 'CN=' + $Publisher
$friendlyName = 'ScreenShift local development - ' + $Publisher
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object {
    $_.Subject -eq $subject -and $_.FriendlyName -eq $friendlyName -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(1)
} | Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -FriendlyName $friendlyName -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(1)
}
$out = Join-Path $PSScriptRoot 'dist\self-signed'
New-Item -ItemType Directory -Force $out | Out-Null
$exe = Join-Path $out 'ScreenShift.exe'
$checksum = $exe + '.sha256'
if (Test-Path -LiteralPath $checksum) { Remove-Item -LiteralPath $checksum }
Copy-Item -LiteralPath $inputFile -Destination $exe
$signature = Set-AuthenticodeSignature -LiteralPath $exe -Certificate $cert -HashAlgorithm SHA256
if (-not $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $cert.Thumbprint -or $signature.Status -eq 'HashMismatch' -or $signature.Status -eq 'NotSigned') {
    throw ('Signing failed: ' + $signature.StatusMessage)
}
Export-Certificate -Cert $cert -FilePath (Join-Path $out 'publisher-public.cer') | Out-Null
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
[IO.File]::WriteAllText($checksum, "$hash  ScreenShift.exe`r`n", [Text.Encoding]::ASCII)
@'
SELF-SIGNED DEVELOPMENT BUILD - NOT A PUBLICLY TRUSTED RELEASE
Publisher certificate is self-issued. Windows may report UnknownError/NotTrusted
because the certificate has no trusted chain. No trusted root was installed.
No trusted timestamp is included. Gmail still blocks executable attachments.
The .cer file contains only the public certificate, not the private key.
Original ScreenShift LICENSE and distribution restrictions still apply.
'@ | Set-Content -LiteralPath (Join-Path $out 'SIGNING-NOTICE.txt') -Encoding UTF8
$signature | Select-Object Status,StatusMessage,@{Name='Publisher';Expression={$_.SignerCertificate.Subject}}
"Self-signed build: $exe"
"SHA256 $hash"

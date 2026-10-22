Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
[void][Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\ScreenShift.exe'))
try {
$c = [ScreenShift.DisplayConfig]::Query()
'Panel: ' + $c.Panel + '; desktop: ' + $c.Desktop + '; region: ' + $c.Region
$d = New-Object Drawing.Size
$r = New-Object Drawing.Rectangle
foreach ($w in @(0,1366,1920)) {
if ([ScreenShift.Planner]::Plan($c.Panel,100,50,200,150,$false,$true,$true,$w,[ref]$d,[ref]$r)) {
'Validation only: desktop ' + $d + ' region ' + $r + ' code=' + $c.With($d,$r).Apply([ScreenShift.DisplayConfig]::SDC_VALIDATE)
}
}
} catch { 'Read-only display check: ' + $_.Exception.Message }
'Identity validation code=' + $c.Identity().Apply([ScreenShift.DisplayConfig]::SDC_VALIDATE)
$area = New-Object Drawing.Rectangle(100,50,1620,880)
foreach ($mode in [ScreenShift.Planner]::DriverModes()) {
    $region = [ScreenShift.Planner]::Fit($mode,$area)
    'Driver mode ' + $mode + '; validation code=' + $c.With($mode,$region).Apply([ScreenShift.DisplayConfig]::SDC_VALIDATE)
}

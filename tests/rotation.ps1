$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
[void][Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\ScreenShift.exe'))
function Assert($v,$m) { if (-not $v) { throw $m } }
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
foreach ($rotation in 1..4) {
    $c = New-Object ScreenShift.DisplayConfig
    $paths = New-Object byte[] 72
    $modes = New-Object byte[] 192
    [BitConverter]::GetBytes([uint32]$rotation).CopyTo($paths,40)
    [BitConverter]::GetBytes([int]1920).CopyTo($modes,104)
    [BitConverter]::GetBytes([int]1080).CopyTo($modes,108)
    foreach ($entry in @{paths=$paths;modes=$modes;src=0;tgt=1;img=2;numPaths=[uint32]1;numModes=[uint32]3}.GetEnumerator()) {
        $c.GetType().GetField($entry.Key,$flags).SetValue($c,$entry.Value)
    }
    $expected = if ($rotation -in 2,4) { New-Object Drawing.Size(1080,1920) } else { New-Object Drawing.Size(1920,1080) }
    Assert ($c.Panel -eq $expected) "Wrong panel for rotation $rotation"
    $identity = $c.Identity()
    Assert ($identity.IsIdentity) "Identity failed for rotation $rotation"
    Assert ($identity.Desktop -eq $expected -and $identity.PathSource -eq $expected) 'Source dimensions incorrect'
    Assert ($identity.Rotation -eq $rotation -and $identity.SignalSize -eq $c.SignalSize) 'Physical timing or rotation changed'
    $area = New-Object Drawing.Rectangle(30,40,($expected.Width-100),($expected.Height-120))
    $desktop = New-Object Drawing.Size([int]($expected.Width/2),[int]($expected.Height/2))
    $region = [ScreenShift.Planner]::Fit($desktop,$area)
    $changed = $identity.With($desktop,$region)
    Assert ($changed.Region -eq $region -and $area.Contains($changed.Region)) 'Crop coordinates changed'
    Assert ($changed.PathSource -eq $expected -and $changed.Desktop -eq $desktop) 'Crop dimensions incorrect'
    Assert ($changed.Identity().IsIdentity -and $identity.IsIdentity) 'Restore or clone failed'
}
Assert ([ScreenShift.Planner]::AspectLabel((New-Object Drawing.Size(768,1366))) -eq ([char]0x2248 + '9:16')) 'Portrait ratio label failed'
'PASS: all four rotations, crop/restore geometry and portrait ratio labels (no display changes).'

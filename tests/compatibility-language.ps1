$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
[void][Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\ScreenShift.exe'))
function Assert($value,$message) { if (-not $value) { throw $message } }
$panel = New-Object Drawing.Size(1920,1080)
$area = New-Object Drawing.Rectangle(100,50,1620,880)
$mode = New-Object Drawing.Size(1366,768)
$key = [ScreenShift.Planner]::DriverKey($mode)
Assert ([ScreenShift.Planner]::ValidResolutionKey($key)) 'Driver key rejected'
Assert (-not [ScreenShift.Planner]::ValidResolutionKey(8193)) 'Invalid custom width accepted'
Assert (-not [ScreenShift.Planner]::ValidResolutionKey(19209999)) 'Invalid driver height accepted'
$d = New-Object Drawing.Size
$r = New-Object Drawing.Rectangle
Assert ([ScreenShift.Planner]::Plan($panel,100,50,200,150,$false,$true,$true,$key,[ref]$d,[ref]$r)) 'Driver plan failed'
Assert ($d -eq $mode) 'Driver dimensions changed'
Assert ($area.Contains($r)) 'Driver region escaped cuts'
Assert ([Math]::Abs($r.Width * 768.0 / 1366 - $r.Height) -le 1) 'Driver region distorted aspect ratio'
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$field = [ScreenShift.Program].GetField('SettingsFile',$flags)
$oldSettings = $field.GetValue($null)
$oldLanguage = [ScreenShift.L]::Language
$oldLanguageFile = [ScreenShift.L]::LanguageFile
$temp = Join-Path ([IO.Path]::GetTempPath()) ('screenshift-ui-' + [Guid]::NewGuid() + '.txt')
$tempLanguage = $temp + '.language'
try {
    $field.SetValue($null,$temp)
    $save = [ScreenShift.Program].GetMethod('Save',$flags)
    $load = [ScreenShift.Program].GetMethod('Load',$flags)
    [void]$save.Invoke($null,@(100,50,200,150,$false,$true,$true,$key))
    $values = @(0,0,0,0,$false,$true,$false,0)
    Assert ($load.Invoke($null,$values)) 'Driver settings failed to load'
    Assert ($values[7] -eq $key) 'Driver choice was not persisted'
    [ScreenShift.L]::LanguageFile = $tempLanguage
    foreach ($code in [ScreenShift.L]::Codes) {
        [ScreenShift.L]::Language = $code
        [ScreenShift.L]::SaveLanguage()
        [ScreenShift.L]::Language = 'unknown'
        [ScreenShift.L]::LoadLanguage()
        Assert ([ScreenShift.L]::Language -eq $code) 'Language preference not loaded'
        foreach ($label in @('aspect','area','instructions','native','driver','custom','crisp','fill','stretch','desktop','invalid','confirm','fallback','rejected','single')) {
            Assert ([ScreenShift.L]::Get($label) -ne $label) ('Missing translation: ' + $code + '/' + $label)
        }
        $overlay = New-Object ScreenShift.Overlay($panel,100,50,200,150,$false,$true,$true,$key)
        try {
            $list = $overlay.Controls[4]
            Assert ($list.Items.Count -eq 5) 'Language options missing'
            $next = ([Array]::IndexOf([ScreenShift.L]::Codes,$code) + 1) % 5
            $list.SelectedIndex = $next
            Assert ($overlay.Controls[0].Text -eq [ScreenShift.L]::Get('aspect')) 'Checkbox did not translate live'
            Assert ($overlay.Controls[1].Text -eq [ScreenShift.L]::Get('area')) 'Area label did not translate live'
            $bitmap = New-Object Drawing.Bitmap(1920,1080)
            try {
                $overlay.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,1920,1080)))
                if ([ScreenShift.L]::Language -eq 'zh') { $bitmap.Save((Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\preview-zh.png')) }
            } finally { $bitmap.Dispose() }
            $confirm = New-Object ScreenShift.Confirm
            try { Assert ($confirm.Controls[1].Text -eq [ScreenShift.L]::Get('Keep')) 'Confirmation button untranslated' }
            finally { $confirm.Dispose() }
        } finally { $overlay.Dispose() }
    }
} finally {
    $field.SetValue($null,$oldSettings)
    [ScreenShift.L]::Language = $oldLanguage
    [ScreenShift.L]::LanguageFile = $oldLanguageFile
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp }
    if (Test-Path -LiteralPath $tempLanguage) { Remove-Item -LiteralPath $tempLanguage }
}
'PASS: driver dimensions and proportional fit, persisted driver selection, five languages, live switching, confirmation labels and rendering. No display settings changed.'
Assert (([ScreenShift.L]::Codes -join ',') -eq 'en,zh,ru,ja,ko') 'Language display order incorrect'
$overlay = New-Object ScreenShift.Overlay($panel,100,50,200,150,$false,$true,$true,0)
try {
    $list = $overlay.Controls[2]
    foreach ($preset in [ScreenShift.Planner]::UltrawideModes()) {
        $overlay.Dispose()
        $overlay = New-Object ScreenShift.Overlay($preset,0,0,0,0,$false,$true,$true,0)
        $list = $overlay.Controls[2]
        $key = [ScreenShift.Planner]::DriverKey($preset)
        $found = $false
        for ($i=0; $i -lt $list.Items.Count; $i++) {
            $item = $list.Items[$i]
            if ($item.GetType().GetField('Width').GetValue($item) -eq $key) {
                $list.SelectedIndex = $i
                $found = $true
                break
            }
        }
        Assert ($found -and $overlay.ResolutionWidth -eq $key) 'Ultrawide preset missing from dropdown'
        Assert ([ScreenShift.Planner]::Plan($panel,100,50,200,150,$false,$true,$true,$key,[ref]$d,[ref]$r)) 'Ultrawide plan failed'
        Assert ($d -eq $preset -and $area.Contains($r)) 'Preset resolution or region incorrect'
        Assert ([Math]::Abs($r.Width * [double]$d.Height / $d.Width - $r.Height) -le 1) 'Ultrawide preset stretched'
    }
    Assert ((1280*1080 -eq 540*2560) -and (1920*1080 -eq 810*2560)) '64:27 preset ratio mismatch'
    Assert ((1720*1440 -eq 720*3440) -and (2580*1440 -eq 1080*3440)) '43:18 preset ratio mismatch'
    Assert ((1920*1440 -eq 540*5120) -and (2560*1440 -eq 720*5120) -and (3840*1440 -eq 1080*5120)) '32:9 preset ratio mismatch'
} finally { $overlay.Dispose() }
'PASS: English-first language order, all 10 ultrawide presets visible/selectable, exact ratio families and proportional placement.'

$families = New-Object 'Collections.Generic.List[Drawing.Size]'
foreach ($pair in @(@(1000,1000),@(1280,720),@(1366,768),@(1920,1080),@(2560,1080),@(3440,1440),@(1920,540),@(2560,720),@(5120,1440))) {
    $families.Add((New-Object Drawing.Size($pair[0],$pair[1])))
}
$filtered = [ScreenShift.Planner]::NearestAspectModes((New-Object Drawing.Size(1600,900)),$families)
Assert ($filtered.Count -eq 3) '16:9 must retain rounded 1366x768 and other sizes, excluding square/ultrawide'
$filtered = [ScreenShift.Planner]::NearestAspectModes((New-Object Drawing.Size(1920,540)),$families)
Assert ($filtered.Count -eq 3) '32:9 family filtering failed'
$filtered = [ScreenShift.Planner]::NearestAspectModes((New-Object Drawing.Size(800,800)),$families)
Assert ($filtered.Count -eq 1 -and $filtered[0].Width -eq 1000) 'Square filtering failed'
$overlay = New-Object ScreenShift.Overlay($panel,0,0,0,0,$false,$true,$true,([ScreenShift.Planner]::DriverKey((New-Object Drawing.Size(1920,1080)))))
try {
    $overlay.CutBottom = 540
    [void][ScreenShift.Overlay].GetMethod('PositionAspectBox',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($overlay,@())
    Assert ($overlay.ResolutionWidth -eq 0) 'Filtered-out selection must reset to auto'
    foreach ($item in $overlay.Controls[2].Items) {
        $key = $item.GetType().GetField('Width').GetValue($item)
        if ($key -gt 8192) {
            $size = [ScreenShift.Planner]::AreaResolution((New-Object Drawing.Size(1920,540)),$key)
            Assert ([Math]::Abs($size.Width / [double]$size.Height - 32.0/9) -lt 0.01) 'Unrelated fixed ratio remains after resize'
        }
    }
} finally { $overlay.Dispose() }
'PASS: closest aspect families, same-ratio resolutions, rounding tolerance and live selection reset.'
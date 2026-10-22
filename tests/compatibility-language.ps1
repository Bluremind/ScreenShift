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
$families = New-Object 'Collections.Generic.List[Drawing.Size]'
foreach ($pair in @(@(1000,1000),@(1280,720),@(1366,768),@(1920,1080),@(2560,1080),@(3440,1440),@(5120,1440))) {
    $families.Add((New-Object Drawing.Size($pair[0],$pair[1])))
}
$wideArea = New-Object Drawing.Rectangle(0,0,1920,800)
$rejectUltrawide = [Func[Drawing.Size,Drawing.Rectangle,int]] { param($size,$rect) if ($size.Width -gt 1920) {31} else {0} }
$accepted = [ScreenShift.Planner]::ValidatedNearestModes($wideArea,$families,$rejectUltrawide)
Assert ($accepted.Count -eq 3) 'Rejected nearest family must not hide valid 16:9 family'
foreach ($m in $accepted) { Assert ($m.Width -le 1920 -and $m.Width -ne 1000) 'Unverified or unrelated mode leaked into list' }
$rejectAll = [Func[Drawing.Size,Drawing.Rectangle,int]] { param($size,$rect) 31 }
Assert ([ScreenShift.Planner]::ValidatedNearestModes($wideArea,$families,$rejectAll).Count -eq 0) 'Rejected modes shown'
Assert ([ScreenShift.Planner]::ValidatedNearestModes($wideArea,$families,$null).Count -eq 0) 'Unvalidated modes shown'
Assert ([ScreenShift.Planner]::AspectLabel((New-Object Drawing.Size(1024,768))) -eq '4:3') '4:3 label incorrect'
Assert ([ScreenShift.Planner]::AspectLabel((New-Object Drawing.Size(1280,1024))) -eq '5:4') '5:4 label incorrect'
Assert ([ScreenShift.Planner]::AspectLabel((New-Object Drawing.Size(1280,768))) -eq '5:3') '5:3 label incorrect'
Assert ([ScreenShift.Planner]::AspectLabel((New-Object Drawing.Size(1920,1080))) -eq '16:9') '16:9 label incorrect'
Assert ([ScreenShift.Planner]::AspectLabel((New-Object Drawing.Size(1366,768))) -eq '≈16:9') 'Rounded aspect label must be approximate'
[ScreenShift.Planner].GetField('driverModes',$flags).SetValue($null,$families)
$overlay = New-Object ScreenShift.Overlay($panel,0,0,0,280,$false,$true,$true,51201440,$rejectUltrawide)
try {
    $list = $overlay.Controls[2]
    Assert ($list.Items.Count -eq 3 -and $list.Enabled) 'Dropdown not restricted to accepted nearest family'
    foreach ($item in $list.Items) {
        $key = $item.GetType().GetField('Width').GetValue($item)
        Assert ($key -gt 8192 -and $key -lt 25600000) 'Guessed resolution remained in dropdown'
        Assert ($item.ToString() -match '16:9') 'Aspect ratio missing from label'
    }
    Assert ($overlay.ResolutionWidth -eq 19201080) 'Invalid saved choice must resolve to nearest accepted size'
    $overlay.CutRight = 920
    $overlay.CutBottom = 80
    [void][ScreenShift.Overlay].GetMethod('PositionAspectBox',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($overlay,@())
    Assert ($list.Items.Count -eq 1 -and $list.Text -match '1:1') 'Changed area did not trigger revalidation and regrouping'
} finally { $overlay.Dispose() }
$overlay = New-Object ScreenShift.Overlay($panel,0,0,0,0,$false,$true,$true,0,$rejectAll)
try { Assert ($overlay.Controls[2].Items.Count -eq 0 -and -not $overlay.Controls[2].Enabled) 'No-mode state must disable list' }
finally { $overlay.Dispose() }
'PASS: validation BEFORE aspect filtering, no guessed modes, ratio labels, nearest accepted selection, live refresh and empty-state handling.'
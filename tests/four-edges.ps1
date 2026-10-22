$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
[void][Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\ScreenShift.exe'))
function Assert($condition, $message) { if (-not $condition) { throw $message } }
$panel = New-Object Drawing.Size(1920,1080)
$desktop = New-Object Drawing.Size
$region = New-Object Drawing.Rectangle
Assert ([ScreenShift.Planner]::Plan($panel,100,50,200,150,$false,[ref]$desktop,[ref]$region)) 'Four-edge fill failed'
Assert ($region.Left -ge 100 -and $region.Top -ge 50 -and $region.Right -le 1720 -and $region.Bottom -le 930) 'Region escaped selected edges'
Assert ($desktop -eq $panel) 'Fill changed desktop resolution'
Assert ([ScreenShift.Planner]::Plan($panel,0,0,0,0,$false,[ref]$desktop,[ref]$region)) 'Identity failed'
Assert ($region -eq (New-Object Drawing.Rectangle(0,0,1920,1080))) 'Identity rectangle differs'
Assert (-not [ScreenShift.Planner]::Plan($panel,-1,0,0,0,$false,[ref]$desktop,[ref]$region)) 'Negative cut accepted'
Assert (-not [ScreenShift.Planner]::Plan($panel,[int]::MaxValue,0,1,0,$false,[ref]$desktop,[ref]$region)) 'Overflow cut accepted'
Assert (-not [ScreenShift.Planner]::Plan($panel,960,0,960,0,$false,[ref]$desktop,[ref]$region)) 'Empty width accepted'
# Inject deterministic standard modes: no display enumeration or display changes.
$flags = [Reflection.BindingFlags]'NonPublic,Static'
$modes = New-Object 'Collections.Generic.List[Drawing.Size]'
$modes.Add((New-Object Drawing.Size(1600,900)))
$modes.Add((New-Object Drawing.Size(1280,720)))
[ScreenShift.Planner].GetField('crispModes',$flags).SetValue($null,$modes)
Assert ([ScreenShift.Planner]::Plan($panel,100,50,200,150,$true,[ref]$desktop,[ref]$region)) 'Crisp failed'
Assert ($desktop.Width -eq 1280 -and $region.X -eq 270 -and $region.Y -eq 130) 'Crisp placement incorrect'
$overlay = New-Object ScreenShift.Overlay($panel,0,0,0,0,$false)
try {
    $method = [ScreenShift.Overlay].GetMethod('ProcessCmdKey',[Reflection.BindingFlags]'Instance,NonPublic')
    function Press([Windows.Forms.Keys]$key) {
        $parameters = @((New-Object Windows.Forms.Message).PSObject.BaseObject,$key)
        [void]$method.Invoke($overlay,$parameters)
    }
    Press ([Windows.Forms.Keys]::Right)
    Press ([Windows.Forms.Keys]::Up)
    Press ([Windows.Forms.Keys]::Control -bor [Windows.Forms.Keys]::Left)
    Press ([Windows.Forms.Keys]::Control -bor [Windows.Forms.Keys]::Down -bor [Windows.Forms.Keys]::Alt)
    Assert ($overlay.CutLeft -eq 4 -and $overlay.CutBottom -eq 4 -and $overlay.CutRight -eq 4 -and $overlay.CutTop -eq 1) 'Four-edge keys incorrect'
    $overlay.CutLeft = 1276
    Press ([Windows.Forms.Keys]::Control -bor [Windows.Forms.Keys]::Left)
    Assert ($overlay.CutLeft -eq 1276 -and $overlay.CutRight -eq 4) 'Moving right edge displaced left edge at limit'
    $bitmap = New-Object Drawing.Bitmap(1920,1080)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try { [ScreenShift.Overlay]::Render($graphics,$panel,100,50,200,150,$false) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
} finally { $overlay.Dispose() }
'PASS: four-edge geometry, invalid cuts, crisp placement, keyboard controls, edge limits and preview rendering. No display settings changed.'

# Stretch must use the exact selected rectangle, even with a different aspect ratio.
Assert ([ScreenShift.Planner]::Plan($panel,100,50,200,150,$false,$false,[ref]$desktop,[ref]$region)) 'Stretch failed'
Assert ($region -eq (New-Object Drawing.Rectangle(100,50,1620,880))) 'Stretch did not fill selected rectangle'
Assert ($desktop -eq $panel) 'Stretch must retain the full desktop'
Assert ([ScreenShift.Planner]::Plan($panel,100,50,200,150,$true,$false,[ref]$desktop,[ref]$region)) 'Crisp with saved stretch failed'
Assert ($desktop.Width -eq 1280 -and $region.Width -eq 1280) 'Crisp must remain 1:1'
$overlay = New-Object ScreenShift.Overlay($panel,100,50,200,150,$false,$true)
try {
    $box = $overlay.Controls[0]
    Assert ($box.Checked -and $box.Enabled) 'Aspect checkbox default incorrect'
    $box.Checked = $false
    Assert (-not $overlay.KeepAspect) 'Mouse checkbox state not reflected'
    Press ([Windows.Forms.Keys]::A)
    Assert ($overlay.KeepAspect) 'A shortcut failed'
    Press ([Windows.Forms.Keys]::Tab)
    Assert (-not $box.Enabled) 'Crisp must disable aspect option'
    Press ([Windows.Forms.Keys]::A)
    Assert ($overlay.KeepAspect) 'Disabled aspect changed in Crisp'
    Press ([Windows.Forms.Keys]::Tab)
    Assert ($box.Enabled -and $box.Checked) 'Fill did not restore aspect preference'
    Assert ($box.Left -ge 100 -and $box.Right -le 1720 -and $box.Top -ge 50 -and $box.Bottom -le 930) 'Checkbox outside selected area'
} finally { $overlay.Dispose() }
# Redirect persistence only for this test process; do not touch the user's settings.
$settingsField = [ScreenShift.Program].GetField('SettingsFile',$flags)
$originalPath = $settingsField.GetValue($null)
$tempPath = Join-Path ([IO.Path]::GetTempPath()) ('screenshift-' + [Guid]::NewGuid().ToString() + '.txt')
$save = [ScreenShift.Program].GetMethod('Save',$flags)
$load = [ScreenShift.Program].GetMethod('Load',$flags)
try {
    $settingsField.SetValue($null,$tempPath)
    [void]$save.Invoke($null,@(100,50,200,150,$false,$false,$false,0))
    $parameters = @(0,0,0,0,$true,$true,$false,0)
    Assert ($load.Invoke($null,$parameters)) 'Six-value settings failed'
    Assert ($parameters[0] -eq 100 -and $parameters[1] -eq 50 -and -not $parameters[4] -and -not $parameters[5]) 'Stretch preference not saved'
    foreach ($legacy in @('100 150 fill','100 50 200 150 fill','100 50 200 150 fill aspect')) {
        [IO.File]::WriteAllText($tempPath,$legacy)
        $parameters = @(0,0,0,0,$false,$false,$true,1920)
        Assert ($load.Invoke($null,$parameters)) 'Legacy settings failed'
        Assert ($parameters[5]) 'Legacy settings must preserve aspect ratio'
    }
    [void]$save.Invoke($null,@(100,50,200,150,$true,$true,$true,1920))
    $parameters = @(0,0,0,0,$false,$false,$false,0)
    Assert ($load.Invoke($null,$parameters)) 'Area settings failed'
    Assert ($parameters[6] -and $parameters[7] -eq 1920) 'Area resolution preference not saved'
    foreach ($bad in @('100 50 200 150 fill aspect area -1','100 50 200 150 fill aspect area 8193','100 50 200 150 fill aspect invalid 1920')) {
        [IO.File]::WriteAllText($tempPath,$bad)
        Assert (-not $load.Invoke($null,@(0,0,0,0,$false,$true,$false,0))) 'Invalid area settings accepted'
    }
    [IO.File]::WriteAllText($tempPath,'100 50 200 150 fill invalid')
    Assert (-not $load.Invoke($null,@(0,0,0,0,$false,$true,$false,0))) 'Invalid aspect preference accepted'
} finally {
    $settingsField.SetValue($null,$originalPath)
    if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath }
}
'PASS: stretch geometry, aspect checkbox, mode transitions and backward-compatible settings.'
# Matching the selected area changes desktop resolution as well as the output rectangle.
Assert ([ScreenShift.Planner]::Plan($panel,100,50,200,150,$true,$true,$true,0,[ref]$desktop,[ref]$region)) 'Area mode failed'
Assert ($desktop -eq (New-Object Drawing.Size(1620,880))) 'Auto resolution must equal usable pixels'
Assert ($region -eq (New-Object Drawing.Rectangle(100,50,1620,880))) 'Area mode must fill all usable pixels'
foreach ($width in @(960,1280,1366,1600,1920,2560,3840)) {
    Assert ([ScreenShift.Planner]::Plan($panel,100,50,200,150,$false,$false,$true,$width,[ref]$desktop,[ref]$region)) 'Scaled area plan failed'
    Assert ($desktop.Width -eq $width) 'Selected resolution width ignored'
    Assert ([Math]::Abs($desktop.Height - $width * 880.0 / 1620) -le 0.5) 'Aspect ratio rounding exceeded half a pixel'
    Assert ($region.Width -eq 1620 -and $region.Height -eq 880) 'Resolution selection changed region'
}
Assert (-not [ScreenShift.Planner]::Plan($panel,0,0,0,0,$false,$true,$true,8193,[ref]$desktop,[ref]$region)) 'Oversized resolution accepted'
Assert ([ScreenShift.Planner]::AreaResolution((New-Object Drawing.Size(1600,900)),1920) -eq (New-Object Drawing.Size(1920,1080))) '16:9 option incorrect'
$overlay = New-Object ScreenShift.Overlay($panel,100,50,200,150,$false,$true,$false,0)
try {
    $aspect = $overlay.Controls[0]
    $area = $overlay.Controls[1]
    $list = $overlay.Controls[2]
    Assert (-not $list.Enabled) 'Resolution list enabled without area mode'
    Press ([Windows.Forms.Keys]::D)
    Assert ($overlay.MatchArea -and $list.Enabled -and -not $aspect.Enabled) 'D toggle did not activate area mode'
    $list.SelectedIndex = 5
    Assert ($overlay.ResolutionWidth -eq 1920 -and $list.Text -like '1920*1043*') 'Resolution selection incorrect'
    Press ([Windows.Forms.Keys]::Down)
    Assert ($overlay.ResolutionWidth -eq 1920 -and $list.Text -like '1920*1048*') 'Resolution did not track changed area'
    Assert ($list.Right -le 1720 -and $list.Bottom -le 934) 'Resolution list outside usable area'
    Press ([Windows.Forms.Keys]::D)
    Assert (-not $overlay.MatchArea -and $aspect.Enabled -and -not $list.Enabled) 'Original mode not restored'
    $bitmap = New-Object Drawing.Bitmap(1920,1080)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try { [ScreenShift.Overlay]::Render($graphics,$panel,100,50,200,150,$false,$true,$true,1920) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
} finally { $overlay.Dispose() }
'PASS: native and scaled area resolutions, aspect rounding, checkbox/dropdown transitions and persistence.'
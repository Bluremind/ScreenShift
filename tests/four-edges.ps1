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
    Press ([Windows.Forms.Keys]::Control -bor [Windows.Forms.Keys]::Down -bor [Windows.Forms.Keys]::Shift)
    Assert ($overlay.CutLeft -eq 4 -and $overlay.CutBottom -eq 4 -and $overlay.CutRight -eq 4 -and $overlay.CutTop -eq 20) 'Four-edge keys incorrect'
    $overlay.CutLeft = 1276
    Press ([Windows.Forms.Keys]::Control -bor [Windows.Forms.Keys]::Left)
    Assert ($overlay.CutLeft -eq 1276 -and $overlay.CutRight -eq 4) 'Moving right edge displaced left edge at limit'
    $bitmap = New-Object Drawing.Bitmap(1920,1080)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try { [ScreenShift.Overlay]::Render($graphics,$panel,100,50,200,150,$false) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
} finally { $overlay.Dispose() }
'PASS: four-edge geometry, invalid cuts, crisp placement, keyboard controls, edge limits and preview rendering. No display settings changed.'

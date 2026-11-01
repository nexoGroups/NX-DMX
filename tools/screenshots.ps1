# Starts the app, programs a small demo show through the real keypad and saves the screenshots used in the README.
#   powershell -File tools/screenshots.ps1 -Exe src\NxDmx.App\bin\Debug\net10.0-windows\NX-DMX.exe
# The Art-Net target is switched to loopback first, so nothing reaches a real node on the network.
param(
    [Parameter(Mandatory)] [string]$Exe,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
if (-not $OutDir) { $OutDir = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\docs\images' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
}
"@
[void][Win]::SetProcessDPIAware()
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

function Save-Window($handle, $path) {
    $r = New-Object Win+RECT
    [void][Win]::GetWindowRect($handle, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [void][Win]::PrintWindow($handle, $hdc, 2)
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    return $bmp
}

function Save-Crop($bmp, $x, $y, $w, $h, $path) {
    $crop = $bmp.Clone((New-Object System.Drawing.Rectangle $x, $y, $w, $h), $bmp.PixelFormat)
    $crop.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $crop.Dispose()
}

$AE = [System.Windows.Automation.AutomationElement]
function Find-ById($root, $id) {
    $root.FindFirst('Descendants', (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)))
}
function Find-Button($root, $name) {
    $c1 = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
    $c2 = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $root.FindFirst('Descendants', (New-Object System.Windows.Automation.AndCondition($c1, $c2)))
}
function Set-Text($el, $text) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text) }
function Click($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }

# Types a line and presses Please. WPF runs Invoke asynchronously, so wait until the command line has been emptied by
# the submit; otherwise the next line can overwrite this one before it was taken.
function Submit-Line($cmd, $please, $line) {
    Set-Text $cmd $line
    Click $please
    $value = $cmd.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $deadline = (Get-Date).AddSeconds(3)
    while ($value.Current.Value -ne '' -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 20 }
    Start-Sleep -Milliseconds 80
}

# Something has to listen on the Art-Net port, otherwise Windows reports the loopback sends as failed.
$listener = New-Object System.Net.Sockets.UdpClient (New-Object System.Net.IPEndPoint ([System.Net.IPAddress]::Loopback), 6454)
$listener.Client.ReceiveBufferSize = 4MB
$drain = [System.Threading.Tasks.Task]::Run([Action]{
    $ep = New-Object System.Net.IPEndPoint ([System.Net.IPAddress]::Any), 0
    try { while ($true) { [void]$listener.Receive([ref]$ep) } } catch { }
})

$proc = Start-Process -FilePath $Exe -PassThru
try {
    $deadline = (Get-Date).AddSeconds(20)
    while ($proc.MainWindowHandle -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $proc.Refresh()
    }
    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Main window did not appear' }
    $root = $AE::FromHandle($proc.MainWindowHandle)
    Start-Sleep -Milliseconds 800

    Set-Text (Find-ById $root 'TargetIp') '127.0.0.1'
    Click (Find-Button $root 'Apply')

    $cmd = Find-ById $root 'CommandLine'
    $please = Find-Button $root 'Please'
    foreach ($line in @(
            'Fixture 1 Thru 8 At 100', 'Red At 100', 'Green At 55', 'Blue At 5',
            'Record Cue 1 "Warm Wash"',
            'Record Group 1 "Front Wash"',
            'ClearAll',
            'Fixture 9 Thru 12 At 100', 'Pan At 35', 'Tilt At 70', 'Red At 0', 'Green At 30', 'Blue At 100',
            'Record Group 2 "Movers"',
            'Fixture 1 Thru 8 At 45', 'Red At 10', 'Green At 30', 'Blue At 100',
            'Record Cue 2 "Blue Night"',
            'ClearAll',
            'Fixture 1 Thru 8 Full', 'Red At 100', 'Green At 100', 'Blue At 100',
            'Fixture 9 Thru 12 Full', 'Green At 100', 'Red At 100',
            'Fixture 21 Thru 24 Full',
            'Record Cue 3 "Finale"',
            'ClearAll',
            'Go Cuelist 1', 'Go Cuelist 1',
            'Fixture 3 Thru 5 At 100', 'Red At 100')) {
        Submit-Line $cmd $please $line
    }

    # A half-typed command, like someone in the middle of programming.
    Set-Text $cmd 'Fixture 1 Thru 4 At 50'
    Start-Sleep -Milliseconds 700

    $full = Save-Window $proc.MainWindowHandle (Join-Path $OutDir 'overview.png')
    # Command line and keypad: the bottom part of the window.
    Save-Crop $full 0 ($full.Height - 345) $full.Width 345 (Join-Path $OutDir 'keypad.png')
    $full.Dispose()
    "Saved screenshots to $OutDir"
}
finally {
    $listener.Close()
    if (-not $proc.HasExited) {
        [void]$proc.CloseMainWindow()
        if (-not $proc.WaitForExit(5000)) { $proc.Kill() }
    }
}

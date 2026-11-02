# Starts the app with its own data folder, programs a small demo show through the real command line and saves the
# screenshots used in the README (docs/images).
#   powershell -File tools/screenshots.ps1 -Exe src\NxDmx.App\bin\Debug\net10.0-windows\NX-DMX.exe
# Art-Net goes to loopback and the remote servers use other ports, so nothing reaches the network and a running copy of
# NX-DMX is left alone. The window must stay in front while this runs.
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
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hgt, bool repaint);
}
"@
[void][Win]::SetProcessDPIAware()
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

$AE = [System.Windows.Automation.AutomationElement]
function Find-ById($root, $id) {
    $root.FindFirst('Descendants', (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)))
}
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
    $bmp.Dispose()
}
function Press($root, $id) {
    $el = Find-ById $root $id
    if (-not $el) { throw "Element $id not found" }
    try { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    catch {
        try { $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
        catch { $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
    }
    Start-Sleep -Milliseconds 450
}
function Command($root, $line) {
    $box = (Find-ById $root 'CommandLine').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $box.SetValue($line)
    Press $root 'PleaseTop'
    $deadline = (Get-Date).AddSeconds(3)
    while ($box.Current.Value -ne '' -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 20 }
    Start-Sleep -Milliseconds 80
}

$data = Join-Path ([IO.Path]::GetTempPath()) ('nxdmx-shots-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $data | Out-Null
'{ "BroadcastAddress": "127.0.0.1", "HttpPort": 18765, "OscPort": 18000, "TextPort": 17000, "FaderBarVisible": true }' | Set-Content -Encoding UTF8 (Join-Path $data 'settings.json')
$env:NXDMX_DATA = $data

# Something has to listen on the Art-Net port, otherwise Windows reports the loopback sends as failed.
$listener = New-Object System.Net.Sockets.UdpClient (New-Object System.Net.IPEndPoint ([System.Net.IPAddress]::Loopback), 6454)
$null = [System.Threading.Tasks.Task]::Run([Action]{
    $ep = New-Object System.Net.IPEndPoint ([System.Net.IPAddress]::Any), 0
    try { while ($true) { [void]$listener.Receive([ref]$ep) } } catch { }
})

$proc = Start-Process -FilePath $Exe -PassThru
try {
    $deadline = (Get-Date).AddSeconds(25)
    while ($proc.MainWindowHandle -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200; $proc.Refresh() }
    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Main window did not appear' }
    [void][Win]::MoveWindow($proc.MainWindowHandle, 20, 20, 1520, 940, $true)
    Start-Sleep -Milliseconds 1800
    $root = $AE::FromHandle($proc.MainWindowHandle)

    # A look to record: warm front wash, blue movers.
    foreach ($line in @(
            'Universe 2 Label "Dimmer rack" Unicast 192.168.1.50',
            'Patch Fixture 31 Thru 34 Type RGBW At 2.1 Label "LED bar"',
            'Group 1', 'At 80', 'Color "Warm White"',
            'Record Cue 1 "Warm Wash"',
            'Group 2', 'At 100', 'Color Blue', 'Pan At 30', 'Tilt At 65',
            'Record Cue 2 "Blue Movers"',
            'ClearAll', 'Go Cuelist 1', 'Go Cuelist 1',
            'Fader 1 Cuelist 1', 'Fader 1 Label "Looks"', 'Fader 2 Group 1', 'Fader 2 Color Blue', 'Fader 3 Group 2', 'Fader 3 Color Orange', 'Fader 4 Master',
            'Fader 1 At 100', 'Fader 2 At 70', 'Fader 3 At 40',
            'Fixture 3 Thru 5', 'At 100')) {
        Command $root $line
    }

    Press $root 'NavFixtures'
    Press $root 'AttrTabColor'
    Save-Window $proc.MainWindowHandle (Join-Path $OutDir 'overview.png')

    Press $root 'NavPatch';     Save-Window $proc.MainWindowHandle (Join-Path $OutDir 'patch.png')
    Press $root 'NavProfiles'
    $list = Find-ById $root 'ProfileList'
    $items = $list.FindAll('Children', (New-Object System.Windows.Automation.PropertyCondition($AE::IsControlElementProperty, $true)))
    $items[5].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 600
    Press $root 'CopyProfile'
    Save-Window $proc.MainWindowHandle (Join-Path $OutDir 'profiles.png')
    Press $root 'NavUniverses'; Save-Window $proc.MainWindowHandle (Join-Path $OutDir 'universes.png')
    Press $root 'NavRemote';    Save-Window $proc.MainWindowHandle (Join-Path $OutDir 'remote.png')
    "Saved screenshots to $OutDir"
}
finally {
    $listener.Close()
    if (-not $proc.HasExited) {
        [void]$proc.CloseMainWindow()
        if (-not $proc.WaitForExit(5000)) { $proc.Kill() }
    }
    Remove-Item Env:NXDMX_DATA -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $data -ErrorAction SilentlyContinue
}

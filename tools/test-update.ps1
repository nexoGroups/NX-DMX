# Installs an OLDER Setup.exe into a throw-away folder, lets it update itself and checks that the new version arrived.
# Without -Feed the app asks GitHub (the real thing, for after a release is published). With -Feed (a folder or URL with
# Velopack files from tools/pack.ps1) it asks that instead, to test before publishing.
#   powershell -File tools/test-update.ps1 -Setup old\NxDmx-win-Setup.exe -ExpectVersion 0.2.0
#   powershell -File tools/test-update.ps1 -Setup old\NxDmx-win-Setup.exe -ExpectVersion 0.9.1 -Feed Releases-test
# The app sends Art-Net to loopback during the test, so nothing reaches the network. Port 6454 must be free.
# Exit code 0 = the update was installed, 1 = it was not.
param(
    [Parameter(Mandatory)] [string]$Setup,
    [Parameter(Mandatory)] [string]$ExpectVersion,
    [string]$Feed,
    [ValidateSet('yes', 'no')] [string]$Answer = 'yes'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NxWin {
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
}
"@
$AE = [System.Windows.Automation.AutomationElement]
function Prop($property, $value) { New-Object System.Windows.Automation.PropertyCondition($property, $value) }

$Setup = (Resolve-Path $Setup).Path
if ($Feed) { $Feed = (Resolve-Path $Feed).Path }
$installDir = Join-Path ([IO.Path]::GetTempPath()) ("nxdmx-update-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8))

function Installed-Version { (Get-Item (Join-Path $installDir 'current\NX-DMX.exe')).VersionInfo.ProductVersion }

# The app autosaves into %AppData%\NX-DMX. Remove that again afterwards unless it belonged to the user before the test.
$appData = Join-Path $env:APPDATA 'NX-DMX'
$hadAppData = Test-Path $appData

$listener = $null
$proc = $null
$ok = $false
try {
    $install = Start-Process -FilePath $Setup -ArgumentList '--silent', '--installto', "`"$installDir`"" -PassThru
    if (-not $install.WaitForExit(180000) -or $install.ExitCode -ne 0) { throw 'Setup did not finish cleanly' }
    "installed      : " + (Installed-Version)

    # Something must listen on the Art-Net port, otherwise Windows reports the loopback sends as failed.
    $listener = New-Object System.Net.Sockets.UdpClient (New-Object System.Net.IPEndPoint ([System.Net.IPAddress]::Loopback), 6454)
    $null = [System.Threading.Tasks.Task]::Run([Action]{
        $ep = New-Object System.Net.IPEndPoint ([System.Net.IPAddress]::Any), 0
        try { while ($true) { [void]$listener.Receive([ref]$ep) } } catch { }
    })

    # Versions from 0.3.0 on read their settings from NXDMX_DATA: Art-Net to loopback, no remote servers. Older ones ignore it
    # and get the target typed in below.
    $dataDir = Join-Path ([IO.Path]::GetTempPath()) ('nxdmx-update-data-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force $dataDir | Out-Null
    '{ "BroadcastAddress": "127.0.0.1", "HttpEnabled": false, "OscEnabled": false, "TextEnabled": false }' | Set-Content -Encoding UTF8 (Join-Path $dataDir 'settings.json')
    $env:NXDMX_DATA = $dataDir

    if ($Feed) { $env:NXDMX_UPDATE_SOURCE = $Feed } else { Remove-Item Env:NXDMX_UPDATE_SOURCE -ErrorAction SilentlyContinue }
    $proc = Start-Process -FilePath (Join-Path $installDir 'current\NX-DMX.exe') -PassThru
    for ($i = 0; $i -lt 120 -and $proc.MainWindowHandle -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 250; $proc.Refresh() }
    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) { throw 'main window did not appear' }
    $root = $AE::FromHandle($proc.MainWindowHandle)
    "window title   : " + $root.Current.Name

    function Find-Id($id) { $root.FindFirst('Descendants', (Prop $AE::AutomationIdProperty $id)) }
    # Before 0.3.0 the Art-Net target was a field in the main window.
    $target = Find-Id 'TargetIp'
    if ($target) {
        $target.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('127.0.0.1')
        $apply = $root.FindFirst('Descendants', (New-Object System.Windows.Automation.AndCondition((Prop $AE::NameProperty 'Apply'), (Prop $AE::ControlTypeProperty ([System.Windows.Automation.ControlType]::Button)))))
        $apply.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    }

    $button = $null
    for ($i = 0; $i -lt 120 -and -not $button; $i++) { $button = Find-Id 'UpdateButton'; if (-not $button) { Start-Sleep -Milliseconds 500 } }
    if (-not $button) { throw 'no update button appeared within 60 s' }
    "update button  : " + $button.Current.Name
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    # The confirmation dialog hangs below the main window in the UI Automation tree. Its buttons show up as panes, so
    # they are clicked through their window handle.
    $dialog = $null
    for ($i = 0; $i -lt 240 -and -not $dialog; $i++) { $dialog = $root.FindFirst('Descendants', (Prop $AE::NameProperty 'NX-DMX update')); if (-not $dialog) { Start-Sleep -Milliseconds 500 } }
    if (-not $dialog) { throw 'the update dialog did not appear (download failed?)' }
    $names = if ($Answer -eq 'yes') { 'Yes', 'Ja' } else { 'No', 'Nein' }
    $choice = $null
    for ($i = 0; $i -lt 30 -and -not $choice; $i++) {
        foreach ($n in $names) { $choice = $dialog.FindFirst('Descendants', (Prop $AE::NameProperty $n)); if ($choice) { break } }
        if (-not $choice) { Start-Sleep -Milliseconds 300 }
    }
    if (-not $choice) { throw 'dialog button not found' }
    [void][NxWin]::SendMessage([IntPtr]$choice.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)   # BM_CLICK
    "answered       : " + $choice.Current.Name

    if ($Answer -eq 'no') {
        Start-Sleep -Seconds 1
        [void]$proc.CloseMainWindow()   # the update is applied when the app closes
    }
    if (-not $proc.WaitForExit(60000) -and $Answer -eq 'yes') { throw 'the app did not exit to install the update' }

    # Wait for the updater to swap the files.
    for ($i = 0; $i -lt 60 -and -not (Installed-Version).StartsWith($ExpectVersion); $i++) { Start-Sleep -Seconds 1 }
    "installed now  : " + (Installed-Version)
    $ok = (Installed-Version).StartsWith($ExpectVersion)

    if ($ok -and $Answer -eq 'yes') {
        # "Yes" restarts the app by itself: the new process must show the new version in its title.
        $restarted = $null
        for ($i = 0; $i -lt 40 -and -not $restarted; $i++) {
            $restarted = Get-Process -Name 'NX-DMX' -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
            if (-not $restarted) { Start-Sleep -Milliseconds 500 }
        }
        $title = if ($restarted) { $AE::FromHandle($restarted.MainWindowHandle).Current.Name } else { '<no restarted window>' }
        "restarted title: $title"
        $ok = $title -like "*$ExpectVersion*"
    }
}
catch {
    "ERROR: " + $_.Exception.Message
}
finally {
    if ($listener) { $listener.Close() }
    Get-Process -Name 'NX-DMX' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$installDir*" } | Stop-Process -Force
    $updater = Join-Path $installDir 'Update.exe'
    if (Test-Path $updater) {
        & $updater --silent uninstall *> $null    # removes shortcuts and the uninstall entry
        Start-Sleep -Seconds 8                    # it deletes the folder itself after a delay
    }
    if (Test-Path $installDir) { Remove-Item -Recurse -Force $installDir -ErrorAction SilentlyContinue }
    if (-not $hadAppData) { Remove-Item -Recurse -Force $appData -ErrorAction SilentlyContinue }
    if ($dataDir) { Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue }
    Remove-Item Env:NXDMX_DATA -ErrorAction SilentlyContinue
}

if ($ok) { "RESULT: PASS (update to $ExpectVersion installed)"; exit 0 }
"RESULT: FAIL"
exit 1

$ErrorActionPreference = 'Stop'
try {
    $root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    if ($PSVersionTable.PSVersion.Major -ne 5 -or -not [Environment]::Is64BitProcess) {
        throw 'Use 64-bit Windows PowerShell 5.1.'
    }
    $windows = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    if ([int]$windows.CurrentBuildNumber -lt 22000) { throw 'Windows 11 or later is required.' }
    $framework = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full'
    if ([int]$framework.Release -lt 533320) { throw '.NET Framework 4.8.1 Runtime is required.' }
    foreach ($relative in @('bin\LCET.dll', 'web\index.html', 'web\app.js', 'web\app.css', 'config\defaults.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $relative))) { throw "Required file missing: $relative" }
    }
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\Application\msedge.exe')
    )
    foreach ($key in @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe', 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe')) {
        if (Test-Path $key) { $candidates += (Get-Item $key).GetValue('') }
    }
    $edge = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    if (-not $edge) { throw 'Microsoft Edge was not found. Ask IT to install Edge 148 or newer.' }
    if ((Get-Item -LiteralPath $edge).VersionInfo.FileMajorPart -lt 148) { throw 'Microsoft Edge 148 or newer is required.' }
    Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml, UIAutomationClient, UIAutomationTypes, System.Web.Extensions
    Add-Type -Path (Join-Path $root 'bin\LCET.dll')
    [LCET.Entry]::Run($root, $edge)
    exit 0
} catch {
    Write-Host ('LCET startup failed: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'No policy changes were made. If blocked by policy, ask IT to authorize this folder and its launcher.'
    exit 1
}
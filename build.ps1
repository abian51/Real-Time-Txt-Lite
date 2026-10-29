param([switch]$Test, [switch]$Exe)
$ErrorActionPreference = 'Stop'
if ($Exe -and $Test) { throw 'Use -Exe for build only; run library tests separately without -Exe.' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'Install Visual Studio Build Tools with .NET desktop build tools and .NET Framework 4.8.1 development tools.' }
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild not found.' }
$buildOptions = @('/t:Rebuild', '/p:Configuration=Release', '/nologo', '/verbosity:minimal')
if ($Exe) { $buildOptions += '/p:BuildExecutable=true' }
& $msbuild (Join-Path $PSScriptRoot 'src\LCET\LCET.csproj') @buildOptions
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($Test) {
    foreach ($file in @('Test-Captions.ps1', 'Test-Loopback.ps1', 'Test-Window.ps1')) {
        & powershell.exe -NoProfile -STA -File (Join-Path $PSScriptRoot "tests\$file") -Root $PSScriptRoot
        if ($LASTEXITCODE -ne 0) { throw "$file failed." }
    }
    if (Get-Command node.exe -ErrorAction SilentlyContinue) {
        & node.exe --test (Join-Path $PSScriptRoot 'tests\Test-Web.cjs')
        if ($LASTEXITCODE -ne 0) { throw 'Browser scheduling tests failed.' }
    } else { Write-Warning 'Node.js not found; browser scheduling tests were skipped (development only).' }
}
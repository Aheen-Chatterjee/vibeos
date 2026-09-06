# VibeOS installer: publishes Release and installs to %LOCALAPPDATA%\VibeOS,
# with a Desktop shortcut. Run from the repo root:
#   powershell -ExecutionPolicy Bypass -File scripts/install.ps1 [-Autostart]
param([switch]$Autostart)

$ErrorActionPreference = 'Stop'
$appRoot = Join-Path $PSScriptRoot '..'
$installDir = Join-Path $env:LOCALAPPDATA 'VibeOS'

Write-Host 'Publishing VibeOS (Release, win-x64)...'
dotnet publish (Join-Path (Join-Path $appRoot 'src') 'VibeOS.App') -c Release -r win-x64 --self-contained false -o $installDir
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

function New-Shortcut($path, $target, $arguments, $workDir, $desc) {
  $shell = New-Object -ComObject WScript.Shell
  $link = $shell.CreateShortcut($path)
  $link.TargetPath = $target
  $link.Arguments = $arguments
  $link.WorkingDirectory = $workDir
  $link.Description = $desc
  $link.Save()
}

$exe = Join-Path $installDir 'VibeOS.exe'
New-Shortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) 'VibeOS.lnk') `
  $exe '--tray' $installDir 'VibeOS controller input (tray)'
Write-Host 'Desktop shortcut created.'

if ($Autostart) {
  New-Shortcut (Join-Path ([Environment]::GetFolderPath('Startup')) 'VibeOS.lnk') `
    $exe '--tray' $installDir 'VibeOS controller input (starts hidden in tray)'
  Write-Host 'Autostart enabled.'
}

Write-Host "Installed to $installDir"
Write-Host 'Launch it from the Desktop icon. First voice use downloads base.en (~140 MB) once.'

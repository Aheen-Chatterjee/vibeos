# VibeOS uninstaller: stops the app, removes %LOCALAPPDATA%\VibeOS and shortcuts.
$ErrorActionPreference = 'SilentlyContinue'
Get-Process VibeOS -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1
Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) 'VibeOS.lnk') -Force
Remove-Item (Join-Path ([Environment]::GetFolderPath('Startup')) 'VibeOS.lnk') -Force
Remove-Item (Join-Path $env:LOCALAPPDATA 'VibeOS') -Recurse -Force
Write-Host 'VibeOS uninstalled. Models in %LOCALAPPDATA%\VibeOS were removed; voice will re-download on reinstall.'

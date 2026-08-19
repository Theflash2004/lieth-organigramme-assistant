$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publish = Join-Path $root 'publish'
dotnet publish (Join-Path $root 'LiethOrganigrammeAssistant.csproj') -c Release -r win-x64 --self-contained true -o $publish
dotnet publish (Join-Path $root 'Updater\LiethUpdater.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $publish 'updater')
Copy-Item (Join-Path $publish 'updater\LiethUpdater.exe') (Join-Path $publish 'LiethUpdater.exe') -Force
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
if (!(Test-Path -LiteralPath $iscc)) { throw 'Inno Setup 6 est requis pour créer l’installateur.' }
& $iscc (Join-Path $root 'installer.iss')
$installer = Join-Path $root 'Output\LiethOrganigrammeAssistant-Setup.exe'
(Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant() + '  LiethOrganigrammeAssistant-Setup.exe' | Set-Content (Join-Path (Split-Path $installer) 'LiethOrganigrammeAssistant-Setup.exe.sha256')

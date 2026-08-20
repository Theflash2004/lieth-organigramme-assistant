$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publish = Join-Path $root 'publish'
$updaterPublish = Join-Path $root 'updater-publish'
$output = Join-Path $root 'Output'
$signingKey = if ($env:DIVA_UPDATE_SIGNING_KEY) { $env:DIVA_UPDATE_SIGNING_KEY } else { Join-Path $env:APPDATA 'Diva Assistant Release Signing\update-signing-private-key.pem' }
$publicKey = 'MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEMPsp/EGsXzr11vJ375kYs1GWPDw8jK44kIfo+U5dq4c0rtxl+8Y/dHrSy0bgBEpGJ6Co7Md2lqoe/2Ow9mPuSw=='

foreach ($path in @($publish, $updaterPublish, $output)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}

dotnet publish (Join-Path $root 'DivaAssistant.csproj') -c Release -r win-x64 --self-contained true -o $publish
dotnet publish (Join-Path $root 'Updater\LiethUpdater.csproj') -c Release -r win-x64 --self-contained true -o $updaterPublish
Copy-Item (Join-Path $updaterPublish 'DivaUpdater.exe') (Join-Path $publish 'DivaUpdater.exe') -Force

foreach ($program in @((Join-Path $publish 'DivaAssistant.exe'), (Join-Path $publish 'DivaUpdater.exe'))) {
    $check = Start-Process $program -ArgumentList '--self-check' -Wait -PassThru
    if ($check.ExitCode -ne 0) { throw "Auto-vérification échouée pour $program (code $($check.ExitCode))." }
}

$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
if (!(Test-Path -LiteralPath $iscc)) { throw 'Inno Setup 6 est requis pour créer l’installateur.' }
& $iscc (Join-Path $root 'installer.iss')

$installer = Join-Path $output 'DivaAssistant-Setup.exe'
$hash = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$length = (Get-Item -LiteralPath $installer).Length
$manifest = Join-Path $output 'DivaAssistant-update.json'
$signature = Join-Path $output 'DivaAssistant-update.json.sig'
[IO.File]::WriteAllText($manifest, "{`"version`":`"2.0.1`",`"asset`":`"DivaAssistant-Setup.exe`",`"sha256`":`"$hash`",`"length`":$length}", [Text.UTF8Encoding]::new($false))
if (!(Test-Path -LiteralPath $signingKey)) { throw "Clé de signature absente : $signingKey" }
dotnet run --project (Join-Path $root 'tools\SigningKeyGen\SigningKeyGen.csproj') -c Release -- --sign $signingKey $manifest $signature
dotnet run --project (Join-Path $root 'tools\SigningKeyGen\SigningKeyGen.csproj') -c Release -- --verify $publicKey $manifest $signature
[IO.File]::WriteAllText((Join-Path $output 'DivaAssistant-Setup.exe.sha256'), "$hash  DivaAssistant-Setup.exe`r`n", [Text.Encoding]::ASCII)

# Compatibility assets let installed Lieth 1.1.1 clients cross the rename once.
Copy-Item $installer (Join-Path $output 'LiethOrganigrammeAssistant-Setup.exe')
"$hash  LiethOrganigrammeAssistant-Setup.exe" | Set-Content (Join-Path $output 'LiethOrganigrammeAssistant-Setup.exe.sha256') -Encoding ascii

Remove-Item -LiteralPath $updaterPublish -Recurse -Force

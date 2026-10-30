$ErrorActionPreference = 'Stop'
Add-Type -Path "$PSScriptRoot/../bin/InstallerCheck/Playnite.SDK.dll"
Add-Type -Path "$PSScriptRoot/../bin/InstallerCheck/Branvik.EmulatorUpdater.dll"
$repo = 'RPCS3/rpcs3-binaries-win'
$release = New-Object 'Branvik.EmulatorUpdater.UpdateChecker+Release'
$release.assets = New-Object 'System.Collections.Generic.List[Branvik.EmulatorUpdater.UpdateChecker+Asset]'
foreach ($name in @('rpcs3-v0.0.42-19759-ffc50905_win64_msvc.7z', 'rpcs3-v0.0.42-19759-ffc50905_win64_msvc.7z.sha256', 'source.zip', 'rpcs3-v0.0.42_linux64.7z')) {
    $asset = New-Object 'Branvik.EmulatorUpdater.UpdateChecker+Asset'
    $asset.name = $name
    $release.assets.Add($asset)
}
if ([Branvik.EmulatorUpdater.UpdateInstaller]::SelectAsset($release, $repo).name -ne $release.assets[0].name) { throw 'Wrong asset' }
$release.assets.Add($release.assets[0])
$rejected = $false
try { [Branvik.EmulatorUpdater.UpdateInstaller]::SelectAsset($release, $repo) } catch { $rejected = $true }
if (-not $rejected) { throw 'Ambiguous release accepted' }
$root = Join-Path $PSScriptRoot ('fixtures-' + [guid]::NewGuid().ToString('N'))
$stage = Join-Path $root 'stage'
$target = Join-Path $root 'rpcs3'
New-Item -ItemType Directory -Force $stage,$target | Out-Null
$protected = @('dev_hdd0/home/00000001/savedata/save.bin', 'dev_flash/sys/firmware.bin', 'dev_flash2/config.bin', 'dev_usb000/game.bin', 'config/custom_configs/game.yml', 'GuiConfigs/CurrentSettings.ini', 'patches/patch.yml', 'config.yml', 'games.yml')
foreach ($relative in $protected) {
    foreach ($folder in @($stage, $target)) {
        $path = Join-Path $folder $relative
        New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
        Set-Content $path $(if ($folder -eq $stage) { 'archive' } else { 'user' })
    }
}
Set-Content "$target/rpcs3.exe" 'old'
Set-Content "$stage/rpcs3.exe" 'new'
Set-Content "$stage/Qt6Core.dll" 'library'
$backup = [Branvik.EmulatorUpdater.UpdateInstaller]::Apply($stage, $target)
foreach ($relative in $protected) { if ((Get-Content (Join-Path $target $relative)) -ne 'user') { throw "Overwritten: $relative" } }
if ((Get-Content "$target/rpcs3.exe") -ne 'new') { throw 'Binary not replaced' }
if ((Get-Content "$backup/files/rpcs3.exe") -ne 'old') { throw 'Backup missing' }
if (-not (Test-Path "$target/Qt6Core.dll")) { throw 'Dependency missing' }
Write-Output 'PASS: RPCS3 archive selection, ambiguity rejection, data preservation, binary replacement, backup'

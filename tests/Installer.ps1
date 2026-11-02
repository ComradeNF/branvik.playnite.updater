$ErrorActionPreference = 'Stop'
Add-Type -Path "$PSScriptRoot/../bin/InstallerCheck/Playnite.SDK.dll"
Add-Type -Path "$PSScriptRoot/../bin/InstallerCheck/Branvik.EmulatorUpdater.dll"
$testRoot = Join-Path $PSScriptRoot ('fixtures-' + [guid]::NewGuid().ToString('N'))
$stage = Join-Path $testRoot 'stage'
$target = Join-Path $testRoot 'emulator'
New-Item -ItemType Directory -Force $stage,$target,"$stage/inis","$target/inis" | Out-Null
Set-Content "$stage/pcsx2-qt.exe" 'new'
Set-Content "$target/pcsx2-qt.exe" 'old'
Set-Content "$stage/inis/settings.ini" 'archive defaults'
Set-Content "$target/inis/settings.ini" 'user settings'
Set-Content "$stage/new.dll" 'new library'
$backup = [Branvik.EmulatorUpdater.UpdateInstaller]::Apply($stage,$target,$false)
if ((Get-Content "$target/pcsx2-qt.exe") -ne 'new') { throw 'Replacement failed' }
if ((Get-Content "$backup/files/pcsx2-qt.exe") -ne 'old') { throw 'Backup failed' }
if ((Get-Content "$target/inis/settings.ini") -ne 'user settings') { throw 'User data overwritten' }
if (-not (Test-Path "$target/new.dll")) { throw 'New file missing' }
foreach ($entry in @('../outside.exe','C:\outside.exe','folder/file:stream','folder./file')) {
    $rejected = $false
    try { [Branvik.EmulatorUpdater.UpdateInstaller]::ValidateEntry($entry) } catch { $rejected = $true }
    if (-not $rejected) { throw "Unsafe path accepted: $entry" }
}
$stage2 = Join-Path $testRoot 'stage2'
$target2 = Join-Path $testRoot 'target2'
New-Item -ItemType Directory $stage2,$target2 | Out-Null
Set-Content "$stage2/a.dll" 'new-a'
Set-Content "$target2/a.dll" 'old-a'
Set-Content "$stage2/z.dll" 'new-z'
Set-Content "$target2/z.dll" 'old-z'
$lock = [IO.File]::Open("$target2/z.dll",[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try {
    $failed = $false
    try { [Branvik.EmulatorUpdater.UpdateInstaller]::Apply($stage2,$target2,$false) } catch { $failed = $true }
    if (-not $failed) { throw 'Locked file should fail' }
    if ((Get-Content "$target2/a.dll") -ne 'old-a') { throw 'Rollback failed' }
} finally { $lock.Dispose() }
Write-Output "PASS: replacement, backup, preserved settings, new file, unsafe paths, rollback on locked file. Fixtures: $testRoot"

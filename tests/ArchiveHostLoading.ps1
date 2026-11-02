$ErrorActionPreference = 'Stop'
Add-Type -Path "$PSScriptRoot/../bin/InstallerCheck/Playnite.SDK.dll"
[Reflection.Assembly]::LoadFrom("$env:LOCALAPPDATA/Playnite/SharpCompress.dll") | Out-Null
[Reflection.Assembly]::LoadFile((Resolve-Path "$PSScriptRoot/../bin/InstallerCheck/Branvik.EmulatorUpdater.dll").Path) | Out-Null
Add-Type -AssemblyName System.IO.Compression
$root = Join-Path $PSScriptRoot ('fixtures-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $root | Out-Null
function MakeZip($path, $name) {
    $stream = [IO.File]::Create($path)
    $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
    $entry = $zip.CreateEntry($name)
    $writer = New-Object IO.StreamWriter($entry.Open())
    $writer.Write('new'); $writer.Dispose(); $zip.Dispose(); $stream.Dispose()
}
MakeZip "$root/good.zip" 'emulator.exe'
[Branvik.EmulatorUpdater.UpdateInstaller]::Extract("$root/good.zip", "$root/stage", [Threading.CancellationToken]::None)
if ((Get-Content "$root/stage/emulator.exe") -ne 'new') { throw 'ZIP extraction failed' }
MakeZip "$root/bad.zip" '../escaped.txt'
$rejected = $false
try { [Branvik.EmulatorUpdater.UpdateInstaller]::Extract("$root/bad.zip", "$root/rejected", [Threading.CancellationToken]::None) } catch { $rejected = $true }
if (-not $rejected -or (Test-Path "$root/escaped.txt")) { throw 'Traversal accepted' }
New-Item -ItemType Directory "$root/target" | Out-Null
Set-Content "$root/target/emulator.exe" 'old'
Set-Content "$root/target/custom.dat" 'user'
Set-Content "$root/stage/custom.dat" 'default'
[Branvik.EmulatorUpdater.UpdateInstaller]::Apply("$root/stage", "$root/target", $true) | Out-Null
if ((Get-Content "$root/target/custom.dat") -ne 'user') { throw 'Generic data preservation failed' }
Write-Output 'PASS: managed ZIP extraction, traversal rejection, generic data preservation'
# Optional fixture creation only: the plugin itself never launches 7-Zip.
$seven = 'C:/Program Files (x86)/7-Zip/7z.exe'
if (Test-Path $seven) {
    & $seven a -ms=on "$root/sample.7z" "$root/stage/emulator.exe" "$root/stage/custom.dat" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Fixture creation failed' }
    [Branvik.EmulatorUpdater.UpdateInstaller]::Extract("$root/sample.7z", "$root/seven", [Threading.CancellationToken]::None)
    if ((Get-Content "$root/seven/emulator.exe") -ne 'new') { throw '7z extraction failed' }
    Write-Output 'PASS: managed 7z extraction'
}


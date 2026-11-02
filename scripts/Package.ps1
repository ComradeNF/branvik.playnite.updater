param([string]$BuildDirectory = "$PSScriptRoot/../bin/Release/net462", [string]$OutputDirectory = "$PSScriptRoot/../outputs")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$manifest = Get-Content (Join-Path $BuildDirectory 'extension.yaml') -Raw
$version = [regex]::Match($manifest, '(?m)^Version:\s*(\d+\.\d+\.\d+)\s*$').Groups[1].Value
if (-not $version) { throw 'Missing package version' }
$package = Join-Path $OutputDirectory "Branvik.EmulatorUpdater_$version.pext"
$temporary = $package + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
$zip = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in @('Branvik.EmulatorUpdater.dll','SharpCompress.dll','System.Memory.dll','System.Buffers.dll','System.Runtime.CompilerServices.Unsafe.dll','System.Numerics.Vectors.dll','Microsoft.Bcl.AsyncInterfaces.dll','System.Text.Encoding.CodePages.dll','System.Threading.Tasks.Extensions.dll','extension.yaml')) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $BuildDirectory $name), $name) | Out-Null
    }
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$PSScriptRoot/../SharpCompress-LICENSE.txt", 'SharpCompress-LICENSE.txt') | Out-Null
} finally { $zip.Dispose() }
Move-Item -LiteralPath $temporary -Destination $package -Force
Write-Output $package

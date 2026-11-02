param([string]$BuildDirectory = "$PSScriptRoot/../bin/Release-0.2.0", [string]$OutputDirectory = "$PSScriptRoot/../outputs")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$package = Join-Path $OutputDirectory 'Branvik.EmulatorUpdater_0.2.0.pext'
if (Test-Path $package) { throw "Package already exists: $package" }
$zip = [IO.Compression.ZipFile]::Open($package, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in @('Branvik.EmulatorUpdater.dll','SharpCompress.dll','System.Memory.dll','System.Buffers.dll','System.Runtime.CompilerServices.Unsafe.dll','System.Numerics.Vectors.dll','Microsoft.Bcl.AsyncInterfaces.dll','System.Text.Encoding.CodePages.dll','System.Threading.Tasks.Extensions.dll','extension.yaml')) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $BuildDirectory $name), $name) | Out-Null
    }
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$PSScriptRoot/../SharpCompress-LICENSE.txt", 'SharpCompress-LICENSE.txt') | Out-Null
} finally { $zip.Dispose() }
Write-Output $package

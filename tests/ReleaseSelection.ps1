param([string]$BuildDirectory = "$PSScriptRoot/../bin/ChannelCheck")
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $BuildDirectory 'Playnite.SDK.dll')
Add-Type -Path (Join-Path $BuildDirectory 'Branvik.EmulatorUpdater.dll')
function Release($tag, $date, $preview, $draft) {
    $r = New-Object 'Branvik.EmulatorUpdater.UpdateChecker+Release'
    $r.tag_name = $tag; $r.published_at = $date; $r.prerelease = $preview; $r.draft = $draft
    return $r
}
$stable = Release 'v2.8.2' '2026-09-01T00:00:00Z' $false $false
$nightly = Release 'v2.9.1' '2026-10-01T00:00:00Z' $true $false
$draft = Release 'v3.0' '2026-10-02T00:00:00Z' $true $true
$items = [Branvik.EmulatorUpdater.UpdateChecker+Release[]]@($draft, $stable, $nightly)
if ([Branvik.EmulatorUpdater.UpdateChecker]::SelectNewest($items).tag_name -ne 'v2.9.1') { throw 'Nightly selection or draft exclusion failed' }
$stable.published_at = '2026-10-03T00:00:00Z'
if ([Branvik.EmulatorUpdater.UpdateChecker]::SelectNewest($items).tag_name -ne 'v2.8.2') { throw 'Newer stable release should win' }
if ($null -ne [Branvik.EmulatorUpdater.UpdateChecker]::SelectNewest([Branvik.EmulatorUpdater.UpdateChecker+Release[]]@())) { throw 'Empty list failed' }
if ($null -ne [Branvik.EmulatorUpdater.UpdateChecker]::SelectNewest([Branvik.EmulatorUpdater.UpdateChecker+Release[]]@($draft))) { throw 'Draft-only list failed' }
$item = New-Object Branvik.EmulatorUpdater.Installation
if ($item.IncludePrereleases) { throw 'Default must remain stable' }
Add-Type -AssemblyName System.Web.Extensions
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$item.IncludePrereleases = $true
$restored = $serializer.Deserialize($serializer.Serialize($item), [Branvik.EmulatorUpdater.Installation])
if (-not $restored.IncludePrereleases) { throw 'Setting did not survive JSON round trip' }
Write-Output 'PASS: nightly, newer stable, draft exclusion, empty list, stable default, JSON round trip'

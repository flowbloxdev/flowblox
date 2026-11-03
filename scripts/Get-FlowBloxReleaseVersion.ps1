<#
.SYNOPSIS
Validates a stable release tag against Windows Installer version limits.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Tag
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Tag -cnotmatch '^v(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)$') {
    throw "Invalid release tag '$Tag'. Use vMAJOR.MINOR.PATCH, for example v1.2.3."
}

$versionParts = @($Matches.major, $Matches.minor, $Matches.patch)
$limits = @(255, 255, 65535)
for ($index = 0; $index -lt $versionParts.Count; $index++) {
    $component = 0
    if (-not [int]::TryParse($versionParts[$index], [ref]$component) -or $component -gt $limits[$index]) {
        throw "Release tag '$Tag' exceeds Windows Installer limits (255.255.65535)."
    }
}

return $Tag.Substring(1)

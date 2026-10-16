param(
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'osx-arm64', 'osx-x64')][string]$Channel,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$feedName = "releases.$Channel.json"
# Only a genuinely absent baseline permits a full-only first release. API/download
# errors must fail the build instead of silently losing incremental updates.
$pages = gh api "repos/$Repository/releases" --paginate --slurp | ConvertFrom-Json
$releases = @($pages | ForEach-Object { $_ } | Where-Object {
    -not $_.draft -and -not $_.prerelease -and $feedName -in $_.assets.name
} | Sort-Object published_at -Descending)
if ($releases.Count -eq 0) {
    Write-Host "No published $Channel baseline; creating the first full release."
    return
}
$baseline = $releases[0]
gh release download $baseline.tag_name --repo $Repository --pattern $feedName --dir $OutputDirectory
$feed = Get-Content (Join-Path $OutputDirectory $feedName) -Raw | ConvertFrom-Json
$fullPackages = @($feed.Assets | Where-Object { $_.Type -eq 'Full' })
if ($fullPackages.Count -eq 0) { throw "Baseline feed contains no full packages." }
# Carry forward the delta chain too, so clients which skipped a release can
# still update incrementally from the newest published feed.
foreach ($asset in $feed.Assets | Where-Object { $_.Type -in @('Full', 'Delta') }) {
    if ([IO.Path]::GetFileName($asset.FileName) -ne $asset.FileName -or $asset.FileName -notlike '*.nupkg') {
        throw 'Invalid package filename in baseline feed.'
    }
    gh release download $baseline.tag_name --repo $Repository --pattern $asset.FileName --dir $OutputDirectory
}
Write-Host "Downloaded baseline from $($baseline.tag_name)."

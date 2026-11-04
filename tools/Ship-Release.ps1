<#
Publishes the version in Directory.Build.props to GitHub with one command: pushes main and the v<version> tag, then
waits for the release workflow, which builds the packages and publishes the release as Latest (target: under 4 minutes).
Before pushing it checks only what would make the release fail or leak: on main, nothing uncommitted, release notes
present, tag unused, no private files tracked. See docs/guides/RELEASING.md.
#>
[CmdletBinding()]
param([string]$Remote = 'public')

$ErrorActionPreference = 'Stop'
$srcRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $srcRoot
$version = ([xml][IO.File]::ReadAllText((Join-Path $srcRoot 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$tag = "v$version"

$branch = (& git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'main') { throw "Releases go out from main; this is '$branch'." }
if (@(& git status --porcelain).Count -gt 0) { throw 'There are uncommitted changes. Commit them first.' }
if (-not (Test-Path -LiteralPath "docs/releases/$version.md")) { throw "docs/releases/$version.md (the release notes) is missing." }
if (& git ls-remote --tags $Remote "refs/tags/$tag") { throw "$tag already exists on $Remote. Raise <Version> in Directory.Build.props." }
$private = @(& git ls-files | Where-Object { $_ -like '*manifest.private*' -or $_ -like 'shots-*' })
if ($private.Count -gt 0) { throw "Private files are tracked: $($private[0]) (and $($private.Count - 1) more)." }

$started = Get-Date
& git push $Remote HEAD:main
if ($LASTEXITCODE) { throw 'Pushing main failed.' }
& git tag $tag
& git push $Remote $tag
if ($LASTEXITCODE) { throw "Pushing $tag failed." }

$repo = ((& git remote get-url $Remote) -replace '^https://github.com/', '' -replace '\.git$', '')
$runId = $null
for ($i = 0; $i -lt 30 -and -not $runId; $i++) {
    Start-Sleep -Seconds 3
    $runId = & gh run list -R $repo --workflow release.yml --branch $tag --limit 1 --json databaseId --jq '.[0].databaseId'
}
if (-not $runId) { throw "The release workflow did not start. Check https://github.com/$repo/actions" }
& gh run watch $runId -R $repo --exit-status --interval 5
$failed = $LASTEXITCODE
$elapsed = (Get-Date) - $started
if ($failed) { throw "The release failed after $([int]$elapsed.TotalSeconds)s: https://github.com/$repo/actions/runs/$runId" }
Write-Output ("Released {0} in {1:m\:ss}: https://github.com/{2}/releases/tag/{0}" -f $tag, $elapsed, $repo)

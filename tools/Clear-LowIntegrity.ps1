<#
.SYNOPSIS
    Keeps a freshly built TabForge at normal (Medium) integrity. BUILD.cmd, REBUILD.cmd and RUN-AVALONIA.cmd call it right
    after building.
.DESCRIPTION
    Some agent sandboxes stamp their workspace folder with a Low integrity label (plus a sandbox SID rule). Every file built
    inside it inherits the label, and Windows starts a Low-labelled exe at Low integrity: the app then cannot write to
    Videos, Music, Documents or %LocalAppData% (recording and autosave fail with "Access is denied") and Windows treats the
    exe as an Internet download ("Open File - Security Warning" / SmartScreen prompts). This script relabels the build
    output Medium and warns when the workspace itself, or the shell running the build, is Low.
.PARAMETER Output
    The build output folder to relabel.
.NOTES
    Never fails the build: a label it cannot change is reported, not thrown.
#>
param([Parameter(Mandatory)][string]$Output)

if (-not (Test-Path -LiteralPath $Output)) { exit 0 }

function Test-LowLabel([string]$Path) { (icacls $Path 2>$null | Out-String) -match 'Low Mandatory Level' }

# A build shell running at Low integrity (an agent sandbox) starts the app at Low integrity whatever the file label says.
if ((& "$env:SystemRoot\System32\whoami.exe" /groups | Out-String) -match 'Low Mandatory Level') {
    Write-Host "WARNING: this build is running at Low integrity (inside a sandbox). TabForge started from here cannot record or"
    Write-Host "         save to your user folders. Start it from Explorer or a normal terminal instead."
}

# The workspace label is reported, not changed: it belongs to whichever tool set it.
$workspace = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (Test-LowLabel $workspace) {
    Write-Host "WARNING: '$workspace' has a Low integrity label (an agent sandbox sets this). Everything built there runs at"
    Write-Host "         Low integrity unless relabelled. To clear it for good, run in a normal terminal:"
    Write-Host "         icacls `"$workspace`" /setintegritylevel (OI)(CI)M /T /C /Q"
}

if (-not (Test-LowLabel $Output)) { exit 0 }
icacls $Output /setintegritylevel '(OI)(CI)M' /T /C /Q | Out-Null
if (Test-LowLabel $Output) {
    Write-Host "WARNING: '$Output' is still labelled Low integrity; TabForge built there will not be able to record or save."
} else {
    Write-Host "Cleared a Low integrity label from the build output (it would have blocked recording and saving)."
}
exit 0

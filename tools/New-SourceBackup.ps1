# Owns: the timestamped source-only zip of TabForge\Source in TabForge\Backups (source-yyyyMMdd-HHmmss.zip), without build output, the git folder or caches.
# Does not own: the build or the release package (Package-Release.ps1).
param([string]$Label = "source")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$source = Join-Path $root "Source"
$target = Join-Path $root "Backups"
New-Item -ItemType Directory -Force -Path $target | Out-Null
$zipPath = Join-Path $target ("{0}-{1}.zip" -f $Label, (Get-Date -Format "yyyyMMdd-HHmmss"))
$skipDirs = 'build', 'bin', 'obj', '.git', '.vs', 'node_modules', 'artifacts', 'TestResults'
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, "Create")
try {
    Get-ChildItem -Path $source -Recurse -File -Force | Where-Object {
        $dirParts = $_.DirectoryName.Substring($source.Length).Split([char[]]@('\'), [StringSplitOptions]::RemoveEmptyEntries)
        -not ($dirParts | Where-Object { $skipDirs -contains $_ }) -and $_.Extension -ine '.dmp'
    } | ForEach-Object {
        $entry = $_.FullName.Substring($root.Length + 1).Replace("\", "/")
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entry, "Optimal")
    }
}
finally { $zip.Dispose() }
Write-Output "Source backup: $zipPath"

# Compares the data a build writes (save copies, clean .gp exports, MusicXML, the offline render probe, default settings round trip) between an
# OLD and a NEW build, both run in the same test VM on the same inputs: the demo song from samples/ as .gp and as .gp5 (Tabs/ is never used).
# Usage: .\Run-DataAB.ps1 [-Old C:\TF\old] [-New C:\TF\main] [-Out $env:TEMP\vm-data-ab] [-VmName TabForge-VM|TabForge-VM2] [-CleanupInVm]
# Only TabForge-VM and TabForge-VM2 are used (TabForge-VM3 is reserved for the behaviour recorder). Every VM step goes through Invoke-InVm.
# Prints "DATA: N files, M differences" then one line per difference; exits 1 when any file differs or is missing on one side.
# -CleanupInVm removes this run's output folders and input copies from the VM after the results are copied back.
# Normalised before comparing (each rule is printed as NORMALISED with its file count, counted over both sides):
#   - GUIDs in .tforge JSON and in the project inside a .gp: the importers give new tracks random ids, so each id is numbered in order of
#     first appearance in its file (<guid 1>, <guid 2> ...). Equal structure still compares equal; a changed id pattern does not.
#   - ProjectSha256 in a .gp's TabForge/binding.json: it hashes that same project, so it is replaced by a placeholder.
#   - Zip container of a .gp: its entries are compared by name and content, not by the zip's own timestamps.
#   - Wall-clock render speed in a .txt report ("in 0.060 s = 58.2x realtime"): the probe's own bit-identical check still compares the WAVs.
param(
    [string] $Old = 'C:\TF\old',
    [string] $New = 'C:\TF\main',
    [string] $Out = "$env:TEMP\vm-data-ab",
    [string] $VmName = 'TabForge-VM',
    [switch] $CleanupInVm
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Arguments reach a VM command line, so only plain values pass: an absolute Windows path of letters, digits, spaces, _ . - and backslashes.
function Assert-PlainArg([string] $Value, [string] $Name) {
    if ($Value -notmatch '^[A-Za-z]:\\[A-Za-z0-9 _.\-\\]+$' -or $Value -match '\.\.') {
        throw "$Name must be a plain absolute Windows path (letters, digits, space, _ . - and backslashes): '$Value'"
    }
}
Assert-PlainArg $Old '-Old'
Assert-PlainArg $New '-New'
Assert-PlainArg $Out '-Out'
# TabForge-VM3 is the behaviour recorder's; a data run never uses it.
if ($VmName -notin @('TabForge-VM', 'TabForge-VM2')) { throw "-VmName must be TabForge-VM or TabForge-VM2 (TabForge-VM3 is reserved): '$VmName'" }

$samples = (Resolve-Path "$PSScriptRoot\..\..\..\..\samples").Path
$tag = [guid]::NewGuid().ToString('N').Substring(0, 6)
# Per-run names in the VM: a run never reads or overwrites another run's inputs or outputs.
$name = @{ Song1 = "dataab-$tag-song1.gp"; Song2 = "dataab-$tag-song2.gp5"; Steps = "dataab-$tag-Data-Steps.ps1" }
$vmSong1 = "C:\TF\$($name.Song1)"; $vmSong2 = "C:\TF\$($name.Song2)"; $vmSteps = "C:\TF\$($name.Steps)"
$vmDirs = @("C:\TF\dataoutA-$tag", "C:\TF\dataoutB-$tag")
$vmInputs = @($vmSong1, $vmSong2, $vmSteps)
$staging = "$env:TEMP\data-ab-$tag"
$aRoot = "$Out\dataoutA-$tag"
$bRoot = "$Out\dataoutB-$tag"

try {
    New-Item $staging -ItemType Directory -Force | Out-Null
    $song1 = Join-Path $staging $name.Song1
    $song2 = Join-Path $staging $name.Song2
    $steps = Join-Path $staging $name.Steps
    Copy-Item "$samples\TabForge Demo - Ashen Meridian.gp" $song1
    Copy-Item "$samples\TabForge Demo - Ashen Meridian.gp5" $song2
    Copy-Item "$PSScriptRoot\Data-Steps.ps1" $steps

    # One VM for both sides (same virtual GPU and pipeline), as Run-AB does; each side runs the same steps script.
    foreach ($side in @(@{ Name = 'A'; Exe = $Old }, @{ Name = 'B'; Exe = $New })) {
        $dir = "C:\TF\dataout$($side.Name)-$tag"
        $command = 'powershell -NoProfile -ExecutionPolicy Bypass -File "{0}" -Exe "{1}" -Out "{2}" -Song1 "{3}" -Song2 "{4}"' -f $vmSteps, $side.Exe, $dir, $vmSong1, $vmSong2
        $callArgs = @{
            CopyIn = @($song1, $song2, $steps); TimeoutSeconds = 1800; OutDir = $dir; LocalOut = $Out; VmName = $VmName; Command = $command
        }
        & "$PSScriptRoot\Invoke-InVm.ps1" @callArgs
    }

    $guidPattern = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'
    $latin1 = [Text.Encoding]::GetEncoding('ISO-8859-1')
    $normalised = @{}
    function Use-Rule([string] $Rule) { $script:normalised[$Rule] = 1 + [int]$script:normalised[$Rule] }

    # Numbers each GUID by first appearance in the text, so only the pattern of equal ids is compared.
    function Normalise-Guids([string] $Text) {
        $map = @{}
        $sb = New-Object System.Text.StringBuilder
        $last = 0
        foreach ($m in [regex]::Matches($Text, $guidPattern)) {
            $key = $m.Value.ToLowerInvariant()
            if (-not $map.ContainsKey($key)) { $map[$key] = "<guid $($map.Count + 1)>" }
            [void]$sb.Append($Text, $last, $m.Index - $last).Append($map[$key])
            $last = $m.Index + $m.Length
        }
        [void]$sb.Append($Text, $last, $Text.Length - $last)
        $sb.ToString()
    }

    function Get-Gunzipped([byte[]] $Bytes) {
        $in = New-Object IO.MemoryStream(, $Bytes)
        $gz = New-Object IO.Compression.GZipStream($in, [IO.Compression.CompressionMode]::Decompress)
        $out = New-Object IO.MemoryStream
        $gz.CopyTo($out)
        $gz.Dispose(); $in.Dispose()
        $out.ToArray()
    }

    # SHA-256 of a string taken as ISO-8859-1 bytes (lossless, so a byte string hashes as its own bytes).
    function Get-Sha([string] $Text) {
        $bytes = $latin1.GetBytes($Text)
        ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($bytes)) -replace '-', '')
    }

    # The canonical form of one output file: the hash of its normalised content.
    function Get-Canonical([string] $Path) {
        $ext = [IO.Path]::GetExtension($Path).ToLowerInvariant()
        if ($ext -eq '.tforge') {
            $text = $latin1.GetString((Get-Gunzipped ([IO.File]::ReadAllBytes($Path))))
            $norm = Normalise-Guids $text
            if ($norm -ne $text) { Use-Rule 'GUIDs in .tforge JSON' }
            return Get-Sha $norm
        }
        if ($ext -eq '.gp') {
            $zip = [IO.Compression.ZipFile]::OpenRead($Path)
            try {
                $parts = New-Object System.Collections.Generic.List[string]
                foreach ($e in ($zip.Entries | Sort-Object FullName)) {
                    $rd = New-Object IO.BinaryReader($e.Open())
                    $bytes = $rd.ReadBytes([int]$e.Length); $rd.Dispose()
                    if ($e.FullName.EndsWith('.gz', [StringComparison]::OrdinalIgnoreCase)) { $bytes = Get-Gunzipped $bytes }
                    $text = $latin1.GetString($bytes)
                    $norm = Normalise-Guids $text
                    if ($norm -ne $text) { Use-Rule 'GUIDs in .gp entry' }
                    if ($e.FullName -like '*binding.json') {
                        $hashed = $norm -replace '"ProjectSha256":"[0-9A-Fa-f]{64}"', '"ProjectSha256":"<sha>"'
                        if ($hashed -ne $norm) { Use-Rule 'ProjectSha256 in .gp binding.json' }
                        $norm = $hashed
                    }
                    $parts.Add("## $($e.FullName) $($norm.Length)`n$norm")
                }
                return Get-Sha ($parts -join "`n")
            } finally { $zip.Dispose() }
        }
        if ($ext -eq '.txt') {
            $text = $latin1.GetString([IO.File]::ReadAllBytes($Path))
            $norm = $text -replace 'in [0-9.]+ s = [0-9.]+x realtime', 'in <time> s = <speed>x realtime'
            if ($norm -ne $text) { Use-Rule 'wall-clock render speed in a .txt report' }
            return Get-Sha $norm
        }
        Get-Sha $latin1.GetString([IO.File]::ReadAllBytes($Path))
    }

    foreach ($root in $aRoot, $bRoot) { if (-not (Test-Path $root)) { throw "missing output folder $root (the VM run failed; see $Out\run.log)" } }
    function Get-Rel([string] $Root) { Get-ChildItem $Root -Recurse -File | ForEach-Object { $_.FullName.Substring($Root.Length).TrimStart('\') } }
    $files = @(@(Get-Rel $aRoot) + @(Get-Rel $bRoot) | Sort-Object -Unique)
    $diffs = New-Object System.Collections.Generic.List[string]
    foreach ($rel in $files) {
        $pa = Join-Path $aRoot $rel; $pb = Join-Path $bRoot $rel
        if (-not (Test-Path $pa)) { $diffs.Add("${rel}: missing in OLD"); continue }
        if (-not (Test-Path $pb)) { $diffs.Add("${rel}: missing in NEW"); continue }
        if ((Get-Canonical $pa) -ne (Get-Canonical $pb)) { $diffs.Add("${rel}: differs") }
    }
    # A step that failed on both sides is still a failure: equal failures must not pass as equal data.
    foreach ($side in @(@{ Name = 'OLD'; Root = $aRoot }, @{ Name = 'NEW'; Root = $bRoot })) {
        foreach ($line in (Get-Content (Join-Path $side.Root 'manifest.txt') -ErrorAction SilentlyContinue)) {
            if ($line -notmatch ' exit 0$') { $diffs.Add("step failed in $($side.Name): $line") }
        }
    }

    $report = New-Object System.Collections.Generic.List[string]
    foreach ($k in $normalised.Keys | Sort-Object) { $report.Add("NORMALISED: $k in $($normalised[$k]) file(s)") }
    $report.Add("DATA: $($files.Count) files, $($diffs.Count) differences")
    foreach ($d in $diffs) { $report.Add("  DIFF: $d") }
    $report | Set-Content -Path "$Out\data-report-$tag.txt" -Encoding utf8
    $report | ForEach-Object { $_ }
    $script:exitCode = if ($diffs.Count -gt 0) { 1 } else { 0 }
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    if ($CleanupInVm) {
        # Through Invoke-InVm like every other VM step; the paths are generated here and hold no spaces.
        $cleanup = 'cmd /c "rmdir /s /q {0} & rmdir /s /q {1} & del /q {2} {3} {4}"' -f $vmDirs[0], $vmDirs[1], $vmInputs[0], $vmInputs[1], $vmInputs[2]
        & "$PSScriptRoot\Invoke-InVm.ps1" -Command $cleanup -VmName $VmName -TimeoutSeconds 600 | Out-Null
    }
}
exit $script:exitCode

<#
.SYNOPSIS
    PE-aware comparison of two builds of the native VST3 bridge (tfvst3.dll): is every difference one of the expected, harmless kinds?
.DESCRIPTION
    A rebuild of native/tfvst3 from the pinned SDK is not byte-identical to the committed DLL (native/tfvst3/BUILD.md,
    "Reproducibility"), but the differences must all be of these kinds:

      1. Link timestamps: the COFF header TimeDateStamp, the export directory and resource directory timestamps, and the
         TimeDateStamp of every debug directory entry (the linker was run without /Brepro).
      2. The PDB identity: the GUID of the CodeView (RSDS) debug record, its embedded PDB path (the build folder), the
         PE optional-header checksum that is derived from the content, and the content hash of a /Brepro debug record.
      3. Source-path hash names: the 8 hex digits of every MSVC anonymous-namespace name (?A0x<hash>, derived from the
         absolute path of the source file, which differs between a local folder and the CI runner).

    The script parses the PE headers, masks exactly those ranges in both files (counting how many bytes differed in each
    category) and compares the rest byte for byte. Anything else (a different size, code, data, imports, sections) is
    reported as unexplained and the script exits 1. Exit codes: 0 identical or only the expected categories, 1 unexplained
    differences, 2 usage or not a PE file.
.EXAMPLE
    .\tools\Compare-NativeBridge.ps1 -Built C:\tfb\Release\tfvst3.dll -Shipped src\TabForge.AudioEngine\native\tfvst3.dll
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Built,
    [Parameter(Mandatory)][string]$Shipped,
    # Appends the markdown result to this file (for example $env:GITHUB_STEP_SUMMARY).
    [string]$SummaryPath = ''
)

$ErrorActionPreference = 'Stop'

function Read-PeRanges([byte[]]$Bytes, [string]$Label) {
    # Returns the expected-difference ranges (Name, Offset, Length) of one PE file.
    if ($Bytes.Length -lt 0x100 -or $Bytes[0] -ne 0x4D -or $Bytes[1] -ne 0x5A) { throw "$Label is not a PE file (no MZ header)." }
    $pe = [BitConverter]::ToInt32($Bytes, 0x3C)
    if ($pe -lt 0 -or $pe + 24 -gt $Bytes.Length -or [BitConverter]::ToUInt32($Bytes, $pe) -ne 0x00004550) { throw "$Label is not a PE file (no PE signature)." }
    $coff = $pe + 4
    $sections = [BitConverter]::ToUInt16($Bytes, $coff + 2)
    $optSize = [BitConverter]::ToUInt16($Bytes, $coff + 16)
    $opt = $coff + 20
    $magic = [BitConverter]::ToUInt16($Bytes, $opt)
    if ($magic -eq 0x20B) { $dirBase = $opt + 112 } elseif ($magic -eq 0x10B) { $dirBase = $opt + 96 } else { throw "$Label has an unknown optional header magic 0x$('{0:X}' -f $magic)." }
    $sectionTable = $opt + $optSize
    $ranges = New-Object System.Collections.Generic.List[object]

    $add = {
        param([string]$Name, [int]$Offset, [int]$Length)
        if ($Offset -ge 0 -and $Length -gt 0 -and $Offset + $Length -le $Bytes.Length) { $ranges.Add([pscustomobject]@{ Name = $Name; Offset = $Offset; Length = $Length }) }
    }
    $toOffset = {
        param([uint32]$Rva)
        for ($i = 0; $i -lt $sections; $i++) {
            $s = $sectionTable + 40 * $i
            if ($s + 40 -gt $Bytes.Length) { break }
            $virtualSize = [BitConverter]::ToUInt32($Bytes, $s + 8); $virtualAddress = [BitConverter]::ToUInt32($Bytes, $s + 12)
            $rawSize = [BitConverter]::ToUInt32($Bytes, $s + 16); $rawPointer = [BitConverter]::ToUInt32($Bytes, $s + 20)
            $span = [Math]::Max($virtualSize, $rawSize)
            if ($Rva -ge $virtualAddress -and $Rva -lt $virtualAddress + $span) { return [int]($rawPointer + ($Rva - $virtualAddress)) }
        }
        return -1
    }
    $directory = {
        param([int]$Index)
        $at = $dirBase + 8 * $Index
        if ($at + 8 -gt $Bytes.Length) { return $null }
        $rva = [BitConverter]::ToUInt32($Bytes, $at); $size = [BitConverter]::ToUInt32($Bytes, $at + 4)
        if ($rva -eq 0 -or $size -eq 0) { return $null }
        return [pscustomobject]@{ Offset = (& $toOffset $rva); Size = [int]$size }
    }

    & $add 'COFF header timestamp' ($coff + 4) 4
    & $add 'optional header checksum (derived from the content)' ($opt + 64) 4
    $export = & $directory 0
    if ($export -and $export.Offset -ge 0) { & $add 'export directory timestamp' ($export.Offset + 4) 4 }
    $resource = & $directory 2
    if ($resource -and $resource.Offset -ge 0) { & $add 'resource directory timestamp' ($resource.Offset + 4) 4 }
    $debug = & $directory 6
    if ($debug -and $debug.Offset -ge 0) {
        for ($e = 0; $e + 28 -le $debug.Size; $e += 28) {
            $entry = $debug.Offset + $e
            if ($entry + 28 -gt $Bytes.Length) { break }
            $type = [BitConverter]::ToUInt32($Bytes, $entry + 12)
            $dataSize = [int][BitConverter]::ToUInt32($Bytes, $entry + 16)
            $dataPointer = [int][BitConverter]::ToUInt32($Bytes, $entry + 24)
            & $add 'debug directory timestamp' ($entry + 4) 4
            if ($type -eq 2 -and $dataSize -ge 24 -and $dataPointer -gt 0 -and $dataPointer + 4 -le $Bytes.Length -and [Text.Encoding]::ASCII.GetString($Bytes, $dataPointer, 4) -eq 'RSDS') {
                & $add 'PDB GUID (CodeView record)' ($dataPointer + 4) 16
                & $add 'PDB path (CodeView record)' ($dataPointer + 24) ($dataSize - 24)
            }
            elseif ($type -eq 16) { & $add 'reproducible-build content hash (debug record)' $dataPointer $dataSize }
        }
    }
    return ,$ranges
}

function Find-PathHashes([byte[]]$Bytes) {
    # "?A0x" + 8 hex digits: the anonymous-namespace name MSVC derives from the source file's absolute path.
    $text = [Text.Encoding]::GetEncoding(28591).GetString($Bytes)
    $found = New-Object System.Collections.Generic.List[int]
    foreach ($m in [regex]::Matches($text, '\?A0x[0-9a-f]{8}')) { $found.Add($m.Index + 4) }
    return ,$found
}

foreach ($p in @($Built, $Shipped)) { if (-not (Test-Path -LiteralPath $p)) { Write-Error "Not found: $p"; exit 2 } }
$a = [IO.File]::ReadAllBytes($Built)
$b = [IO.File]::ReadAllBytes($Shipped)
$lines = New-Object System.Collections.Generic.List[string]
function Emit([string]$Line) { $lines.Add($Line); Write-Output $Line }
function Finish([int]$Code) { if ($SummaryPath) { $lines | Add-Content -Path $SummaryPath }; exit $Code }

try { $rangesA = Read-PeRanges $a 'the built DLL'; $rangesB = Read-PeRanges $b 'the checked-in DLL' }
catch { Write-Error $_.Exception.Message; exit 2 }

Emit '### tfvst3.dll: PE-aware comparison (CI build vs checked-in)'
$hashA = (Get-FileHash -Algorithm SHA256 -LiteralPath $Built).Hash.ToLowerInvariant()
$hashB = (Get-FileHash -Algorithm SHA256 -LiteralPath $Shipped).Hash.ToLowerInvariant()
Emit "- built: ``$hashA`` ($($a.Length) bytes)"
Emit "- checked in: ``$hashB`` ($($b.Length) bytes)"

if ($hashA -eq $hashB) { Emit '- result: identical'; Finish 0 }
if ($a.Length -ne $b.Length) {
    Emit "- result: DIFFERENT, sizes differ ($($a.Length) vs $($b.Length) bytes): not explained by timestamps, the PDB identity or path hashes (a different compiler or linker, SDK or source?). Run the build-bridge workflow and commit its output (docs/REPRODUCIBLE_BUILDS.md)."
    Finish 1
}

# Same size: mask the expected ranges (both files' ranges, plus path-hash digits present in both) and compare the rest.
$categories = [ordered]@{}
$masked = New-Object 'bool[]' $a.Length
function Mask([string]$Name, [int]$Offset, [int]$Length) {
    $changed = 0
    for ($i = $Offset; $i -lt $Offset + $Length; $i++) {
        if ($a[$i] -ne $b[$i]) { $changed++ }
        $masked[$i] = $true
    }
    if (-not $categories.Contains($Name)) { $categories[$Name] = [pscustomobject]@{ Ranges = 0; Differing = 0 } }
    $categories[$Name].Ranges++
    $categories[$Name].Differing += $changed
}
foreach ($r in $rangesA) { Mask $r.Name $r.Offset $r.Length }
foreach ($r in $rangesB) { if (-not $masked[$r.Offset]) { Mask $r.Name $r.Offset $r.Length } }
$hashesA = Find-PathHashes $a
$hashesB = New-Object 'System.Collections.Generic.HashSet[int]'
foreach ($at in (Find-PathHashes $b)) { [void]$hashesB.Add($at) }
foreach ($at in $hashesA) { if ($hashesB.Contains($at) -and -not $masked[$at]) { Mask 'source-path hash names (?A0x<8 hex digits>)' $at 8 } }

$unexplained = 0; $first = -1
for ($i = 0; $i -lt $a.Length; $i++) {
    if (-not $masked[$i] -and $a[$i] -ne $b[$i]) { $unexplained++; if ($first -lt 0) { $first = $i } }
}
Emit '- expected differences (range count / differing bytes):'
$total = 0
foreach ($name in $categories.Keys) { Emit "  - ${name}: $($categories[$name].Ranges) / $($categories[$name].Differing)"; $total += $categories[$name].Differing }
if ($unexplained -eq 0) {
    Emit "- result: same size; all $total differing bytes are link timestamps, the PDB identity or source-path hashes. Code and data are identical."
    Finish 0
}
Emit ("- result: DIFFERENT, $unexplained byte(s) differ outside the expected categories (first at offset 0x{0:X}). The bridge source, SDK or compiler differs: run the build-bridge workflow and commit its output (docs/REPRODUCIBLE_BUILDS.md)." -f $first)
Finish 1

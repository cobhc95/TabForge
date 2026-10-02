<#
.SYNOPSIS
    Compares two TabForge release builds byte by byte: two portable zips and, optionally, two installers.
.DESCRIPTION
    Use it to check that the build from GitHub Actions and a local tools\Package-Release.ps1 build of the same commit
    are identical (docs/REPRODUCIBLE_BUILDS.md).

    Portable zips: entry names, sizes and the SHA-256 of every entry's content are compared (the zip container
    itself is also compared as a whole file). For a differing entry it prints the number of differing bytes and the
    first differing offset; for .exe/.dll it says whether the only differences are the 4-byte PE link timestamp.

    Installers (-SetupA/-SetupB): an Inno Setup installer embeds compile-time timestamps and is not expected to be
    byte-identical between builds. If it is, that is reported. If not, and innounp.exe or innoextract.exe is on PATH,
    the two payloads are extracted and compared file by file; otherwise only the differing byte count is shown and the
    script says so. The installer is built from the same tested folder as the portable zip, so an identical portable
    zip already establishes that the payload is identical.

    Exit code: 0 when every compared zip entry is identical (installer differences alone do not fail the comparison
    unless the extracted payloads differ), 1 when a zip entry or an installer payload differs, 2 on a usage error.
.EXAMPLE
    .\tools\Compare-Release.ps1 -ZipA dist\TabForge-0.2.0-beta.6-win-x64-portable.zip -ZipB C:\ci\TabForge-0.2.0-beta.6-win-x64-portable.zip
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ZipA,
    [Parameter(Mandatory)][string]$ZipB,
    [string]$SetupA = '',
    [string]$SetupB = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$differences = 0

function Get-Sha256Bytes([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { -join ($sha.ComputeHash($Bytes) | ForEach-Object { $_.ToString('x2') }) } finally { $sha.Dispose() }
}

function Read-Entries([string]$Path) {
    $map = New-Object 'System.Collections.Generic.SortedDictionary[string,byte[]]' ([StringComparer]::Ordinal)
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName.EndsWith('/')) { continue }
            $ms = New-Object IO.MemoryStream
            $stream = $entry.Open()
            try { $stream.CopyTo($ms) } finally { $stream.Dispose() }
            $map[$entry.FullName] = $ms.ToArray()
        }
    } finally { $zip.Dispose() }
    return ,$map
}

# Describes how two byte arrays differ. For PE files it also separates the link timestamp from other differences.
function Describe-Difference([string]$Name, [byte[]]$A, [byte[]]$B) {
    if ($A.Length -ne $B.Length) { return "sizes differ ($($A.Length) vs $($B.Length) bytes)" }
    $count = 0; $first = -1
    $tsOffset = -1
    if ($Name -match '\.(exe|dll)$' -and $A.Length -gt 0x100 -and $A[0] -eq 0x4D -and $A[1] -eq 0x5A) {
        $pe = [BitConverter]::ToInt32($A, 0x3C)
        if ($pe -gt 0 -and $pe + 12 -lt $A.Length) { $tsOffset = $pe + 8 }
    }
    $outsideTimestamp = 0
    for ($i = 0; $i -lt $A.Length; $i++) {
        if ($A[$i] -ne $B[$i]) {
            $count++
            if ($first -lt 0) { $first = $i }
            if ($tsOffset -lt 0 -or $i -lt $tsOffset -or $i -ge $tsOffset + 4) { $outsideTimestamp++ }
        }
    }
    $text = "$count of $($A.Length) bytes differ, first at offset 0x{0:X}" -f $first
    if ($tsOffset -ge 0) {
        if ($outsideTimestamp -eq 0) { $text += ' (only the PE header link timestamp: a non-deterministic link, e.g. a native DLL linked without /Brepro)' }
        else { $text += " ($outsideTimestamp outside the PE link timestamp)" }
    }
    return $text
}

function Explain([string]$Name) {
    switch -Regex ($Name) {
        '(^|/)tfvst3\.dll$' { return 'native bridge: differs when a different tfvst3.dll was shipped. Both builds must use the committed DLL (docs/REPRODUCIBLE_BUILDS.md).' }
        '(^|/)TabForge\.exe$' { return 'managed single-file build: differs when the SDK version, the commit, the source text (line endings) or a build setting differs.' }
        '\.(pdb)$' { return 'symbols are not shipped; if present they embed source checksums, so line endings matter.' }
        '\.(md|txt|json|svg)$' { return 'text file: usually a line-ending difference (checkout is not eol=crlf) or a different commit.' }
        default { return '' }
    }
}

foreach ($p in @($ZipA, $ZipB)) { if (-not (Test-Path -LiteralPath $p)) { Write-Error "Not found: $p"; exit 2 } }
if ([bool]$SetupA -ne [bool]$SetupB) { Write-Error 'Pass both -SetupA and -SetupB, or neither.'; exit 2 }

Write-Output "Portable zip A: $ZipA"
Write-Output "Portable zip B: $ZipB"
$hashA = (Get-FileHash -Algorithm SHA256 -LiteralPath $ZipA).Hash.ToLowerInvariant()
$hashB = (Get-FileHash -Algorithm SHA256 -LiteralPath $ZipB).Hash.ToLowerInvariant()
Write-Output "  whole-file SHA-256 A: $hashA"
Write-Output "  whole-file SHA-256 B: $hashB"

$a = Read-Entries $ZipA
$b = Read-Entries $ZipB
$names = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
foreach ($k in $a.Keys) { [void]$names.Add($k) }
foreach ($k in $b.Keys) { [void]$names.Add($k) }

$identical = 0
foreach ($name in $names) {
    if (-not $a.ContainsKey($name)) { Write-Output "  ONLY IN B: $name"; $differences++; continue }
    if (-not $b.ContainsKey($name)) { Write-Output "  ONLY IN A: $name"; $differences++; continue }
    if ((Get-Sha256Bytes $a[$name]) -eq (Get-Sha256Bytes $b[$name])) { $identical++; continue }
    $differences++
    Write-Output ("  DIFFERENT: {0}: {1}" -f $name, (Describe-Difference $name $a[$name] $b[$name]))
    $why = Explain $name
    if ($why) { Write-Output "             $why" }
}
Write-Output ("  {0} of {1} entries identical." -f $identical, $names.Count)
if ($differences -eq 0) {
    if ($hashA -eq $hashB) { Write-Output 'PORTABLE ZIP: identical (the files are byte for byte the same).' }
    else { Write-Output 'PORTABLE ZIP: every entry is identical; only the zip container differs (entry order, compression or timestamps).' }
}

# ---- installers ------------------------------------------------------------------------------------
if ($SetupA) {
    foreach ($p in @($SetupA, $SetupB)) { if (-not (Test-Path -LiteralPath $p)) { Write-Error "Not found: $p"; exit 2 } }
    Write-Output ''
    Write-Output "Installer A: $SetupA"
    Write-Output "Installer B: $SetupB"
    $sa = [IO.File]::ReadAllBytes($SetupA); $sb = [IO.File]::ReadAllBytes($SetupB)
    if ((Get-Sha256Bytes $sa) -eq (Get-Sha256Bytes $sb)) {
        Write-Output 'INSTALLER: identical.'
    } else {
        $tool = $null
        foreach ($n in @('innounp.exe', 'innoextract.exe')) { $c = Get-Command $n -ErrorAction SilentlyContinue; if ($c) { $tool = $c.Source; break } }
        if ($tool) {
            $tmp = Join-Path ([IO.Path]::GetTempPath()) ('tfcmp-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
            $dirs = @()
            try {
                foreach ($setup in @($SetupA, $SetupB)) {
                    $d = Join-Path $tmp ('s' + $dirs.Count); [IO.Directory]::CreateDirectory($d) | Out-Null; $dirs += $d
                    if ($tool -like '*innounp*') { & $tool -x "-d$d" -y $setup | Out-Null } else { & $tool --output-dir $d $setup | Out-Null }
                }
                $fa = @{}; $fb = @{}
                Get-ChildItem -LiteralPath $dirs[0] -Recurse -File | ForEach-Object { $fa[$_.FullName.Substring($dirs[0].Length)] = $_.FullName }
                Get-ChildItem -LiteralPath $dirs[1] -Recurse -File | ForEach-Object { $fb[$_.FullName.Substring($dirs[1].Length)] = $_.FullName }
                $payloadDiff = 0
                foreach ($k in (@($fa.Keys) + @($fb.Keys) | Sort-Object -Unique)) {
                    if (-not $fa.ContainsKey($k) -or -not $fb.ContainsKey($k)) { Write-Output "  payload file only in one installer: $k"; $payloadDiff++; continue }
                    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $fa[$k]).Hash -ne (Get-FileHash -Algorithm SHA256 -LiteralPath $fb[$k]).Hash) { Write-Output "  payload file differs: $k"; $payloadDiff++ }
                }
                if ($payloadDiff -eq 0) { Write-Output 'INSTALLER: the files differ as a whole (Inno embeds timestamps) but every extracted payload file is identical.' }
                else { Write-Output "INSTALLER: $payloadDiff payload file(s) differ."; $differences++ }
            } finally { if (Test-Path -LiteralPath $tmp) { [IO.Directory]::Delete($tmp, $true) } }
        } else {
            $len = if ($sa.Length -eq $sb.Length) { "same size ($($sa.Length) bytes)" } else { "sizes differ ($($sa.Length) vs $($sb.Length) bytes)" }
            $n = [Math]::Min($sa.Length, $sb.Length); $count = 0
            for ($i = 0; $i -lt $n; $i++) { if ($sa[$i] -ne $sb[$i]) { $count++ } }
            Write-Output "INSTALLER: not identical ($len, $count differing bytes in the common length)."
            Write-Output '  Expected: Inno Setup embeds its own compile timestamps and a random uninstaller id, so two builds of the same folder differ.'
            Write-Output '  Its payload could not be compared here (no innounp.exe or innoextract.exe on PATH). The installer is built from the same'
            Write-Output '  tested folder as the portable zip, so the portable zip result above decides whether the shipped files are identical.'
        }
    }
}

Write-Output ''
if ($differences -eq 0) { Write-Output 'RESULT: identical.'; exit 0 }
Write-Output "RESULT: $differences difference(s) found."; exit 1

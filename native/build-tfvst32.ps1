# Builds Source\native\tfvst32 twice from the one source tree: x86 Release -> tfvst32.exe and x64 Release -> tfvst64.exe
# (static CRT), then copies both beside TabForge.exe in Source\src\TabForge.AudioEngine\native\. Test-only copies (the
# stall hook, the deterministic test plug-in and the native self-test) go to test-bin\ for both bitnesses; they are never
# shipped.
# Pins the installed Visual Studio 18 toolchain and the Windows SDK, records both (and every source hash) in
# Source\native\BUILD_PROVENANCE32.md and BUILD_PROVENANCE64.md, and fails with a clear message when the toolchain is missing.
# Usage: .\build-tfvst32.ps1 [-Clean] [-NoTest] [-BuildDir <path>] [-TestOutDir <path>]
[CmdletBinding()]
param([switch]$Clean, [switch]$NoTest, [string]$BuildDir = '', [string]$TestOutDir = '')
$ErrorActionPreference = 'Stop'

$candidate = Split-Path -Parent $PSScriptRoot
if (Test-Path (Join-Path $candidate 'TabForge.sln')) {
    $sourceRoot = $candidate
    $repoRoot = Split-Path -Parent $candidate
} else {
    $repoRoot = $candidate
    $sourceRoot = $candidate
}
$src = Join-Path $PSScriptRoot 'tfvst32'
$buildDir32 = if ($BuildDir) { $BuildDir } else { Join-Path $src 'build' }
$buildDir64 = if ($BuildDir) { "$BuildDir-x64" } else { Join-Path $src 'build64' }
$testOutDir = if ($TestOutDir) { $TestOutDir } else { Join-Path $src 'test-bin' }
$destDir = Join-Path $sourceRoot 'src\TabForge.AudioEngine\native'
$provenance32 = Join-Path $PSScriptRoot 'BUILD_PROVENANCE32.md'
$provenance64 = Join-Path $PSScriptRoot 'BUILD_PROVENANCE64.md'

# ---- pinned toolchain -----------------------------------------------------------------------------------
$cmake = 'C:\Program Files\CMake\bin\cmake.exe'
if (-not (Test-Path $cmake)) { $cmake = (Get-Command cmake -ErrorAction SilentlyContinue).Source }
if (-not $cmake) { throw 'CMake was not found (expected C:\Program Files\CMake\bin\cmake.exe or cmake on PATH).' }

$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'vswhere.exe was not found. Install Visual Studio 18 with the x86/x64 C++ tools.' }
$vsPath = (& $vswhere -latest -products * -version '[18.0,19.0)' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Out-String).Trim()
if (-not $vsPath) { throw 'Visual Studio 18 with the x86/x64 C++ toolchain was not found.' }

function Find-Compiler([string]$toolset) {
    Get-ChildItem (Join-Path $vsPath 'VC\Tools\MSVC') -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "bin\Hostx64\$toolset\cl.exe" } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
$clX86 = Find-Compiler 'x86'
$clX64 = Find-Compiler 'x64'
if (-not $clX86) { throw "The x86 MSVC compiler was not found under $vsPath." }
if (-not $clX64) { throw "The x64 MSVC compiler was not found under $vsPath." }

$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdkVersion = Get-ChildItem (Join-Path $sdkRoot 'Lib') -Directory -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending |
    Where-Object { (Test-Path (Join-Path $_.FullName 'um\x86\kernel32.lib')) -and (Test-Path (Join-Path $_.FullName 'um\x64\kernel32.lib')) } |
    Select-Object -First 1 -ExpandProperty Name
if (-not $sdkVersion) { throw 'A Windows 10/11 SDK with the x86 and x64 libraries was not found.' }

$clVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($clX86).FileVersion
$cmakeVersion = ((& $cmake --version | Select-Object -First 1) -replace 'cmake version ', '').Trim()
$generator = 'Visual Studio 18 2026'
Write-Host "Toolchain: MSVC $clVersion, Windows SDK $sdkVersion, CMake $cmakeVersion, generator $generator (Win32 + x64)"

if ($Clean) {
    foreach ($dir in @($buildDir32, $buildDir64)) { if (Test-Path $dir) { Remove-Item -Recurse -Force $dir } }
}
$testFlag = if ($NoTest) { 'OFF' } else { 'ON' }
$builds = @(
    [pscustomobject]@{ Platform = 'Win32'; BuildDir = $buildDir32; Helper = 'tfvst32.exe'; Machine = 0x14C; PlatformLabel = 'Win32 (x86)';
        Plugin = 'passthrough32.dll'; TestExe = 'tfvst32test.exe'; Hooks = 'tfvst32-testhooks.exe'; Provenance = $provenance32; Heading = 'tfvst32'; Compiler = $clX86 },
    [pscustomobject]@{ Platform = 'x64'; BuildDir = $buildDir64; Helper = 'tfvst64.exe'; Machine = 0x8664; PlatformLabel = 'x64';
        Plugin = 'passthrough64.dll'; TestExe = 'tfvst64test.exe'; Hooks = 'tfvst64-testhooks.exe'; Provenance = $provenance64; Heading = 'tfvst64'; Compiler = $clX64 }
)

$sources = @('CMakeLists.txt', 'README.md', 'tfvst32_protocol.h', 'control_channel.h', 'host_session.h', 'host_session.cpp',
    'probe.cpp', 'selftest.cpp', 'host_runtime.h', 'host_runtime.cpp',
    'bridge_block.h', 'bridge_block.cpp', 'pipe_channel.h',
    'pipe_channel.cpp', 'tcp_channel.h', 'tcp_channel.cpp', 'hash_hold.h', 'hash_hold.cpp', 'vst2_host.h', 'vst2_host.cpp', 'vst2_calls.h',
    'vst2_calls.cpp', 'vst2_state.cpp',
    'vst3_host.h', 'vst3_host.cpp', 'plugin_host.h', 'plugin_host.cpp',
    'editor_target.h', 'editor_window.h', 'editor_window.cpp', 'crash_report.h', 'crash_report.cpp', 'main.cpp',
    'test/passthrough32.cpp', 'test/tfvst32test.cpp', 'test/fuzz_frames.cpp') | ForEach-Object {
    $file = Join-Path $src $_
    "- $_ SHA-256: $((Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant())"
}

foreach ($build in $builds) {
    Write-Host "=== $($build.PlatformLabel): $($build.Helper) ==="
    & $cmake -S $src -B $build.BuildDir -G $generator -A $build.Platform "-DTFVST32_BUILD_TEST=$testFlag"
    if ($LASTEXITCODE -ne 0) { throw "$($build.Helper) CMake configure failed ($LASTEXITCODE)" }
    & $cmake --build $build.BuildDir --config Release --parallel
    if ($LASTEXITCODE -ne 0) { throw "$($build.Helper) build failed ($LASTEXITCODE)" }

    $exe = Get-ChildItem $build.BuildDir -Recurse -Filter $build.Helper | Where-Object { $_.FullName -match '\\Release\\' } | Select-Object -First 1 -ExpandProperty FullName
    if (-not $exe) { throw "build succeeded but $($build.Helper) is missing under $($build.BuildDir)" }
    $bytes = [IO.File]::ReadAllBytes($exe)
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
    if ($machine -ne $build.Machine) { throw ("$($build.Helper) has PE machine 0x{0:X4}, expected 0x{1:X4}" -f $machine, $build.Machine) }

    New-Item -ItemType Directory -Force $destDir | Out-Null
    Copy-Item $exe (Join-Path $destDir $build.Helper) -Force
    $shippedSize = (Get-Item (Join-Path $destDir $build.Helper)).Length
    Write-Host "$($build.Helper) -> $destDir (PE machine 0x$($build.Machine.ToString('X4')), $shippedSize bytes)"

    if (-not $NoTest) {
        New-Item -ItemType Directory -Force $testOutDir | Out-Null
        $testDll = Get-ChildItem $build.BuildDir -Recurse -Filter $build.Plugin | Where-Object { $_.FullName -match '\\Release\\' } | Select-Object -First 1 -ExpandProperty FullName
        $testExe = Get-ChildItem $build.BuildDir -Recurse -Filter $build.TestExe | Where-Object { $_.FullName -match '\\Release\\' } | Select-Object -First 1 -ExpandProperty FullName
        $testHooks = Get-ChildItem $build.BuildDir -Recurse -Filter $build.Hooks | Where-Object { $_.FullName -match '\\Release\\' } | Select-Object -First 1 -ExpandProperty FullName
        if (-not $testDll -or -not $testExe -or -not $testHooks) { throw "the native test assets for $($build.Platform) are missing from the build output" }
        Copy-Item $testDll (Join-Path $testOutDir $build.Plugin) -Force
        Copy-Item $testExe (Join-Path $testOutDir $build.TestExe) -Force
        Copy-Item $testHooks (Join-Path $testOutDir $build.Hooks) -Force
        Write-Host "Test assets ($($build.PlatformLabel)) -> $testOutDir"
    }

    # ---- provenance -------------------------------------------------------------------------------------
    $exeHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $destDir $build.Helper)).Hash.ToLowerInvariant()
    $lines = @(
        "# $($build.Heading) build provenance",
        '',
        'Generated by `Source/native/build-tfvst32.ps1`; do not edit by hand. `Source/tools/Package-Release.ps1` verifies',
        "the shipped ``$($build.Helper)`` against the hash recorded here, and ``Source/tools/Write-Sbom.ps1`` copies the record into",
        '`Source/docs/reference/SBOM.md`.',
        '',
        "- Built (UTC): $([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss'))",
        "- Visual Studio: $vsPath",
        "- Compiler: $($build.Compiler)",
        "- Compiler version: MSVC $clVersion",
        "- Windows SDK: $sdkVersion",
        "- CMake: $cmakeVersion; generator: $generator; platform $($build.PlatformLabel), configuration Release, static CRT",
        '- No third-party library and no Steinberg VST2 SDK header: the AEffect/opcode declarations are original TabForge code.',
        '- Built from the same source tree as the other bitness; only the PE target differs.',
        '',
        '## Source inputs',
        ''
    ) + $sources + @(
        '',
        '## Output',
        '',
        "$($build.Helper) SHA-256: $exeHash"
    )
    [IO.File]::WriteAllText($build.Provenance, (($lines -join "`r`n") + "`r`n"), [Text.Encoding]::ASCII)
    Write-Host "Provenance -> $($build.Provenance) ($($build.Helper) SHA-256 $exeHash)"
}

if (-not $NoTest) {
    & (Join-Path $testOutDir 'tfvst32test.exe') (Join-Path $testOutDir 'passthrough32.dll')
    if ($LASTEXITCODE -ne 0) { throw "the 32-bit native self-test failed ($LASTEXITCODE)" }
    & (Join-Path $testOutDir 'tfvst64test.exe') (Join-Path $testOutDir 'passthrough64.dll')
    if ($LASTEXITCODE -ne 0) { throw "the 64-bit native self-test failed ($LASTEXITCODE)" }
    & (Join-Path $testOutDir 'tfvst32-testhooks.exe') --selftest
    if ($LASTEXITCODE -ne 0) { throw "the 32-bit test-hook helper's selftest failed ($LASTEXITCODE)" }
    & (Join-Path $testOutDir 'tfvst64-testhooks.exe') --selftest
    if ($LASTEXITCODE -ne 0) { throw "the 64-bit test-hook helper's selftest failed ($LASTEXITCODE)" }
}
& (Join-Path $destDir 'tfvst32.exe') --selftest
if ($LASTEXITCODE -ne 0) { throw "tfvst32 --selftest failed ($LASTEXITCODE)" }
& (Join-Path $destDir 'tfvst64.exe') --selftest
if ($LASTEXITCODE -ne 0) { throw "tfvst64 --selftest failed ($LASTEXITCODE)" }

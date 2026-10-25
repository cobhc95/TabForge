# KEEP (cleanup 2026-10-19): test-only deliberately-faulty VST2 crash-isolation DLL, unreferenced by csproj/sln by design. Kept; see Docs/Archive/Source-reports/STALE_CONFIG_REPORT.md section 3. No files deleted.
# Builds TabForgeCrashTest.dll and copies it to Source/native/crashtest/bin/.
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildDir = Join-Path $root "build"
$binDir = Join-Path $root "bin"
$cmake = "C:\Program Files\CMake\bin\cmake.exe"

if (-not (Test-Path $cmake)) {
    $cmake = "cmake"
}

New-Item -ItemType Directory -Force -Path $binDir | Out-Null

$generators = @("Visual Studio 18 2026", "Visual Studio 17 2022")
$configured = $false

foreach ($gen in $generators) {
    if (Test-Path $buildDir) {
        Remove-Item -Recurse -Force $buildDir
    }
    New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

    Write-Host "Trying generator: $gen"
    & $cmake -S $root -B $buildDir -G $gen -A x64
    if ($LASTEXITCODE -eq 0) {
        $configured = $true
        Write-Host "Configured successfully with $gen"
        break
    }
}

if (-not $configured) {
    throw "CMake configuration failed with all generators."
}

& $cmake --build $buildDir --config Release
if ($LASTEXITCODE -ne 0) {
    throw "Build failed."
}

$dll = Get-ChildItem -Recurse -Path $buildDir -Filter "TabForgeCrashTest.dll" | Select-Object -First 1
if (-not $dll) {
    throw "TabForgeCrashTest.dll not found after build."
}

Copy-Item -Force $dll.FullName (Join-Path $binDir "TabForgeCrashTest.dll")
Write-Host "Copied $($dll.FullName) -> $binDir\TabForgeCrashTest.dll"

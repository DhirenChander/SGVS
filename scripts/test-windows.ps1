<#
.SYNOPSIS
  Runs every test suite in the repo on Windows and prints a summary.

.DESCRIPTION
  The Windows-only tests (shared memory library, WASAPI audio capture) are skipped on other platforms,
  so this is the script that actually exercises them. Each suite runs even if an earlier one fails.

  You need: Go (1.27.3+), the .NET 10 SDK, CMake, a C++ compiler (Visual Studio Build Tools) and OpenSSL
  (the shared memory library links against it, set OPENSSL_ROOT_DIR if CMake can't find it).

  Build output goes to a temp folder, nothing is written into the repo.

  Run from the repo root:  powershell -File scripts\test-windows.ps1
#>
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $env:TEMP "sgvs-tests"
New-Item -ItemType Directory -Force -Path $work | Out-Null

$results = [ordered]@{}

function Run-Suite([string]$name, [scriptblock]$body) {
    Write-Host ""
    Write-Host "=== $name ===" -ForegroundColor Cyan
    $global:LASTEXITCODE = 0
    try { & $body } catch { Write-Host $_ -ForegroundColor Red; $global:LASTEXITCODE = 1 }
    $results[$name] = ($global:LASTEXITCODE -eq 0)
}

Run-Suite "Networking (Go)" {
    Push-Location (Join-Path $root "src\networking")
    go vet ./...
    if ($LASTEXITCODE -eq 0) { go test -race -count=1 ./... }
    Pop-Location
}

Run-Suite "Shared memory library core (C, Unity)" {
    $build = Join-Path $work "shared-mem-core"
    cmake -S (Join-Path $root "src\shared_mem_lib\core") -B $build
    if ($LASTEXITCODE -eq 0) { cmake --build $build --config $Configuration }
    if ($LASTEXITCODE -eq 0) { ctest --test-dir $build -C $Configuration --output-on-failure }
}

Run-Suite "Processing (C++, gtest)" {
    $build = Join-Path $work "processing"
    cmake -S (Join-Path $root "src\processing") -B $build
    if ($LASTEXITCODE -eq 0) { cmake --build $build --config $Configuration }
    if ($LASTEXITCODE -eq 0) { ctest --test-dir $build -C $Configuration --output-on-failure }
}

Run-Suite "Frontend + integration (.NET, includes the Windows-only shared memory tests)" {
    # the first build also builds the Go networking module into the app's backend\ folder
    dotnet test (Join-Path $root "SGVS.slnx") -c $Configuration
}

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
foreach ($name in $results.Keys) {
    $status = if ($results[$name]) { "PASS" } else { "FAIL" }
    $color = if ($results[$name]) { "Green" } else { "Red" }
    Write-Host ("{0}  {1}" -f $status, $name) -ForegroundColor $color
}

if ($results.Values -contains $false) { exit 1 }

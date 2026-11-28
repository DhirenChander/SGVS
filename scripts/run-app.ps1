<#
.SYNOPSIS
  Builds and launches the whole app on Windows: the frontend, the Go networking module and the C++ processing module.

.DESCRIPTION
  1. Builds the frontend (its build also runs `go build` for networking into bin\<config>\net10.0\backend\).
  2. Builds processing_main with CMake into a temp folder (the first build downloads Opus, later ones are quick).
  3. Starts the frontend, then networking, then processing, each in its own console window titled "SGVS <name>".
     Processing gets the frontend's PID, so its audio capture leaves the app's own sound out.
     Networking's window is where you copy the offer SDP and paste the guest's answer when hosting.
  4. Waits for the frontend to close, then stops networking and processing and closes all three windows.
     Ctrl+C here does the same. A window whose program stops on its own stays open so you can read why.

  You need: Go, the .NET 10 SDK, CMake and a C++ compiler (Visual Studio Build Tools). Streaming also needs
  ffmpeg (winget install --id Gyan.FFmpeg -e, or set SGVS_FFMPEG).

  Run from the repo root:   powershell -File scripts\run-app.ps1
  A second copy on one PC:  powershell -File scripts\run-app.ps1 -Instance B
#>
param(
    [string]$Configuration = "Debug",

    # gives this copy its own shared memory (SGVS_SHM_INSTANCE), so two copies on one PC don't share rings.
    # 1-16 letters or digits, the same rule the shared memory library applies
    [ValidatePattern('^$|^[A-Za-z0-9]{1,16}$')]
    [string]$Instance = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$instanceName = if ($Instance) { $Instance } else { "default" }
$processingBuild = Join-Path $env:TEMP "sgvs-run\processing-build"

# how often the launcher checks on the three processes
$pollSeconds = 1
# how long to look for a program inside its new window before deciding it already exited
$childLookupSeconds = 10

function Assert-Tool([string]$name) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        throw "$name was not found on PATH. See the notes at the top of this script for what to install."
    }
}

function Invoke-Checked([string]$what, [scriptblock]$body) {
    Write-Host "== $what" -ForegroundColor Cyan
    & $body
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." }
}

function ConvertTo-Quoted([string]$value) { "'" + $value.Replace("'", "''") + "'" }

# the program the window's powershell started. null if it exited before it could be found
function Find-Child([int]$parentId, [string]$exe) {
    $exeName = Split-Path -Leaf $exe
    $deadline = (Get-Date).AddSeconds($childLookupSeconds)
    while ((Get-Date) -lt $deadline) {
        $child = Get-CimInstance Win32_Process -Filter "ParentProcessId = $parentId AND Name = '$exeName'"
        if ($child) {
            $process = Get-Process -Id $child.ProcessId -ErrorAction SilentlyContinue
            # Windows PowerShell only keeps the exit code if the handle was opened while the process ran
            if ($process) { $null = $process.Handle }
            return $process
        }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

# opens exe in its own console window, titled "SGVS <name>". the window stays open after the program exits so its
# last message can be read, until the launcher closes it
function Start-Module([string]$name, [string]$exe, [string[]]$arguments = @()) {
    if (-not (Test-Path $exe)) { throw "$name was not found at $exe." }
    $call = (@($exe) + $arguments | ForEach-Object { ConvertTo-Quoted $_ }) -join " "
    $command = "`$Host.UI.RawUI.WindowTitle = 'SGVS $name'; & $call; " +
        "Write-Host ''; Write-Host `"$name exited (code `$LASTEXITCODE). Press Enter to close this window.`"; [void][Console]::ReadLine()"
    $window = Start-Process powershell -ArgumentList @("-NoProfile", "-Command", $command) `
        -WorkingDirectory (Split-Path -Parent $exe) -PassThru
    $process = Find-Child $window.Id $exe
    if ($process) { Write-Host "   started $name (PID $($process.Id)) in its own window" }
    else { Write-Host "!! $name exited right away, its window shows why." -ForegroundColor Yellow }
    return [pscustomobject]@{ Name = $name; Window = $window; Process = $process; Reported = ($null -eq $process) }
}

Assert-Tool "dotnet"
Assert-Tool "go"
Assert-Tool "cmake"
if (-not $env:SGVS_FFMPEG -and -not (Get-Command "ffmpeg" -ErrorAction SilentlyContinue)) {
    Write-Host "ffmpeg not found: the app runs, but Start stream and watching a stream will report an error." -ForegroundColor Yellow
}

Invoke-Checked "Building the frontend and networking ($Configuration)" {
    dotnet build (Join-Path $root "src\SGVS.Frontend\SGVS.Frontend.csproj") -c $Configuration --nologo -v quiet
}
Invoke-Checked "Configuring processing" {
    cmake -S (Join-Path $root "src\processing") -B $processingBuild | Out-Null
}
Invoke-Checked "Building processing" {
    cmake --build $processingBuild --config $Configuration --target processing_main
}

$appDir = Join-Path $root "src\SGVS.Frontend\bin\$Configuration\net10.0"
$frontendExe = Join-Path $appDir "SGVS.Frontend.exe"
$networkingExe = Join-Path $appDir "backend\sgvs-networking.exe"
# Visual Studio generators put the exe in a per-config folder, single-config generators (Ninja) don't
$processingExe = @(
    (Join-Path $processingBuild "$Configuration\processing_main.exe"),
    (Join-Path $processingBuild "processing_main.exe")
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $processingExe) { throw "processing_main.exe was not found under $processingBuild." }

# the children inherit it. empty means the default shared memory names
$env:SGVS_SHM_INSTANCE = $Instance

$modules = @()
try {
    Write-Host "== Launching (instance: $instanceName)" -ForegroundColor Cyan
    $frontend = Start-Module "frontend" $frontendExe
    $modules += $frontend
    if (-not $frontend.Process) { throw "The frontend did not start." }
    $modules += Start-Module "networking" $networkingExe
    $modules += Start-Module "processing" $processingExe @("$($frontend.Process.Id)")

    Write-Host "Running. Close the SGVS window (or press Ctrl+C here) to stop everything." -ForegroundColor Green
    while (-not $frontend.Process.HasExited) {
        foreach ($module in $modules) {
            if (-not $module.Reported -and $module.Process.HasExited) {
                $module.Reported = $true
                Write-Host "!! $($module.Name) stopped on its own (exit code $($module.Process.ExitCode)), its window shows why." -ForegroundColor Yellow
            }
        }
        Start-Sleep -Seconds $pollSeconds
    }
    Write-Host "The frontend closed (exit code $($frontend.Process.ExitCode))."
}
finally {
    # runs on a normal exit, an error and Ctrl+C. /T ends each window's powershell and the program inside it
    foreach ($module in $modules) {
        if (-not $module.Window.HasExited) {
            # redirected inside cmd: in Windows PowerShell a native command's stderr would become a terminating error here
            cmd /c "taskkill /T /F /PID $($module.Window.Id) >nul 2>&1"
            Write-Host "   stopped $($module.Name)"
        }
    }
}

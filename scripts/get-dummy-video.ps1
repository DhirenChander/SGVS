<#
.SYNOPSIS
  Downloads the clip the dummy stream plays (Rocket League tournament, from outplayed.tv).

.DESCRIPTION
  The clip is committed already, so this is only needed if the file goes missing. It goes to
  src\SGVS.Frontend\Assets\Placeholders\StreamVideo\rocket-league.mp4 and the build copies it next to the app.
  The download is checked against a SHA-256 so a changed or truncated file is rejected.

  Run from the repo root:  powershell -File scripts\get-dummy-video.ps1
#>
$ErrorActionPreference = "Stop"

$url = "https://uploads.outplayed.tv/media/2026/11/15/c40794c2-0d6e-44cc-98bc-8b2d08965525.mp4"
$expected = "4890802b49f968100bf698046a313c7eecdc7e920cee9cdb196e5a1fe9c26caf"
$root = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $root "src\SGVS.Frontend\Assets\Placeholders\StreamVideo"
$dest = Join-Path $dir "rocket-league.mp4"

function Test-Hash([string]$path) {
    (Get-FileHash -Algorithm SHA256 -Path $path).Hash.ToLowerInvariant() -eq $expected
}

if ((Test-Path $dest) -and (Test-Hash $dest)) {
    Write-Host "Dummy video already present: $dest"
    exit 0
}

New-Item -ItemType Directory -Force -Path $dir | Out-Null
Write-Host "Downloading $url"
Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing

if (-not (Test-Hash $dest)) {
    Remove-Item -Force $dest
    Write-Error "Downloaded file does not match the expected SHA-256, deleted it."
    exit 1
}

Write-Host "Saved $dest"

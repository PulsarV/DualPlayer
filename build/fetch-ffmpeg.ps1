<#
Downloads one pinned LGPL win-x64 shared FFmpeg build, verifies its SHA-256 and unpacks the DLLs
into build/ffmpeg/ (git-ignored). The FFmpeg major version matches the FFmpeg.AutoGen package (8.x).
Fails without unpacking anything on a hash mismatch.

The pin is a dated BtbN autobuild release; BtbN prunes old autobuilds, so this URL will eventually
return 404. CI caches build/ffmpeg/ keyed on this script's hash, so a cached unpack survives the
pruning. When the URL dies, re-pin to a current lgpl-shared build of the same FFmpeg major and
update $url and $sha256 together.
#>
[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot 'ffmpeg')
)

$ErrorActionPreference = 'Stop'

$url    = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-11-11-12-39/ffmpeg-n8.1.3-35-gf1acb0a266-win64-lgpl-shared-8.1.zip'
$sha256 = 'f7697c270197c04bf72759c65ada64fcaea663bcb80767c390752d2412ea2dcf'

# A marker holding the pinned SHA-256 is written only after a complete unpack, so a partial, stale
# or differently pinned folder is re-fetched.
$marker = Join-Path $Destination '.sha256'
if ((Test-Path $marker) -and ((Get-Content -Raw $marker).Trim() -eq $sha256) -and
    (Test-Path (Join-Path $Destination 'avutil-60.dll'))) {
    Write-Host "FFmpeg $sha256 already present in $Destination"
    return
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("ffmpeg-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    $zip = Join-Path $tmp 'ffmpeg.zip'
    Invoke-WebRequest -Uri $url -OutFile $zip

    $actual = (Get-FileHash -Algorithm SHA256 -Path $zip).Hash.ToLowerInvariant()
    if ($actual -ne $sha256) {
        throw "SHA-256 mismatch for FFmpeg download: expected $sha256, got $actual. Nothing was unpacked."
    }

    $extract = Join-Path $tmp 'x'
    Expand-Archive -Path $zip -DestinationPath $extract
    if (Test-Path $Destination) {
        Remove-Item -Recurse -Force $Destination
    }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -Path $extract -Recurse -Filter '*.dll' |
        Where-Object { $_.Directory.Name -eq 'bin' } |
        Copy-Item -Destination $Destination -Force
    Set-Content -Path $marker -Value $sha256 -NoNewline
    Write-Host "FFmpeg unpacked to $Destination"
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}

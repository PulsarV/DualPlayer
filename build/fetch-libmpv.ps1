<#
Downloads one pinned LGPL win-x64 libmpv build (zhongfly/mpv-winbuild, `mpv-dev-lgpl-x86_64`), verifies
its SHA-256 and unpacks libmpv-2.dll into build/mpv/ (git-ignored). Fails without unpacking anything
on a hash mismatch.

The pin is a dated release; the project's builds prune old releases, so this URL will eventually
return 404. CI caches build/mpv/ keyed on this script's hash, so a cached unpack survives the
pruning. When the URL dies, re-pin to a current mpv-dev-lgpl-x86_64 asset and update $url and
$sha256 together. The archive is a .7z: it is unpacked with the Windows tar (libarchive) or 7z.
#>
[CmdletBinding()]
param(
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 shows a slow per-byte progress bar for Invoke-WebRequest.
$ProgressPreference = 'SilentlyContinue'

# Windows PowerShell 5.1 leaves $PSScriptRoot empty in param() defaults, so resolve it here.
if (-not $Destination) {
    $Destination = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'mpv'
}

$url    = 'https://github.com/zhongfly/mpv-winbuild/releases/download/2026-11-20-94127e1e1a/mpv-dev-lgpl-x86_64-20261120-git-94127e1e1a.7z'
$sha256 = '6edded03578c35ec1a89c1c50ad92bd52a79521dec6180f49fd697f5e18f367b'

# A marker holding the pinned SHA-256 is written only after a complete unpack, so a partial, stale
# or differently pinned folder is re-fetched.
$marker = Join-Path $Destination '.sha256'
if ((Test-Path $marker) -and ((Get-Content -Raw $marker).Trim() -eq $sha256) -and
    (Test-Path (Join-Path $Destination 'libmpv-2.dll'))) {
    Write-Host "libmpv $sha256 already present in $Destination"
    return
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("libmpv-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    $archive = Join-Path $tmp 'libmpv.7z'
    Invoke-WebRequest -Uri $url -OutFile $archive

    $actual = (Get-FileHash -Algorithm SHA256 -Path $archive).Hash.ToLowerInvariant()
    if ($actual -ne $sha256) {
        throw "SHA-256 mismatch for libmpv download: expected $sha256, got $actual. Nothing was unpacked."
    }

    $extract = Join-Path $tmp 'x'
    New-Item -ItemType Directory -Path $extract | Out-Null
    if (Get-Command 7z -ErrorAction SilentlyContinue) {
        & 7z x -y "-o$extract" $archive | Out-Null
    }
    else {
        & tar -xf $archive -C $extract
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Could not unpack the libmpv archive (exit code $LASTEXITCODE)."
    }

    $dll = Get-ChildItem -Path $extract -Recurse -Filter 'libmpv-2.dll' | Select-Object -First 1
    if (-not $dll) {
        throw 'libmpv-2.dll not found in the downloaded archive.'
    }
    if (Test-Path $Destination) {
        Remove-Item -Recurse -Force $Destination
    }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -Path $dll.FullName -Destination $Destination -Force
    Set-Content -Path $marker -Value $sha256 -NoNewline
    Write-Host "libmpv unpacked to $Destination"
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}

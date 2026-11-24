<#
Generates the click-and-flash test videos for the lip-sync acceptance (T23, spec AC-16): a black 1080p picture at
23.976 fps with one white frame at the start of every 1001 ms, and on every audio track a 5 ms 2 kHz click starting at
the same instant. Two stereo audio tracks (one per Viewer). Writes an H.264 and an HEVC file.

Any FFmpeg with libx264 and libx265 works (a GPL build is fine: this is a test tool, never shipped). On the dev Mac:
Homebrew's ffmpeg. On Windows: a full build on PATH, or pass -Ffmpeg. Checked on the dev Mac with the same filter graph:
flashes at 0.000/1.001/2.002 s, clicks at 1.001/2.002 s on both tracks.

Example:
  pwsh build/make-click-flash-video.ps1 -Minutes 10 -OutDir D:\Video\click-flash
#>
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path (Get-Location) 'click-flash'),
    [int]$Minutes = 10,
    [string]$Ffmpeg = 'ffmpeg'
)

$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$seconds = $Minutes * 60

# One white frame every 24 frames (1001 ms at 24000/1001 fps); the click repeats on the same 1001 ms period.
$video = "color=c=black:s=1920x1080:r=24000/1001,drawbox=x=0:y=0:w=iw:h=ih:color=white:t=fill:enable='lt(mod(n\,24)\,1)'"
$click = "aevalsrc='if(lt(mod(t\,1001/1000)\,0.005)\,0.8*sin(2*PI*2000*t)\,0)':s=48000:c=stereo"

$encodings = @(
    @{ Name = 'click-flash-h264.mkv'; Codec = @('-c:v', 'libx264', '-profile:v', 'high') },
    @{ Name = 'click-flash-hevc.mkv'; Codec = @('-c:v', 'libx265', '-profile:v', 'main', '-tag:v', 'hvc1') }
)

foreach ($e in $encodings) {
    $out = Join-Path $OutDir $e.Name
    $arguments = @(
        '-hide_banner', '-loglevel', 'error', '-y',
        '-f', 'lavfi', '-i', $video,
        '-f', 'lavfi', '-i', $click,
        '-f', 'lavfi', '-i', $click,
        '-t', "$seconds",
        '-map', '0:v', '-map', '1:a', '-map', '2:a'
    ) + $e.Codec + @(
        '-pix_fmt', 'yuv420p', '-g', '24', '-preset', 'veryfast',
        '-c:a', 'aac', '-b:a', '192k',
        '-metadata:s:a:0', 'title=Viewer 1', '-metadata:s:a:1', 'title=Viewer 2',
        $out
    )

    & $Ffmpeg @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "FFmpeg failed for $($e.Name) (exit $LASTEXITCODE)"
    }

    Write-Host "wrote $out ($Minutes min)"
}

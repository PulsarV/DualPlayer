# TestMedia notes

`long-gop-ramp.mkv` (12 s): one 64x48 x264 video stream with a single keyframe at 0 s (GOP of 300 frames at 25 fps),
plus two mono 48 kHz FLAC audio tracks whose sample value encodes the timeline: track 1 = t/12, track 2 = 0.5*t/12
(t in seconds, so value * 12 = content seconds on track 1). Generated with:

```sh
ffmpeg -f lavfi -i "color=c=gray:s=64x48:r=25:d=12" \
  -f lavfi -i "aevalsrc=t/12:s=48000:d=12:c=mono" -f lavfi -i "aevalsrc=0.5*t/12:s=48000:d=12:c=mono" \
  -map 0 -map 1 -map 2 -c:v libx264 -preset veryfast -g 300 -keyint_min 300 -sc_threshold 0 -pix_fmt yuv420p \
  -c:a flac -sample_fmt s16 -metadata:s:a:0 language=eng -metadata:s:a:1 language=ukr long-gop-ramp.mkv
```

`ramp-offset.ts` and `ramp-offset.mkv` (6 s each): mono 48 kHz audio-only files whose container timestamps start above 0
(`start_time` 1.4 s for the MPEG-TS, 600 s for the MKV). The sample value is t/6, so value * 6 = content seconds.
The TS is AC-3 (lossy, so compare with a tolerance); the MKV is FLAC. Generated with:

```sh
ffmpeg -f lavfi -i "aevalsrc=t/6:s=48000:d=6:c=mono" -c:a ac3 -b:a 64k ramp-offset.ts
ffmpeg -f lavfi -i "aevalsrc=t/6:s=48000:d=6:c=mono" -c:a flac -sample_fmt s16 -output_ts_offset 600 ramp-offset.mkv
```

Check with `ffprobe -show_entries format=start_time`.

`ramp-offset-span.mkv` (10 s, 81 KB): mono 48 kHz FLAC ramp (value = t/10, so value * 10 = content seconds) with
`start_time` 3 s whose Matroska Duration element holds the span (10 s), as mkvmerge writes it, instead of the end
timestamp (13 s) FFmpeg's muxer writes. mkvmerge is not needed: generate with FFmpeg, then overwrite the 8-byte
big-endian float after the Duration element id `44 89 88` (first occurrence, in the Segment Info) with 10000.0
(Duration is in TimeCodeScale units, 1 ms):

```sh
ffmpeg -f lavfi -i "aevalsrc=t/10:s=48000:d=10:c=mono" -c:a flac -sample_fmt s16 -output_ts_offset 3 ramp-offset-span.mkv
python3 -I - <<'PY'
import struct
b = bytearray(open('ramp-offset-span.mkv', 'rb').read())
i = b.find(b'\x44\x89\x88')
b[i+3:i+11] = struct.pack('>d', 10000.0)
open('ramp-offset-span.mkv', 'wb').write(b)
PY
```

Check with `ffprobe -show_entries format=start_time,duration` (3.0 and 10.0; before patching 3.0 and 13.0).

`tones-2.1.wav` (3 channels, with LFE) and `tones-6.0.wav` (hexagonal, 6 channels, no LFE): 0.2 s 440 Hz sine, 48 kHz PCM,
used to check that track info reports the real channel count and LFE presence (both map to `ChannelLayout.Other`):

```sh
ffmpeg -f lavfi -i "sine=f=440:d=0.2:r=48000" -af "aformat=channel_layouts=2.1" -c:a pcm_s16le tones-2.1.wav
ffmpeg -f lavfi -i "sine=f=440:d=0.2:r=48000" -af "aformat=channel_layouts=hexagonal" -c:a pcm_s16le tones-6.0.wav
```

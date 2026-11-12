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

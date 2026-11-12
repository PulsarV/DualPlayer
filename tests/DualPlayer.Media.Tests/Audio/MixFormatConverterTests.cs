using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Media.Audio;

namespace DualPlayer.Media.Tests.Audio;

/// <summary>AC-12: tracks are converted to the device mix format; LFE dropped, no upmix, correct pitch.</summary>
public sealed class MixFormatConverterTests
{
    private static readonly MixFormat Stereo48 = new(48000, 2, ChannelLayout.Stereo);
    private static readonly MixFormat Surround51At48 = new(48000, 6, ChannelLayout.Surround51);

    // Per-channel tones of tones-5.1.wav / tones-7.1.wav: FL FR FC LFE BL BR (SL SR).
    private const double Fl = 500, Fr = 700, Fc = 900, Lfe = 60, Bl = 1500, Br = 1900, Sl = 2300, Sr = 2700;
    private const double Audible = 0.01;

    private static string Media(string name) => Path.Combine(AppContext.BaseDirectory, "TestMedia", name);

    private static float[] Decode(string file, MixFormat target)
    {
        var opened = new FfmpegAudioDecoder().Open(Media(file), [new DecodeRequest(1, target)]);
        opened.IsSuccess.ShouldBeTrue(opened.Error?.Message);
        using var session = opened.Value;
        var all = new List<float>();
        for (var i = 0; i < 10_000; i++)
        {
            var read = session.Read();
            if (!read.IsSuccess)
            {
                break;
            }

            all.AddRange(read.Value.Samples);
        }

        all.Count.ShouldBeGreaterThan(0);
        return [.. all];
    }

    // Goertzel magnitude (amplitude-normalised) of one channel at one frequency, skipping the first 0.1 s.
    private static double Level(float[] interleaved, int channels, int channel, int rate, double freq)
    {
        var frames = interleaved.Length / channels;
        var start = rate / 10;
        var w = 2 * Math.PI * freq / rate;
        var coeff = 2 * Math.Cos(w);
        double s1 = 0, s2 = 0;
        var n = 0;
        for (var i = start; i < frames; i++, n++)
        {
            var s = interleaved[(i * channels) + channel] + (coeff * s1) - s2;
            s2 = s1;
            s1 = s;
        }

        var power = (s1 * s1) + (s2 * s2) - (coeff * s1 * s2);
        return 2 * Math.Sqrt(Math.Max(power, 0)) / n;
    }

    [Fact]
    public void A_5_1_track_on_a_stereo_device_folds_every_channel_except_LFE_into_left_and_right()
    {
        var pcm = Decode("tones-5.1.wav", Stereo48);

        foreach (var f in new[] { Fl, Fc, Bl })
        {
            Level(pcm, 2, 0, 48000, f).ShouldBeGreaterThan(Audible, $"{f} Hz must be heard on left");
        }

        foreach (var f in new[] { Fr, Fc, Br })
        {
            Level(pcm, 2, 1, 48000, f).ShouldBeGreaterThan(Audible, $"{f} Hz must be heard on right");
        }
    }

    [Fact]
    public void A_5_1_track_on_a_stereo_device_does_not_play_the_LFE_tone()
    {
        var pcm = Decode("tones-5.1.wav", Stereo48);

        Level(pcm, 2, 0, 48000, Lfe).ShouldBeLessThan(0.001);
        Level(pcm, 2, 1, 48000, Lfe).ShouldBeLessThan(0.001);
    }

    [Fact]
    public void A_7_1_track_on_a_stereo_device_folds_surrounds_in_and_drops_LFE()
    {
        var pcm = Decode("tones-7.1.wav", Stereo48);

        foreach (var f in new[] { Fl, Fc, Bl, Sl })
        {
            Level(pcm, 2, 0, 48000, f).ShouldBeGreaterThan(Audible, $"{f} Hz must be heard on left");
        }

        foreach (var f in new[] { Fr, Fc, Br, Sr })
        {
            Level(pcm, 2, 1, 48000, f).ShouldBeGreaterThan(Audible, $"{f} Hz must be heard on right");
        }

        Level(pcm, 2, 0, 48000, Lfe).ShouldBeLessThan(0.001);
        Level(pcm, 2, 1, 48000, Lfe).ShouldBeLessThan(0.001);
    }

    [Fact]
    public void A_44_1_kHz_track_on_a_48_kHz_device_keeps_its_pitch_and_duration()
    {
        var pcm = Decode("tone-stereo-44k.wav", Stereo48);

        var frames = pcm.Length / 2;
        frames.ShouldBeInRange(47_000, 49_000, "one second at 44.1 kHz is one second at 48 kHz");
        Level(pcm, 2, 0, 48000, 1000).ShouldBeGreaterThan(0.05);
        Level(pcm, 2, 0, 48000, 1088).ShouldBeLessThan(0.005); // 1000 * 48000 / 44100 would be the wrong-speed result
    }

    [Fact]
    public void A_mono_track_on_a_stereo_device_plays_the_same_signal_on_both_fronts()
    {
        var pcm = Decode("tone-mono.wav", Stereo48);

        Level(pcm, 2, 0, 48000, 800).ShouldBeGreaterThan(0.03);
        Level(pcm, 2, 1, 48000, 800).ShouldBe(Level(pcm, 2, 0, 48000, 800), 0.01);
    }

    [Fact]
    public void A_mono_track_on_a_5_1_device_plays_on_both_front_channels_only()
    {
        var pcm = Decode("tone-mono.wav", Surround51At48);

        Level(pcm, 6, 0, 48000, 800).ShouldBeGreaterThan(0.03);
        Level(pcm, 6, 1, 48000, 800).ShouldBeGreaterThan(0.03);
        for (var ch = 2; ch < 6; ch++)
        {
            Level(pcm, 6, ch, 48000, 800).ShouldBeLessThan(0.001, $"channel {ch} must stay silent (no upmix)");
        }
    }

    [Fact]
    public void A_stereo_track_on_a_5_1_device_plays_on_the_front_channels_only()
    {
        var pcm = Decode("tone-stereo-44k.wav", Surround51At48);

        Level(pcm, 6, 0, 48000, 1000).ShouldBeGreaterThan(0.03);
        Level(pcm, 6, 1, 48000, 1000).ShouldBeGreaterThan(0.03);
        for (var ch = 2; ch < 6; ch++)
        {
            Level(pcm, 6, ch, 48000, 1000).ShouldBeLessThan(0.001, $"channel {ch} must stay silent (no upmix)");
        }
    }

    [Fact]
    public void A_matching_5_1_layout_passes_every_channel_through_to_its_own_channel()
    {
        var pcm = Decode("tones-5.1.wav", Surround51At48);

        double[] tones = [Fl, Fr, Fc, Lfe, Bl, Br];
        for (var ch = 0; ch < 6; ch++)
        {
            Level(pcm, 6, ch, 48000, tones[ch]).ShouldBeGreaterThan(0.03, $"channel {ch} keeps its own tone");
        }
    }

    [Fact]
    public void The_same_track_on_two_devices_is_converted_independently_into_each_device_format()
    {
        var opened = new FfmpegAudioDecoder().Open(
            Media("tones-5.1.wav"),
            [new DecodeRequest(1, Stereo48), new DecodeRequest(1, Surround51At48)]);
        opened.IsSuccess.ShouldBeTrue(opened.Error?.Message);
        using var session = opened.Value;
        var widths = new Dictionary<int, int>();
        for (var i = 0; i < 10_000; i++)
        {
            var read = session.Read();
            if (!read.IsSuccess)
            {
                break;
            }

            widths[read.Value.OutputIndex] = widths.GetValueOrDefault(read.Value.OutputIndex) + read.Value.Samples.Length;
        }

        (widths[1] / widths[0]).ShouldBe(3, "6-channel output carries three times the samples of the stereo one");
    }
}

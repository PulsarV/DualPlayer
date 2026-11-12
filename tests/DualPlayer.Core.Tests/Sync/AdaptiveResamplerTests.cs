using DualPlayer.Core.Sync;

namespace DualPlayer.Core.Tests.Sync;

public sealed class AdaptiveResamplerTests
{
    private const int SampleRate = 48_000;
    private const double ToneHz = 1_000;
    private const double DriftPpm = 500e-6;

    // Everything that is not the fitted tone must stay this far below the tone (no new spectral peaks).
    private const double MaxResidualDb = -60;

    private sealed class SineReader(int channels, double hz = ToneHz, int limitFrames = int.MaxValue) : IFrameReader
    {
        public long Consumed { get; private set; }

        public int Channels => channels;

        public int Read(Span<float> destination)
        {
            var frames = Math.Min(destination.Length / channels, (int)Math.Min(limitFrames - Consumed, int.MaxValue));
            for (var i = 0; i < frames; i++)
            {
                var v = (float)Math.Sin(2 * Math.PI * hz * (Consumed + i) / SampleRate);
                for (var c = 0; c < channels; c++)
                {
                    destination[(i * channels) + c] = v;
                }
            }

            Consumed += frames;
            return frames;
        }
    }

    [Fact]
    public void Ratio_of_exactly_one_reproduces_the_input()
    {
        var resampler = new AdaptiveResampler(1);
        var reader = new SineReader(1);
        var output = new float[4_800];

        var produced = resampler.Process(reader, output, 1.0);

        produced.ShouldBe(output.Length);
        // The resampler may add a fixed latency of a few frames; the signal must match at some constant lag.
        var lag = Enumerable.Range(0, 8).First(l => Math.Abs(output[200] - Math.Sin(2 * Math.PI * ToneHz * (200 - l) / SampleRate)) < 1e-4);
        for (var i = 100; i < output.Length; i++)
        {
            output[i].ShouldBe((float)Math.Sin(2 * Math.PI * ToneHz * (i - lag) / SampleRate), 1e-4f);
        }
    }

    [Theory]
    [InlineData(1 + DriftPpm)]
    [InlineData(1 - DriftPpm)]
    public void Sine_keeps_its_expected_frequency_at_500_ppm(double ratio)
    {
        var (output, _) = Run(ratio, SampleRate * 4);

        var measured = MeasureFrequencyHz(output);

        // Consuming ratio input frames per output frame scales the tone by 1/ratio.
        measured.ShouldBe(ToneHz / ratio, 0.02);
    }

    [Theory]
    [InlineData(1 + DriftPpm)]
    [InlineData(1 - DriftPpm)]
    public void Sine_shows_no_new_spectral_peak_at_500_ppm(double ratio)
    {
        var (output, _) = Run(ratio, SampleRate * 2);

        var residualDb = ResidualDb(output.AsSpan(1_000), ToneHz / ratio);

        residualDb.ShouldBeLessThan(MaxResidualDb);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(100)]
    public void Realign_positive_drops_exactly_n_input_frames(int n)
    {
        var baseline = Run(1.0, 4_800).Consumed;
        var resampler = new AdaptiveResampler(1);
        var reader = new SineReader(1);
        resampler.Realign(n);

        resampler.Process(reader, new float[4_800], 1.0);

        (reader.Consumed - baseline).ShouldBe(n);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(100)]
    public void Realign_negative_inserts_exactly_n_frames_so_input_consumed_shrinks_by_n(int n)
    {
        var baseline = Run(1.0, 4_800).Consumed;
        var resampler = new AdaptiveResampler(1);
        var reader = new SineReader(1);
        resampler.Realign(-n);

        resampler.Process(reader, new float[4_800], 1.0);

        (baseline - reader.Consumed).ShouldBe(n);
    }

    [Fact]
    public void Realign_larger_than_the_available_input_carries_the_remainder_to_the_next_call()
    {
        var resampler = new AdaptiveResampler(1);
        var reader = new SineReader(1, limitFrames: 50);
        resampler.Realign(80);

        var produced = resampler.Process(reader, new float[256], 1.0);

        produced.ShouldBe(0);
        reader.Consumed.ShouldBe(50);

        var more = new SineReader(1);
        var baseline = Run(1.0, 256).Consumed;
        resampler.Process(more, new float[256], 1.0);
        more.Consumed.ShouldBe(baseline + 30);
    }

    [Fact]
    public void Input_running_dry_returns_the_frames_produced()
    {
        var resampler = new AdaptiveResampler(1);
        var reader = new SineReader(1, limitFrames: 100);

        var produced = resampler.Process(reader, new float[1_000], 1.0);

        produced.ShouldBeInRange(90, 100);
    }

    [Fact]
    public void Reset_makes_the_next_output_identical_to_a_fresh_resampler()
    {
        var used = new AdaptiveResampler(2);
        used.Process(new SineReader(2, 440), new float[2_000], 1.0003);
        used.Realign(7);
        used.Reset();
        var fresh = new AdaptiveResampler(2);
        var a = new float[2_000];
        var b = new float[2_000];

        used.Process(new SineReader(2), a, 1.0003);
        fresh.Process(new SineReader(2), b, 1.0003);

        a.ShouldBe(b);
    }

    [Fact]
    public void Process_allocates_zero_bytes()
    {
        var resampler = new AdaptiveResampler(2);
        var reader = new SineReader(2);
        var output = new float[2_048];
        resampler.Process(reader, output, 1.0002);
        resampler.Realign(3);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 50; i++)
        {
            resampler.Process(reader, output, 1.0002);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
    }

    private static (float[] Output, long Consumed) Run(double ratio, int frames)
    {
        var resampler = new AdaptiveResampler(1);
        var reader = new SineReader(1);
        var output = new float[frames];
        var done = 0;
        while (done < frames)
        {
            var n = Math.Min(480, frames - done);
            done += resampler.Process(reader, output.AsSpan(done, n), ratio);
        }

        return (output, reader.Consumed);
    }

    private static double MeasureFrequencyHz(float[] x)
    {
        double first = 0, last = 0;
        var count = 0;
        for (var i = 1000; i < x.Length; i++)
        {
            if (x[i - 1] < 0 && x[i] >= 0)
            {
                var t = (i - 1) + (-x[i - 1] / (double)(x[i] - x[i - 1]));
                if (count == 0)
                {
                    first = t;
                }

                last = t;
                count++;
            }
        }

        return (count - 1) * SampleRate / (last - first);
    }

    private static double ResidualDb(ReadOnlySpan<float> x, double hz)
    {
        // Least-squares fit of a sine at hz (phase-free), then compare what is left to the tone.
        double sc = 0, cc = 0, ssn = 0, xc = 0, xs = 0;
        for (var i = 0; i < x.Length; i++)
        {
            var w = 2 * Math.PI * hz * i / SampleRate;
            double c = Math.Cos(w), s = Math.Sin(w);
            cc += c * c;
            ssn += s * s;
            sc += s * c;
            xc += x[i] * c;
            xs += x[i] * s;
        }

        var det = (cc * ssn) - (sc * sc);
        var a = ((xc * ssn) - (xs * sc)) / det;
        var b = ((xs * cc) - (xc * sc)) / det;
        double toneEnergy = 0, residualEnergy = 0;
        for (var i = 0; i < x.Length; i++)
        {
            var w = 2 * Math.PI * hz * i / SampleRate;
            var fit = (a * Math.Cos(w)) + (b * Math.Sin(w));
            toneEnergy += fit * fit;
            residualEnergy += (x[i] - fit) * (x[i] - fit);
        }

        return 10 * Math.Log10(residualEnergy / toneEnergy);
    }
}

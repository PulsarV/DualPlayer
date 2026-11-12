using FFmpeg.AutoGen;

namespace DualPlayer.Media.Audio;

/// <summary>
/// Builds the rematrix from a source channel layout to a device layout (AC-12): LFE is dropped when folding down,
/// every other channel is folded in, a track with fewer channels plays on the front channels only (no upmix).
/// </summary>
internal static class FoldDownMatrix
{
    private const double Centre = 0.70710678;

    /// <summary>Returns a row-major matrix: element [out * source.Length + in] is the gain of source channel in to output out.</summary>
    public static double[] Build(IReadOnlyList<AVChannel> source, IReadOnlyList<AVChannel> device)
    {
        var inCount = source.Count;
        var matrix = new double[device.Count * inCount];

        if (SameLayout(source, device))
        {
            for (var i = 0; i < inCount; i++)
            {
                matrix[(i * inCount) + i] = 1;
            }

            return matrix;
        }

        var deviceHasStereo = IndexOf(device, AVChannel.AV_CHAN_FRONT_LEFT) >= 0 && IndexOf(device, AVChannel.AV_CHAN_FRONT_RIGHT) >= 0;
        if (source.Count > device.Count)
        {
            FoldDown(matrix, source, device, deviceHasStereo);
        }
        else
        {
            FrontOnly(matrix, source, device);
        }

        return matrix;
    }

    private static void FoldDown(double[] matrix, IReadOnlyList<AVChannel> source, IReadOnlyList<AVChannel> device, bool stereo)
    {
        var inCount = source.Count;
        var left = IndexOf(device, AVChannel.AV_CHAN_FRONT_LEFT);
        var right = IndexOf(device, AVChannel.AV_CHAN_FRONT_RIGHT);
        for (var i = 0; i < inCount; i++)
        {
            var (l, r) = Weights(source[i]);
            if (stereo)
            {
                matrix[(left * inCount) + i] = l;
                matrix[(right * inCount) + i] = r;
            }
            else
            {
                // Mono (or any non-stereo) device: one shared feed on every device channel.
                for (var o = 0; o < device.Count; o++)
                {
                    matrix[(o * inCount) + i] = (l + r) / 2;
                }
            }
        }

        // Normalise each output row so the fold-down cannot clip.
        for (var o = 0; o < device.Count; o++)
        {
            var sum = 0.0;
            for (var i = 0; i < inCount; i++)
            {
                sum += matrix[(o * inCount) + i];
            }

            if (sum > 1)
            {
                for (var i = 0; i < inCount; i++)
                {
                    matrix[(o * inCount) + i] /= sum;
                }
            }
        }
    }

    // Gain of one source channel into (left, right); LFE is 0.
    private static (double L, double R) Weights(AVChannel channel) => channel switch
    {
        AVChannel.AV_CHAN_FRONT_LEFT => (1, 0),
        AVChannel.AV_CHAN_FRONT_RIGHT => (0, 1),
        AVChannel.AV_CHAN_FRONT_CENTER or AVChannel.AV_CHAN_BACK_CENTER => (Centre, Centre),
        AVChannel.AV_CHAN_LOW_FREQUENCY or AVChannel.AV_CHAN_LOW_FREQUENCY_2 => (0, 0),
        AVChannel.AV_CHAN_BACK_LEFT or AVChannel.AV_CHAN_SIDE_LEFT => (Centre, 0),
        AVChannel.AV_CHAN_BACK_RIGHT or AVChannel.AV_CHAN_SIDE_RIGHT => (0, Centre),
        _ => (0, 0),
    };

    private static void FrontOnly(double[] matrix, IReadOnlyList<AVChannel> source, IReadOnlyList<AVChannel> device)
    {
        var inCount = source.Count;
        var left = IndexOf(device, AVChannel.AV_CHAN_FRONT_LEFT);
        var right = IndexOf(device, AVChannel.AV_CHAN_FRONT_RIGHT);
        for (var i = 0; i < inCount; i++)
        {
            var channel = source[i];
            if (channel == AVChannel.AV_CHAN_FRONT_CENTER && source.Count == 1 && left >= 0 && right >= 0)
            {
                matrix[(left * inCount) + i] = 1;
                matrix[(right * inCount) + i] = 1;
                continue;
            }

            var target = IndexOf(device, channel);
            if (target >= 0)
            {
                matrix[(target * inCount) + i] = 1;
            }
        }
    }

    private static bool SameLayout(IReadOnlyList<AVChannel> a, IReadOnlyList<AVChannel> b) =>
        a.Count == b.Count && a.SequenceEqual(b);

    private static int IndexOf(IReadOnlyList<AVChannel> list, AVChannel channel)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == channel)
            {
                return i;
            }
        }

        return -1;
    }
}

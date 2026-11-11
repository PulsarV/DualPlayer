namespace DualPlayer.Platform.Windows.Audio;

/// <summary>Converts WASAPI QPC readings (100 ns units) to the <see cref="TimeProvider"/> timestamp base.</summary>
internal static class QpcTimeBase
{
    private const long UnitsPerSecond = 10_000_000;

    /// <summary>Scales <paramref name="qpc100Ns"/> to ticks of <paramref name="timestampFrequency"/> without overflow.</summary>
    public static long ToTimestamp(long qpc100Ns, long timestampFrequency) =>
        (long)((Int128)qpc100Ns * timestampFrequency / UnitsPerSecond);
}

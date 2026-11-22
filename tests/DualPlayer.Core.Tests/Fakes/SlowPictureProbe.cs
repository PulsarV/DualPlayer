using DualPlayer.Core.Playback;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>
/// A picture probe that is slow: every call takes <c>cost</c> of the session's own (fake) time, or the whole limit it was given
/// when that is shorter, and then fails as a probe that gave up. <see cref="Time"/> is set once the rig exists.
/// </summary>
internal sealed class SlowPictureProbe(TimeSpan cost) : IPictureProbe
{
    public TimeProvider? Time { get; set; }

    public List<string> Probed { get; } = [];

    public Result<PictureInfo> Probe(string path, TimeSpan timeLimit)
    {
        Probed.Add(path);
        var spent = cost < timeLimit ? cost : timeLimit;
        var start = Time!.GetTimestamp();
        var end = start + (long)(spent.TotalSeconds * Time.TimestampFrequency);
        Time.SpinUntil(end);

        return Result<PictureInfo>.Failure("video.probe_timeout", "The picture probe ran out of time.");
    }
}

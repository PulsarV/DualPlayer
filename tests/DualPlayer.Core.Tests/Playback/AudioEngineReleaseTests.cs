using DualPlayer.Core.Playback;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>
/// How the start's release reads one output (ADR-0008): moved, still awaited, or not moved. A concurrent render can
/// tear the queue read, which must not turn a moved output into one that gets the film at once.
/// </summary>
public sealed class AudioEngineReleaseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_output_whose_queue_has_moved_counts_as_moved(bool pastDeadline) =>
        AudioEngine.ReadForRelease(queueRead: true, queuePlayed: 480, latestPlayed: 480, pastDeadline, stalled: false)
            .ShouldBe(AudioEngine.ReleaseReading.Moved);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_torn_queue_read_of_an_output_that_has_a_position_waits_for_the_next_poll(bool pastDeadline) =>
        AudioEngine.ReadForRelease(queueRead: false, queuePlayed: 480, latestPlayed: 480, pastDeadline, stalled: false)
            .ShouldBe(AudioEngine.ReleaseReading.Wait, "a torn read of a moved output must be read again, not reset to film at once");

    [Fact]
    public void An_output_that_has_not_moved_holds_the_release_before_the_deadline() =>
        AudioEngine.ReadForRelease(queueRead: true, queuePlayed: 0, latestPlayed: 0, pastDeadline: false, stalled: false)
            .ShouldBe(AudioEngine.ReleaseReading.Wait);

    [Fact]
    public void An_output_that_has_not_moved_by_the_deadline_no_longer_holds_the_release() =>
        AudioEngine.ReadForRelease(queueRead: true, queuePlayed: 0, latestPlayed: 0, pastDeadline: true, stalled: false)
            .ShouldBe(AudioEngine.ReleaseReading.NotMoved);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 480)]
    [InlineData(true, 480)]
    public void A_stalled_output_without_a_moved_queue_reading_does_not_hold_the_release(bool pastDeadline, long latestPlayed) =>
        AudioEngine.ReadForRelease(queueRead: false, queuePlayed: 0, latestPlayed, pastDeadline, stalled: true)
            .ShouldBe(AudioEngine.ReleaseReading.NotMoved);
}

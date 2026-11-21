using System.Diagnostics;
using DualPlayer.Media.Audio;
using DualPlayer.Media.Video;

namespace DualPlayer.Media.Tests;

/// <summary>
/// A cancelled open or probe returns promptly and lets go of the file (AC-09b): without that an abandoned worker keeps a hung
/// file open (on Windows that blocks rename and delete) and keeps its thread busy.
/// </summary>
public sealed class AbandonedWorkerTests
{
    private static readonly TimeSpan HangLimit = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Prompt = TimeSpan.FromSeconds(5);

    [Fact]
    public void A_cancelled_track_listing_of_a_stalled_file_returns_promptly_and_closes_the_file_AC_09b()
    {
        using var pipe = StalledPipe.CreateOrSkip();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var clock = Stopwatch.StartNew();
        var result = new FfmpegAudioDecoder().ListTracks(pipe.Path, cancel.Token);
        clock.Stop();

        result.IsSuccess.ShouldBeFalse("a cancelled listing is not a listing");
        clock.Elapsed.ShouldBeLessThan(Prompt, "the interrupted open kept waiting for the stalled file");
        pipe.ReaderClosed.Wait(Prompt, TestContext.Current.CancellationToken).ShouldBeTrue("the listing returned but left the file open");
    }

    [Fact]
    public void A_cancelled_picture_probe_of_a_stalled_file_returns_before_its_time_limit_and_closes_the_file_AC_09b()
    {
        using var pipe = StalledPipe.CreateOrSkip();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var clock = Stopwatch.StartNew();
        var result = new FfmpegPictureProbe().Probe(pipe.Path, HangLimit, cancel.Token);
        clock.Stop();

        result.IsSuccess.ShouldBeFalse("a cancelled probe is not a picture");
        clock.Elapsed.ShouldBeLessThan(Prompt, "the probe waited for its time limit instead of its cancellation");
        pipe.ReaderClosed.Wait(Prompt, TestContext.Current.CancellationToken).ShouldBeTrue("the probe returned but its worker kept the file open");
    }

    [Fact]
    public void A_picture_probe_that_runs_out_of_time_on_a_stalled_file_cancels_its_worker_and_closes_the_file_AC_09b()
    {
        using var pipe = StalledPipe.CreateOrSkip();

        var result = new FfmpegPictureProbe().Probe(pipe.Path, TimeSpan.FromMilliseconds(500));

        result.IsSuccess.ShouldBeFalse();
        pipe.ReaderClosed.Wait(Prompt, TestContext.Current.CancellationToken).ShouldBeTrue("the abandoned probe worker kept the stalled file open");
    }
}

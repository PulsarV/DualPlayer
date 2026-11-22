using System.Diagnostics;
using DualPlayer.Core;
using DualPlayer.Media.Audio;
using DualPlayer.Media.Video;
using FFmpeg.AutoGen;

namespace DualPlayer.Media.Tests;

/// <summary>
/// A cancelled open or probe returns promptly and lets go of the file (AC-09b): without that an abandoned worker keeps a hung
/// file open (on Windows that blocks rename and delete) and keeps its thread busy. The source is a <see cref="StalledPipe"/>
/// opened through FFmpeg's file protocol, the path the adapters take in production, on Windows and macOS alike. Each test
/// first checks that, uncancelled, the call is still reading the pipe, so it fails if the interrupt is taken out instead of
/// passing because the source ended the open by itself.
/// </summary>
public sealed class AbandonedWorkerTests
{
    private static readonly TimeSpan HangLimit = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Prompt = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StillReading = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task A_cancelled_track_listing_of_a_stalled_file_returns_promptly_with_an_interrupt_and_closes_the_file_AC_09b()
    {
        using var pipe = StalledPipe.CreateOrSkip();
        using var cancel = new CancellationTokenSource();
        var listing = Run(() => new FfmpegAudioDecoder().ListTracks(pipe.Path, cancel.Token));

        await ShouldStillBeReading(listing, pipe);
        cancel.Cancel();

        (await Finishes(listing, Prompt)).ShouldBeTrue("the interrupted open kept reading the stalled file");
        var result = await listing;
        result.IsSuccess.ShouldBeFalse("a cancelled listing is not a listing");
        result.Error.Message.ShouldContain(FfmpegAudioDecoder.ErrorText(ffmpeg.AVERROR_EXIT), Case.Sensitive, "the open did not end through the interrupt callback (AVERROR_EXIT)");
        pipe.ReaderClosed.Wait(Prompt, TestContext.Current.CancellationToken).ShouldBeTrue("the listing returned but left the file open");
    }

    [Fact]
    public async Task A_cancelled_picture_probe_of_a_stalled_file_returns_before_its_time_limit_and_closes_the_file_AC_09b()
    {
        using var pipe = StalledPipe.CreateOrSkip();
        using var cancel = new CancellationTokenSource();
        var probe = Run(() => new FfmpegPictureProbe().Probe(pipe.Path, HangLimit, cancel.Token));

        await ShouldStillBeReading(probe, pipe);
        cancel.Cancel();

        (await Finishes(probe, Prompt)).ShouldBeTrue("the probe waited for its time limit instead of its cancellation");
        (await probe).IsSuccess.ShouldBeFalse("a cancelled probe is not a picture");
        pipe.ReaderClosed.Wait(Prompt, TestContext.Current.CancellationToken).ShouldBeTrue("the probe returned but its worker kept the file open");
    }

    [Fact]
    public void A_picture_probe_that_runs_out_of_time_on_a_stalled_file_cancels_its_worker_and_closes_the_file_AC_09b()
    {
        using var pipe = StalledPipe.CreateOrSkip();
        var limit = TimeSpan.FromMilliseconds(500);

        var clock = Stopwatch.StartNew();
        var result = new FfmpegPictureProbe().Probe(pipe.Path, limit, TestContext.Current.CancellationToken);
        clock.Stop();

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("video.probe_timeout", "a probe whose own limit ran out before the first picture is a timeout, not an undecodable picture (AC-07, AC-08)");
        // The limit is counted on the tick clock, which can end a few ms before the Stopwatch agrees: allow slack.
        clock.Elapsed.ShouldBeGreaterThanOrEqualTo(limit - TimeSpan.FromMilliseconds(50), "the stalled file ended the probe by itself, so the test would not see the interrupt");
        pipe.ReaderClosed.Wait(Prompt, TestContext.Current.CancellationToken).ShouldBeTrue("the abandoned probe worker kept the stalled file open");
    }

    private static Task<Result<T>> Run<T>(Func<Result<T>> call) =>
        Task.Factory.StartNew(call, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    // The control: with no cancellation yet the call is still reading the pipe, so whatever ends it next is the interrupt.
    private static async Task ShouldStillBeReading(Task call, StalledPipe pipe)
    {
        (await Finishes(call, StillReading)).ShouldBeFalse("the stalled file ended the call by itself, so the test would not see the interrupt");
        pipe.ReaderClosed.IsSet.ShouldBeFalse("the stalled file was let go of before the call was cancelled");
    }

    private static async Task<bool> Finishes(Task call, TimeSpan within) =>
        await Task.WhenAny(call, Task.Delay(within, TestContext.Current.CancellationToken)) == call;
}

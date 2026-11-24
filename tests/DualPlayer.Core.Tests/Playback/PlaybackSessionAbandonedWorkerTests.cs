using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>
/// A neighbour whose track listing or picture probe hangs is given up on at the deadline (AC-09b): the abandoned worker is
/// cancelled, and the next press in the same direction steps past the file instead of rescanning it (AC-07, AC-08).
/// </summary>
public sealed class PlaybackSessionAbandonedWorkerTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("dualplayer-abandoned-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string File(string name)
    {
        var path = Path.Combine(_folder, name);
        System.IO.File.WriteAllBytes(path, []);
        return path;
    }

    [Fact]
    public void A_second_page_down_steps_past_two_hanging_neighbours_and_reaches_the_third_file_AC_09b()
    {
        var probe = new HangingPictureProbe("Episode-02.mkv", "Episode-03.mkv");
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourPictureProbe: probe, barrierEvery: NeighbourRig.BarrierEvery);
        probe.Time = rig.Engine.Time;
        File("Episode-02.mkv");
        File("Episode-03.mkv");
        var third = File("Episode-04.mkv");
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File("Episode-01.mkv") })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        rig.Engine.Call(() => rig.Session.NextFile());
        rig.Events.OfType<FileChanged>().ShouldBeEmpty("the first press cannot get past two hanging neighbours within its budget");
        probe.Probed.Clear();

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<NeighbourSearchTimedOut>().ShouldHaveSingleItem("the second press timed out again instead of stepping past");
        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File.ShouldBe(third);
        // E2 ran its full limit and is remembered, so it is not scanned again; E3 was only cut by the first press's budget (T88), so it is checked again.
        probe.Probed.Select(Path.GetFileName).ShouldBe(["Episode-03.mkv", "Episode-04.mkv"], "the second press rescanned a file that timed out on its own on the first");
    }

    [Fact]
    public void A_track_listing_that_hangs_is_cancelled_when_the_session_gives_up_on_the_file_AC_09b()
    {
        var decoder = new HangingListingDecoder();
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourDecoder: decoder, barrierEvery: NeighbourRig.BarrierEvery);
        decoder.Time = rig.Engine.Time;
        File("Episode-02.mkv");
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File("Episode-01.mkv") })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<NeighbourSearchTimedOut>().ShouldHaveSingleItem();
        decoder.Cancelled.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            .ShouldBeTrue("the abandoned listing worker was never told to stop");
    }

    [Fact]
    public void A_file_whose_track_listing_hung_is_stepped_past_on_the_next_press_AC_09b()
    {
        var decoder = new HangingListingDecoder();
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourDecoder: decoder, barrierEvery: NeighbourRig.BarrierEvery);
        decoder.Time = rig.Engine.Time;
        File("Episode-02.mkv");
        var next = File("Episode-03.mkv");
        decoder.HangOn = "Episode-02.mkv";
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File("Episode-01.mkv") })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        rig.Engine.Call(() => rig.Session.NextFile());
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        decoder.Listed.Clear();

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File.ShouldBe(next);
        decoder.Listed.ShouldNotContain("Episode-02.mkv", "the second press listed the hanging file again");
    }

    [Fact]
    public void A_picture_probe_that_timed_out_is_handed_a_token_the_session_cancels_when_it_gives_up_AC_09b()
    {
        var probe = new HangingPictureProbe("Episode-02.mkv");
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourPictureProbe: probe, barrierEvery: NeighbourRig.BarrierEvery);
        probe.Time = rig.Engine.Time;
        File("Episode-02.mkv");
        File("Episode-03.mkv");
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File("Episode-01.mkv") })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        rig.Engine.Call(() => rig.Session.NextFile());

        var token = probe.TokenOf("Episode-02.mkv");
        token.CanBeCanceled.ShouldBeTrue("the probe was given no way to be told to stop");
        token.IsCancellationRequested.ShouldBeTrue("the session gave up on the file but never cancelled its probe");
    }

    [Fact]
    public void Files_that_timed_out_are_tried_again_after_a_successful_file_change_AC_09b()
    {
        var probe = new HangingPictureProbe("Episode-03.mkv");
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourPictureProbe: probe, barrierEvery: NeighbourRig.BarrierEvery);
        probe.Time = rig.Engine.Time;
        var second = File("Episode-02.mkv");
        File("Episode-03.mkv");
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File("Episode-01.mkv") })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        rig.Engine.Call(() => rig.Session.NextFile()); // Episode-02 plays
        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File.ShouldBe(second);
        rig.Engine.Render(300);
        rig.Engine.Call(() => rig.Session.NextFile()); // Episode-03 hangs: remembered
        probe.Probed.Count(p => Path.GetFileName(p) == "Episode-03.mkv").ShouldBe(1);

        rig.Engine.Call(() => rig.Session.PreviousFile()); // back to Episode-01: the remembered set is cleared
        rig.Engine.Render(300);
        probe.Probed.Clear();
        rig.Engine.Call(() => rig.Session.NextFile());
        rig.Engine.Render(300);
        rig.Engine.Call(() => rig.Session.NextFile());

        probe.Probed.Select(Path.GetFileName).ShouldContain("Episode-03.mkv", "a file change should clear the remembered timeouts");
    }

    /// <summary>Hangs (on the session's fake time) for the named files until its limit or its token runs out; every other file probes fine.</summary>
    private sealed class HangingPictureProbe(params string[] hanging) : IPictureProbe
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, CancellationToken> _tokens = new(StringComparer.Ordinal);

        public TimeProvider? Time { get; set; }

        public List<string> Probed { get; } = [];

        public CancellationToken TokenOf(string fileName)
        {
            lock (_gate)
            {
                return _tokens[fileName];
            }
        }

        public Result<PictureInfo> Probe(string path, TimeSpan timeLimit) => Probe(path, timeLimit, CancellationToken.None);

        public Result<PictureInfo> Probe(string path, TimeSpan timeLimit, CancellationToken cancel)
        {
            var name = Path.GetFileName(path);
            lock (_gate)
            {
                Probed.Add(path);
                _tokens[name] = cancel;
            }

            if (!hanging.Contains(name))
            {
                return Result<PictureInfo>.Success(new PictureInfo("h264", "High", 1920, 1080));
            }

            var end = Time!.GetTimestamp() + (long)(timeLimit.TotalSeconds * Time.TimestampFrequency);
            Time.SpinUntil(end, cancel);

            return Result<PictureInfo>.Failure("video.probe_timeout", "The picture probe ran out of time.");
        }
    }

    /// <summary>Lists tracks 1 and 2 for every file; the file named in <see cref="HangOn"/> (any file when null) blocks until cancelled.</summary>
    private sealed class HangingListingDecoder : IAudioDecoder
    {
        public ManualResetEventSlim Cancelled { get; } = new();

        public TimeProvider? Time { get; set; }

        public string? HangOn { get; set; }

        public List<string> Listed { get; } = [];

        public Result<IReadOnlyList<AudioTrackInfo>> ListTracks(string path) => ListTracks(path, CancellationToken.None);

        public Result<IReadOnlyList<AudioTrackInfo>> ListTracks(string path, CancellationToken cancel)
        {
            var name = Path.GetFileName(path);
            lock (Listed)
            {
                Listed.Add(name);
            }

            if (HangOn is not null && HangOn != name)
            {
                return Result<IReadOnlyList<AudioTrackInfo>>.Success([.. new[] { 1, 2 }.Select(n => new AudioTrackInfo(n, "und", ChannelLayout.Stereo, 48_000))]);
            }

            using var waiting = Time.Waiting();
            if (cancel.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
            {
                Cancelled.Set();
            }

            return Result<IReadOnlyList<AudioTrackInfo>>.Failure("playback.file_unreadable", "The listing was cancelled.");
        }

        public Result<IDecodeSession> Open(string path, IReadOnlyList<DecodeRequest> outputs) =>
            Result<IDecodeSession>.Failure("test.not_decoding", "The listing decoder does not decode.");
    }
}

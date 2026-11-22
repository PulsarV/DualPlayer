using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>
/// T97 (review-2026-11-22c U2-U4): a neighbour that had the press's whole budget and still could not be judged is remembered;
/// a remembered file the scan steps past is reported as skipped (unreadable); a live worker is never started twice.
/// </summary>
public sealed class PlaybackSessionNeighbourRememberedFilesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("dualplayer-remembered-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string File(string name)
    {
        var path = Path.Combine(_folder, name);
        System.IO.File.WriteAllBytes(path, []);
        return path;
    }

    private SessionRig Playing(IAudioDecoder? decoder, IPictureProbe? probe)
    {
        var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourDecoder: decoder, neighbourPictureProbe: probe);
        (decoder as ITimed)?.Attach(rig.Engine.Time);
        (probe as ITimed)?.Attach(rig.Engine.Time);
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File("Episode-01.mkv") })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    [Fact]
    public void A_neighbour_whose_listing_and_probe_together_need_more_than_the_press_budget_is_remembered_and_the_next_press_reaches_the_file_after_it_AC_09b()
    {
        var decoder = new TimedDecoder { ListingCost = TimeSpan.FromMilliseconds(1200) };
        var probe = new TimedProbe { ProbeCost = TimeSpan.FromMilliseconds(1900) };
        File("Episode-02.mkv");
        var third = File("Episode-03.mkv");
        using var rig = Playing(decoder, probe);
        decoder.OnlyFor = "Episode-02.mkv";
        probe.OnlyFor = "Episode-02.mkv";

        rig.Engine.Call(() => rig.Session.NextFile());
        rig.Events.OfType<FileChanged>().ShouldBeEmpty("the first press cannot judge E2 within its budget");

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("the second press never got past E2, which had the whole budget the first time").File.ShouldBe(third);
    }

    [Fact]
    public void A_remembered_file_passed_over_before_the_target_is_reported_as_skipped_unreadable_and_counted_AC_09b()
    {
        var probe = new TimedProbe { HangOn = "Episode-02.mkv", AlsoHangOn = "Episode-03.mkv" };
        File("Episode-02.mkv");
        var third = File("Episode-03.mkv");
        using var rig = Playing(null, probe);

        rig.Engine.Call(() => rig.Session.NextFile()); // E2 hangs its full limit: remembered; E3 is only cut by the budget
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        probe.AlsoHangOn = null;
        var skipsBefore = rig.Events.OfType<FileSkipped>().Count();

        rig.Engine.Call(() => rig.Session.NextFile()); // E3 is checked first and plays; E2 was deferred before it

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File.ShouldBe(third);
        var skip = rig.Events.OfType<FileSkipped>().Skip(skipsBefore).ShouldHaveSingleItem("the remembered file passed over was not reported");
        skip.File.ShouldBe("Episode-02.mkv");
        skip.Reason.ShouldBe("unreadable");
        rig.Events.OfType<NoteShown>().Select(e => e.Text).ShouldContain(t => t.StartsWith("Skipped Episode-02.mkv", StringComparison.Ordinal));
    }

    [Fact]
    public void A_listing_that_never_returns_is_started_once_however_many_presses_find_nothing_else_AC_09b()
    {
        using var decoder = new TimedDecoder { NeverReturnsOn = "Episode-02.mkv" };
        File("Episode-02.mkv");
        using var rig = Playing(decoder, null);

        rig.Engine.Call(() => rig.Session.NextFile());
        rig.Engine.Call(() => rig.Session.NextFile());
        rig.Engine.Call(() => rig.Session.NextFile());

        decoder.Listed.Count(n => n == "Episode-02.mkv").ShouldBe(1, "a press restarted the worker that was still blocked");
    }

    private interface ITimed
    {
        void Attach(TimeProvider time);
    }

    private sealed class TimedProbe : IPictureProbe, ITimed
    {
        private TimeProvider? _time;

        public TimeSpan ProbeCost { get; set; }

        public string? OnlyFor { get; set; }

        public string? HangOn { get; set; }

        public string? AlsoHangOn { get; set; }

        public void Attach(TimeProvider time) => _time = time;

        public Result<PictureInfo> Probe(string path, TimeSpan timeLimit) => Probe(path, timeLimit, CancellationToken.None);

        public Result<PictureInfo> Probe(string path, TimeSpan timeLimit, CancellationToken cancel)
        {
            var name = Path.GetFileName(path);
            var hang = HangOn == name || AlsoHangOn == name;
            var cost = hang ? TimeSpan.MaxValue : OnlyFor is null || OnlyFor == name ? ProbeCost : TimeSpan.Zero;
            var spent = cost < timeLimit ? cost : timeLimit;
            var end = _time!.GetTimestamp() + (long)(spent.TotalSeconds * _time.TimestampFrequency);
            while (_time.GetTimestamp() < end && !cancel.IsCancellationRequested)
            {
                Thread.Sleep(1);
            }

            return hang || cost > timeLimit
                ? Result<PictureInfo>.Failure("video.probe_timeout", "The probe gave up.")
                : Result<PictureInfo>.Success(new PictureInfo("h264", "High", 1920, 1080));
        }
    }

    private sealed class TimedDecoder : IAudioDecoder, ITimed, IDisposable
    {
        private readonly ManualResetEventSlim _release = new();
        private TimeProvider? _time;

        public TimeSpan ListingCost { get; set; }

        public string? OnlyFor { get; set; }

        public string? NeverReturnsOn { get; set; }

        public List<string> Listed { get; } = [];

        public void Attach(TimeProvider time) => _time = time;

        public void Dispose() => _release.Set();

        public Result<IReadOnlyList<AudioTrackInfo>> ListTracks(string path)
        {
            var name = Path.GetFileName(path);
            lock (Listed)
            {
                Listed.Add(name);
            }

            if (NeverReturnsOn == name)
            {
                _release.Wait(TimeSpan.FromSeconds(30)); // ignores the token: an uninterruptible native call
            }
            else if (_time is not null && ListingCost > TimeSpan.Zero && (OnlyFor is null || OnlyFor == name))
            {
                var end = _time.GetTimestamp() + (long)(ListingCost.TotalSeconds * _time.TimestampFrequency);
                while (_time.GetTimestamp() < end)
                {
                    Thread.Sleep(1);
                }
            }

            return Result<IReadOnlyList<AudioTrackInfo>>.Success([.. new[] { 1, 2 }.Select(n => new AudioTrackInfo(n, "und", ChannelLayout.Stereo, 48_000))]);
        }

        public Result<IDecodeSession> Open(string path, IReadOnlyList<DecodeRequest> outputs) =>
            Result<IDecodeSession>.Failure("test.not_decoding", "The listing decoder does not decode.");
    }
}

using DualPlayer.App.Startup;
using DualPlayer.Core;
using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;

namespace DualPlayer.App.Tests.Startup;

// Raw-writer assertions: the exact text of every terminal line of contracts/cli.md 1.1, 2.2 and 2.3, with no whitespace squashing,
// so a lost label padding or a changed two-space separator fails here.
public sealed class TerminalPrinterRawLineTests
{
    private const string DevA = "{0.0.0.00000000}.{0001}";
    private const string DevB = "{0.0.0.00000000}.{0002}";

    private static readonly OutputLatency OutA = new(DevA, DeviceType.BluetoothHeadphones, 0, 200, 40, 160, 40);
    private static readonly OutputLatency OutB = new(DevB, DeviceType.BluetoothHeadphones, 0, 200, 0, 200, 0);

    [Fact]
    public void The_start_lines_keep_their_exact_two_space_separators_and_label_padding()
    {
        var lines = Raw(
            s =>
            {
                s.Publish(new LatencyEstimated(new LatencyEstimate([OutA, OutB], 200)));
                s.Publish(new StartTimings(0, 1900, new Dictionary<string, long> { [DevA] = 1900, [DevB] = 1900 }));
            },
            e => e.Publish(new AudioStarted(TimeSpan.Zero, TimeSpan.FromSeconds(1.9))));

        lines.ShouldBe(
        [
            "output 1  track 1  device Test Headset A  type bluetooth-headphones  reported 0  default 200  offset +40  assumed 160  lag 40",
            "output 2  track 2  device Test Headset B  type bluetooth-headphones  reported 0  default 200  offset 0  assumed 200  lag 0",
            "picture   delay 200  screen 1  1920x1080",
            "started   clock 00:00:00.000  first-frame 1.9 s  first-audio 1.9 s 1.9 s",
        ]);
    }

    [Fact]
    public void A_lost_output_line_is_output_N_two_spaces_lost()
    {
        var lines = Raw(
            s => s.Publish(new LatencyEstimated(new LatencyEstimate(
                [OutB with { OutputNumber = 1, IsLost = true }, OutA with { OutputNumber = 2, AssumedMs = 0, LagMs = 0 }],
                0))),
            null);

        lines[0].ShouldBe("output 1  lost");
    }

    [Fact]
    public void The_file_change_lines_pad_the_label_to_eight_characters_then_one_space()
    {
        var lines = Raw(
            s =>
            {
                s.Publish(new FileChanged("b.mkv", "a.mkv"));
                s.Publish(new FileChanged("a.mkv", "b.mkv") { ToPrevious = true });
            },
            null);

        lines.ShouldBe(["next     b.mkv", "previous a.mkv"]);
    }

    [Fact]
    public void The_skipped_lines_keep_two_spaces_between_every_field()
    {
        var lines = Raw(
            s =>
            {
                s.Publish(new FileSkipped("b.mkv", "track-missing", 2, 2));
                s.Publish(new FileSkipped("c.mkv", "not-hardware-decodable", null, null) { Codec = "hevc", Profile = "Main 10" });
                s.Publish(new FileSkipped("d.mkv", "unreadable", null, null));
                s.Publish(new FileSkipped("e.mkv", "access-denied", null, null));
            },
            null);

        lines.ShouldBe(
        [
            "skipped  b.mkv  reason track-missing  output 2  track 2",
            "skipped  c.mkv  reason not-hardware-decodable  codec hevc  profile main-10",
            "skipped  d.mkv  reason unreadable",
            "skipped  e.mkv  reason access-denied",
        ]);
    }

    [Fact]
    public void The_neighbour_search_lines_are_exact()
    {
        var lines = Raw(
            s =>
            {
                s.Publish(new NoNeighbour(Next: true));
                s.Publish(new NoNeighbour(Next: false));
                s.Publish(new NeighbourSearchTimedOut(Next: true, Checked: 12));
                s.Publish(new NeighbourSearchTimedOut(Next: false, Checked: 1));
            },
            null);

        lines.ShouldBe(["no-next-file", "no-previous-file", "next-file-timeout  checked 12", "previous-file-timeout  checked 1"]);
    }

    [Fact]
    public void The_device_lost_and_picture_failed_lines_keep_two_space_separators()
    {
        var lines = Raw(
            s =>
            {
                s.Publish(new OutputLost(2, DevB, new TimeSpan(0, 0, 40, 12, 300), OthersContinue: true) { OldPictureDelayMs = 200, NewPictureDelayMs = 0 });
                s.Publish(new OutputLost(1, DevA, new TimeSpan(0, 0, 41, 0, 0), OthersContinue: false));
                s.Publish(new PictureFailed(new TimeSpan(1, 10, 5) + TimeSpan.FromMilliseconds(120), "decode"));
            },
            null);

        lines.ShouldBe(
        [
            $"device-lost  output 2  device {DevB}  clock 00:40:12.300  others continue  picture-delay 200 -> 0",
            $"device-lost  output 1  device {DevA}  clock 00:41:00.000  last output, film ended",
            "picture-failed  clock 01:10:05.120  the picture could not be decoded further, film ended",
        ]);
    }

    [Fact]
    public void The_run_summary_header_keeps_two_spaces_between_fields()
    {
        var lines = Raw(
            null,
            e => e.Publish(new RunSummary(RunEndReason.FileEnd, TimeSpan.FromSeconds(20), [new OutputSummary(DevA, TimeSpan.FromSeconds(20), 3.5, 1.2, 4, 0, [])], "a.mkv")));

        lines[0].ShouldBe("run-summary  file a.mkv  reason end");
    }

    [Fact]
    public void The_offset_warning_lines_are_exact()
    {
        var lines = Raw(
            s =>
            {
                s.Publish(new OffsetsWarning(new Error("state.unreadable", "store wording")));
                s.Publish(new OffsetsWarning(new Error("offsets.clamped", "device {0001} offset 900 -> 500")));
            },
            null);

        lines.ShouldBe(
        [
            "warning state.unreadable: remembered offsets could not be read, every output starts at offset 0",
            "warning offsets.clamped: device {0001} offset 900 -> 500",
        ]);
    }

    private static List<string> Raw(Action<Feed<SessionEvent>>? session, Action<Feed<EngineEvent>>? engine)
    {
        var raw = new StringWriter();
        var sessionFeed = new Feed<SessionEvent>();
        var engineFeed = new Feed<EngineEvent>();
        var context = new TerminalContext(
            [new OutputLabel(1, "Test Headset A"), new OutputLabel(2, "Test Headset B")],
            new ScreenInfo(1, 1920, 1080, true),
            "a.mkv",
            new FixedFrequencyTime());
        using (new TerminalPrinter(engineFeed, sessionFeed, raw, new StringWriter(), null, context))
        {
            session?.Invoke(sessionFeed);
            engine?.Invoke(engineFeed);
        }

        return raw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
    }

    private sealed class FixedFrequencyTime : TimeProvider
    {
        public override long TimestampFrequency => 1000;
    }

    private sealed class Feed<T> : IObservable<T>
    {
        private readonly List<IObserver<T>> _observers = [];

        public IDisposable Subscribe(IObserver<T> observer)
        {
            _observers.Add(observer);
            return new Unsubscribe(() => _observers.Remove(observer));
        }

        public void Publish(T value)
        {
            foreach (var o in _observers.ToArray())
            {
                o.OnNext(value);
            }
        }

        private sealed class Unsubscribe(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }
}

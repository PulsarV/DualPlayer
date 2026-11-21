using DualPlayer.App.Startup;
using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using Microsoft.Extensions.Logging;

namespace DualPlayer.App.Tests.Startup;

public sealed class SessionEventLogWriterTests
{
    private const string Dev = "{0.0.0.00000000}.{0001}";

    private static readonly OutputLatency Out = new(Dev, DeviceType.BluetoothHeadphones, 10, 200, 40, 170, 30);

    [Fact]
    public void Latency_estimated_is_logged_per_output_with_the_reported_default_offset_split()
    {
        var h = new Harness();
        h.Feed.Publish(new LatencyEstimated(new LatencyEstimate([Out], 200)));

        var e = h.Log.Entries.ShouldHaveSingleItem();
        e["Output"].ShouldBe(1);
        e["DeviceId"].ShouldBe(Dev);
        e["ReportedMs"].ShouldBe(10);
        e["DefaultMs"].ShouldBe(200);
        e["OffsetMs"].ShouldBe(40);
        e["AssumedMs"].ShouldBe(170);
        e["LagMs"].ShouldBe(30);
        e["PictureDelayMs"].ShouldBe(200);
    }

    [Fact]
    public void Picture_delay_change_is_logged_with_clock_cause_and_old_and_new_delay()
    {
        var h = new Harness();
        h.Feed.Publish(new PictureDelayChanged(TimeSpan.FromSeconds(75), PictureDelayCause.Offset, 200, 240, [Out]));

        var e = h.Log.Entries[0];
        e["Clock"].ShouldBe("00:01:15.000");
        e["Cause"].ShouldBe("offset");
        e["OldPictureDelayMs"].ShouldBe(200);
        e["NewPictureDelayMs"].ShouldBe(240);
        h.Log.Entries.Count.ShouldBe(2);
        h.Log.Entries[1]["LagMs"].ShouldBe(30);
    }

    [Fact]
    public void Offset_change_picture_timing_frame_counters_and_start_timings_are_logged_with_their_fields()
    {
        var h = new Harness();
        h.Feed.Publish(new OffsetChanged(2, Dev, 0, 10, null));
        h.Feed.Publish(new PictureTimingReported(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(1.51), 10.0));
        h.Feed.Publish(new FrameCountersReported(TimeSpan.FromSeconds(60), new FrameCounters(3, 2, 1)));
        h.Feed.Publish(new StartTimings(5, 1905, new Dictionary<string, long> { [Dev] = 1950 }));

        var entries = h.Log.Entries;
        entries[0]["Output"].ShouldBe(2);
        entries[0]["OldOffsetMs"].ShouldBe(0);
        entries[0]["NewOffsetMs"].ShouldBe(10);
        entries[1]["Clock"].ShouldBe("00:00:01.000");
        entries[1]["FrameDue"].ShouldBe("00:00:01.500");
        entries[1]["Presented"].ShouldBe("00:00:01.510");
        entries[1]["ErrorMs"].ShouldBe(10.0);
        entries[2]["Late"].ShouldBe(3L);
        entries[2]["Dropped"].ShouldBe(2L);
        entries[2]["SkippedDamaged"].ShouldBe(1L);
        entries[3]["CommandTimestamp"].ShouldBe(5L);
        entries[3]["FirstFramePresented"].ShouldBe(1905L);
        entries[3]["FirstAudioHeard"].ShouldBe($"{Dev}=1950");
    }

    [Fact]
    public void Nothing_is_logged_after_dispose()
    {
        var h = new Harness();
        h.Writer.Dispose();
        h.Feed.Publish(new OffsetChanged(1, Dev, 0, 10, null));

        h.Log.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Each_output_latency_is_logged_with_its_source()
    {
        var h = new Harness();
        h.Feed.Publish(new LatencyEstimated(new LatencyEstimate([Out with { Source = "wasapi-stream-latency" }], 200)));

        h.Log.Entries.ShouldHaveSingleItem()["Source"].ShouldBe("wasapi-stream-latency");
    }

    [Fact]
    public void Lip_synced_again_is_logged_with_the_command_and_how_long_after_it()
    {
        var h = new Harness();
        h.Feed.Publish(new LipSyncedAgain(TimeSpan.FromSeconds(75), "seek", 420.5, -3.25));

        var e = h.Log.Entries.ShouldHaveSingleItem();
        e["{OriginalFormat}"]!.ToString()!.ShouldStartWith("LipSyncedAgain");
        e["Clock"].ShouldBe("00:01:15.000");
        e["Command"].ShouldBe("seek");
        e["AfterCommandMs"].ShouldBe(420.5);
        e["ErrorMs"].ShouldBe(-3.25);
    }

    [Fact]
    public void Lip_synced_again_is_logged_with_each_outputs_position_error()
    {
        const string Other = "{0.0.0.00000000}.{0002}";
        var h = new Harness();
        h.Feed.Publish(new LipSyncedAgain(TimeSpan.FromSeconds(75), "seek", 420.5, -3.25)
        {
            OutputErrorsMs = new Dictionary<string, double> { [Dev] = 2.5, [Other] = -4.25 },
        });

        var e = h.Log.Entries.ShouldHaveSingleItem();
        e["{OriginalFormat}"]!.ToString()!.ShouldStartWith("LipSyncedAgain");
        var errors = e["OutputErrorsMs"].ShouldBeOfType<string>();
        errors.ShouldContain($"{Dev}=2.5");
        errors.ShouldContain($"{Other}=-4.25");
    }

    [Fact]
    public void Not_lip_synced_is_logged_with_the_cause_the_picture_error_and_each_outputs_error()
    {
        const string Other = "{0.0.0.00000000}.{0002}";
        var h = new Harness();
        h.Feed.Publish(new NotLipSynced(TimeSpan.FromSeconds(75), "seek", 1000.5, 2.5)
        {
            OutputErrorsMs = new Dictionary<string, double> { [Dev] = 30, [Other] = -4.25 },
        });

        var e = h.Log.Entries.ShouldHaveSingleItem();
        e["{OriginalFormat}"]!.ToString()!.ShouldStartWith("NotLipSynced");
        e["Clock"].ShouldBe("00:01:15.000");
        e["Command"].ShouldBe("seek");
        e["AfterCommandMs"].ShouldBe(1000.5);
        e["ErrorMs"].ShouldBe(2.5);
        var errors = e["OutputErrorsMs"].ShouldBeOfType<string>();
        errors.ShouldContain($"{Dev}=30");
        errors.ShouldContain($"{Other}=-4.25");
    }

    [Fact]
    public void A_skipped_damaged_stretch_is_logged_with_where_how_long_and_how_many_frames()
    {
        var h = new Harness();
        h.Feed.Publish(new DamagedStretchSkipped(TimeSpan.FromSeconds(75), TimeSpan.FromMilliseconds(400), 12));

        var e = h.Log.Entries.ShouldHaveSingleItem();
        e["{OriginalFormat}"]!.ToString()!.ShouldStartWith("DamagedStretchSkipped");
        e["Clock"].ShouldBe("00:01:15.000");
        e["LengthMs"].ShouldBe(400.0);
        e["Frames"].ShouldBe(12L);
    }

    [Theory]
    [InlineData(PictureDelayCause.Offset, "offset")]
    [InlineData(PictureDelayCause.DeviceLost, "device-lost")]
    [InlineData(PictureDelayCause.Seek, "seek")]
    [InlineData(PictureDelayCause.Pause, "pause")]
    [InlineData(PictureDelayCause.Resume, "resume")]
    [InlineData(PictureDelayCause.FileChange, "file-change")]
    public void The_picture_delay_cause_is_logged_as_cli_md_names_it(PictureDelayCause cause, string logged)
    {
        var h = new Harness();
        h.Feed.Publish(new PictureDelayChanged(TimeSpan.FromSeconds(1), cause, 200, 240, []));

        h.Log.Entries[0]["Cause"].ShouldBe(logged);
    }

    [Theory]
    [InlineData(DeviceType.BluetoothHeadphones, "bluetooth-headphones")]
    [InlineData(DeviceType.BluetoothSpeaker, "bluetooth-speaker")]
    [InlineData(DeviceType.Wired, "wired")]
    [InlineData(DeviceType.Hdmi, "hdmi")]
    [InlineData(DeviceType.Other, "other")]
    public void The_device_type_is_logged_as_cli_md_names_it(DeviceType type, string logged)
    {
        var h = new Harness();
        h.Feed.Publish(new LatencyEstimated(new LatencyEstimate([Out with { Type = type }], 200)));

        h.Log.Entries.ShouldHaveSingleItem()["Type"].ShouldBe(logged);
    }

    [Theory]
    [InlineData(DualPlayer.Core.Diagnostics.RunEndReason.FileChange, "file-change")]
    [InlineData(DualPlayer.Core.Diagnostics.RunEndReason.PictureFailed, "picture-failed")]
    [InlineData(DualPlayer.Core.Diagnostics.RunEndReason.Stop, "exit")]
    [InlineData(DualPlayer.Core.Diagnostics.RunEndReason.FileEnd, "end")]
    [InlineData(DualPlayer.Core.Diagnostics.RunEndReason.AllDevicesLost, "last-output-lost")]
    public void The_run_summary_reason_is_printed_as_cli_md_names_it(DualPlayer.Core.Diagnostics.RunEndReason reason, string printed) =>
        RunSummaryLines.Reason(reason).ShouldBe(printed);

    private sealed class Harness
    {
        public Harness()
        {
            Writer = new SessionEventLogWriter(Feed, Log);
        }

        public Feed Feed { get; } = new();

        public CaptureLogger Log { get; } = new();

        public SessionEventLogWriter Writer { get; }
    }

    private sealed class Feed : IObservable<SessionEvent>
    {
        private readonly List<IObserver<SessionEvent>> _observers = [];

        public IDisposable Subscribe(IObserver<SessionEvent> observer)
        {
            _observers.Add(observer);
            return new Unsubscribe(() => _observers.Remove(observer));
        }

        public void Publish(SessionEvent value)
        {
            foreach (var o in _observers.ToList())
            {
                o.OnNext(value);
            }
        }

        private sealed class Unsubscribe(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }

    private sealed class CaptureLogger : ILogger
    {
        public List<Dictionary<string, object?>> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var fields = new Dictionary<string, object?>();
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var p in pairs)
                {
                    fields[p.Key] = p.Value;
                }
            }

            Entries.Add(fields);
        }
    }
}

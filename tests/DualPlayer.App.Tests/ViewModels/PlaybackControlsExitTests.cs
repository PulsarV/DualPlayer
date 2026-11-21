using DualPlayer.App.ViewModels;
using DualPlayer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace DualPlayer.App.Tests.ViewModels;

/// <summary>T39: once the window has exited (Esc or Alt+F4), keys still in flight are dropped before they reach the session.</summary>
public sealed class PlaybackControlsExitTests : IDisposable
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];
    private readonly string _folder = Directory.CreateTempSubdirectory("dualplayer-controls-exit-").FullName;
    private readonly ListLogger _log = new();

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void A_key_posted_after_Alt_F4_ExitNow_does_not_reach_the_session_AC_06()
    {
        var current = Path.Combine(_folder, "a.mkv");
        File.WriteAllBytes(current, []);
        File.WriteAllBytes(Path.Combine(_folder, "b.mkv"), []);
        using var rig = new SessionRig(TwoDevices, null, 600);
        rig.Decoder.Script("b.mkv", 1); // no track 2: were the key to run, the folder is scanned and the file skipped
        var probed = 0;
        rig.Decoder.BeforeList = () => Interlocked.Increment(ref probed);
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = current })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        var controls = new PlaybackControls(rig.Session, _log, rig.Engine.Time);
        var exited = 0;
        controls.Exited += () => exited++;

        rig.Engine.Call(() =>
        {
            controls.ExitNow();
            return true;
        });
        controls.NextFile();
        controls.Dispose(); // drains the queue: whatever was posted has run by now

        exited.ShouldBe(1);
        probed.ShouldBe(0, "a key posted after the exit scanned the folder");
        _log.Records.ShouldBeEmpty("a key posted after the exit reached the session and failed there instead of being dropped");
    }

    private sealed class ListLogger : ILogger<PlaybackControls>
    {
        private readonly Lock _gate = new();
        private readonly List<string> _records = [];

        public IReadOnlyList<string> Records
        {
            get
            {
                lock (_gate)
                {
                    return [.. _records];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_gate)
            {
                _records.Add(formatter(state, exception));
            }
        }
    }
}

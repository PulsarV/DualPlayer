using System.Collections.Concurrent;
using DualPlayer.Console.Commands;
using DualPlayer.Core;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;
using DualPlayer.Media.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DualPlayer.Console.Tests.E2E;

/// <summary>
/// The console over a real <see cref="AudioEngine"/> and a real <see cref="FfmpegAudioDecoder"/> on the test media, with the
/// audio backend replaced by <see cref="FakeAudioBackend"/> and time by a <see cref="FakeTimeProvider"/>. The command runs on
/// its own thread reading scripted standard input; the test thread plays the part of time: it advances fake time in 10 ms
/// steps and feeds the fake devices until the wanted console output has appeared.
/// </summary>
internal sealed class ConsoleRig : IDisposable
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(10);

    private readonly List<FakeAudioDevice> _devices = [];
    private readonly ScriptedInput _input = new();
    private Task<int>? _run;

    public ConsoleRig(int deviceCount = 3)
    {
        Time = new FakeTimeProvider();
        Backend = new FakeAudioBackend(Time, new MixFormat(48_000, 2, ChannelLayout.Stereo));
        for (var i = 1; i <= deviceCount; i++)
        {
            Backend.AddDevice(new AudioDeviceInfo($"Test Device {i}", i == 1 ? "bluetooth" : null, $"dev-{i}"));
        }

        Decoder = new FfmpegAudioDecoder();
        Engine = new AudioEngine(new CapturingBackend(Backend, _devices), Decoder, Time, NullLogger<AudioEngine>.Instance);
    }

    public FakeTimeProvider Time { get; }

    public FakeAudioBackend Backend { get; }

    public FfmpegAudioDecoder Decoder { get; }

    public AudioEngine Engine { get; }

    public LockedWriter Out { get; } = new();

    public LockedWriter Err { get; } = new();

    public IReadOnlyList<FakeAudioDevice> Devices
    {
        get
        {
            lock (_devices)
            {
                return [.. _devices];
            }
        }
    }

    public static string Media(string name) => Path.Combine(AppContext.BaseDirectory, "TestMedia", name);

    public static IReadOnlyList<string> Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public IReadOnlyList<string> OutLines => Lines(Out.Snapshot());

    public IReadOnlyList<string> ErrLines => Lines(Err.Snapshot());

    /// <summary>Parses the arguments as the console's entry point does and runs the command; it reads the scripted input.</summary>
    public void StartPlay(params string[] args)
    {
        var parsed = CommandLine.Parse(args);
        parsed.IsSuccess.ShouldBeTrue(parsed.IsSuccess ? null : parsed.Error.Message);
        var request = parsed.Value.ShouldBeOfType<PlayRequest>();
        _run = Task.Factory.StartNew(
            () => new PlayCommand(Engine, Time).Run(request, Out, Err, _input),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public void Send(string line) => _input.Add(line);

    public void CloseInput() => _input.Complete();

    /// <summary>Moves fake time on until <paramref name="condition"/> holds on the console output.</summary>
    public void PumpUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, $"timed out waiting for {what}; stdout:\n{Out.Snapshot()}stderr:\n{Err.Snapshot()}");
            Advance();
            Thread.Sleep(1);
        }
    }

    public void PumpUntilLine(string prefix) =>
        PumpUntil(() => OutLines.Any(l => l.StartsWith(prefix, StringComparison.Ordinal)), $"a line starting '{prefix}'");

    /// <summary>Sends a command line and moves time on until the console has printed a line starting with <paramref name="prefix"/>.</summary>
    public string SendAndWaitFor(string line, string prefix)
    {
        var before = OutLines.Count(l => l.StartsWith(prefix, StringComparison.Ordinal));
        Send(line);
        PumpUntil(() => OutLines.Count(l => l.StartsWith(prefix, StringComparison.Ordinal)) > before, $"a new line starting '{prefix}'");
        return OutLines.Last(l => l.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>Moves time on until the command has returned; returns its exit code.</summary>
    public int Finish()
    {
        _run.ShouldNotBeNull();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!_run.IsCompleted)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, $"the command never returned; stdout:\n{Out.Snapshot()}stderr:\n{Err.Snapshot()}");
            Advance();
            Thread.Sleep(1);
        }

        return _run.GetAwaiter().GetResult();
    }

    /// <summary>Runs a command that does not start a run (refused plays and listings), moving time on until it returns.</summary>
    public int Play(params string[] args)
    {
        StartPlay(args);
        CloseInput();
        return Finish();
    }

    public void Dispose()
    {
        _input.Complete();
        Engine.Dispose();
    }

    private void Advance()
    {
        Time.Advance(Step);
        foreach (var device in Devices)
        {
            var chunk = device.MixFormat.SampleRate / 100;
            while (device.WrittenFrames < device.PlayedFramesNow + (2 * chunk))
            {
                // A closed, lost or hung device renders nothing; the engine may close it between two pumps.
                var before = device.WrittenFrames;
                device.PumpRender(chunk);
                if (device.WrittenFrames == before)
                {
                    break;
                }
            }
        }
    }

    internal sealed class LockedWriter : StringWriter
    {
        private readonly Lock _gate = new();

        public string Snapshot()
        {
            lock (_gate)
            {
                return ToString();
            }
        }

        public override void Write(char value)
        {
            lock (_gate)
            {
                base.Write(value);
            }
        }

        public override void Write(string? value)
        {
            lock (_gate)
            {
                base.Write(value);
            }
        }

        public override void Write(char[] buffer, int index, int count)
        {
            lock (_gate)
            {
                base.Write(buffer, index, count);
            }
        }

        public override void WriteLine(string? value)
        {
            lock (_gate)
            {
                base.WriteLine(value);
            }
        }
    }

    private sealed class ScriptedInput : TextReader
    {
        private readonly BlockingCollection<string> _lines = [];

        public void Add(string line) => _lines.Add(line);

        public void Complete() => _lines.CompleteAdding();

        public override string? ReadLine() => _lines.TryTake(out var line, Timeout.Infinite) ? line : null;
    }

    private sealed class CapturingBackend(IAudioBackend inner, List<FakeAudioDevice> devices) : IAudioBackend
    {
        public IReadOnlyList<AudioDeviceInfo> ListDevices() => inner.ListDevices();

        public Result<IAudioDevice> Open(string deviceId, TimeSpan requestedBuffer, IRenderSource source)
        {
            var result = inner.Open(deviceId, requestedBuffer, source);
            if (result.IsSuccess)
            {
                lock (devices)
                {
                    devices.Add((FakeAudioDevice)result.Value);
                }
            }

            return result;
        }
    }
}

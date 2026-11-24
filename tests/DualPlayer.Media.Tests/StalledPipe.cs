using System.Diagnostics;
using System.IO.Pipes;
using DualPlayer.Media.Audio;

namespace DualPlayer.Media.Tests;

/// <summary>
/// A named pipe that hands out a real Matroska file one byte every 20 ms, so an open or a probe through FFmpeg's file
/// protocol (the path the production adapters take) keeps reading and never finishes on its own: a stand-in for a network
/// share or a dying disk that makes FFmpeg hang. Every read returns, so FFmpeg reaches an I/O boundary every 20 ms and its
/// interrupt callback gets the chance to stop it. A POSIX FIFO on macOS and Linux, a <c>\\.\pipe\</c> named pipe on Windows.
/// <see cref="ReaderClosed"/> is set when a reader that took bytes has let go of its end (the writer's next write fails),
/// which is how a test sees that the abandoned worker really stopped and closed the file.
/// </summary>
internal sealed class StalledPipe : IDisposable
{
    private const string Source = "two-tracks.mkv";

    // A reader that let go before taking this many bytes was not FFmpeg: on Windows File.Exists opens the pipe and closes it.
    private const int ReaderMinimumBytes = 3;

    // Listening Windows pipe instances: one may be taken by the existence check, the rest stay free for the real open.
    private const int WindowsInstances = 4;

    private static readonly TimeSpan Trickle = TimeSpan.FromMilliseconds(20);

    private readonly byte[] _content = File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "TestMedia", Source));
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Thread> _writers = [];
    private readonly string? _folder;

    private StalledPipe()
    {
        if (OperatingSystem.IsWindows())
        {
            var name = $"dualplayer-stalled-{Guid.NewGuid():N}.mkv";
            Path = $@"\\.\pipe\{name}";
            for (var i = 0; i < WindowsInstances; i++)
            {
                var server = new NamedPipeServerStream(name, PipeDirection.Out, WindowsInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                Start(() => ServeWindows(server));
            }
        }
        else
        {
            _folder = Directory.CreateTempSubdirectory("dualplayer-pipe-").FullName;
            Path = System.IO.Path.Combine(_folder, "stalled.mkv");
            using var mkfifo = Process.Start(new ProcessStartInfo("mkfifo", [Path]) { UseShellExecute = false })!;
            mkfifo.WaitForExit();
            mkfifo.ExitCode.ShouldBe(0, "mkfifo failed");
            Start(ServePosix);
        }
    }

    public string Path { get; }

    public ManualResetEventSlim ReaderClosed { get; } = new();

    /// <summary>Makes the pipe, or skips the running test where the FFmpeg libraries are missing.</summary>
    public static StalledPipe CreateOrSkip()
    {
        var loaded = FfmpegLibrary.Load();
        Assert.SkipUnless(loaded.IsSuccess, $"The FFmpeg libraries are not available: {loaded.Error?.Message}");
        return new StalledPipe();
    }

    public void Dispose()
    {
        _stop.Cancel();
        foreach (var writer in _writers)
        {
            writer.Join(TimeSpan.FromSeconds(5));
        }

        if (_folder is null)
        {
            return;
        }

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void Start(ThreadStart serve)
    {
        var writer = new Thread(serve) { IsBackground = true };
        _writers.Add(writer);
        writer.Start();
    }

    private void ServePosix()
    {
        try
        {
            // Blocks until the reader opens its end; a reader that never does leaves the thread to the test's disposal.
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 1);
            TrickleTo(stream);
        }
        catch (IOException)
        {
        }
    }

    private void ServeWindows(NamedPipeServerStream server)
    {
        using (server)
        {
            try
            {
                server.WaitForConnectionAsync(_stop.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return;
            }

            TrickleTo(server);
        }
    }

    private void TrickleTo(Stream stream)
    {
        var delivered = 0;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                stream.WriteByte(delivered < _content.Length ? _content[delivered] : (byte)0);
                stream.Flush();
                delivered++;
                _stop.Token.WaitHandle.WaitOne(Trickle);
            }
        }
        catch (IOException)
        {
            if (delivered >= ReaderMinimumBytes)
            {
                ReaderClosed.Set();
            }
        }
    }
}

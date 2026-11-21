using System.Diagnostics;
using DualPlayer.Media.Audio;

namespace DualPlayer.Media.Tests;

/// <summary>
/// A named pipe (POSIX FIFO) that delivers one byte at a time and so never lets an open or a probe finish: a stand-in for a
/// network share or a dying disk that makes FFmpeg hang. <see cref="ReaderClosed"/> is set when the reader has let go of its
/// end (the writer's next write fails), which is how a test sees that the abandoned worker really stopped and closed the file.
/// </summary>
internal sealed class StalledPipe : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("dualplayer-pipe-").FullName;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _writer;

    private StalledPipe()
    {
        Path = System.IO.Path.Combine(_folder, "stalled.mkv");
        using var mkfifo = Process.Start(new ProcessStartInfo("mkfifo", [Path]) { UseShellExecute = false })!;
        mkfifo.WaitForExit();
        mkfifo.ExitCode.ShouldBe(0, "mkfifo failed");
        _writer = new Thread(Write) { IsBackground = true };
        _writer.Start();
    }

    public string Path { get; }

    public ManualResetEventSlim ReaderClosed { get; } = new();

    /// <summary>Makes the pipe, or skips the running test where there is no <c>mkfifo</c> (Windows) or no FFmpeg library.</summary>
    public static StalledPipe CreateOrSkip()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A POSIX FIFO is needed to stall the read; Windows has none.");
        var loaded = FfmpegLibrary.Load();
        Assert.SkipUnless(loaded.IsSuccess, $"The FFmpeg libraries are not available: {loaded.Error?.Message}");
        return new StalledPipe();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _writer.Join(TimeSpan.FromSeconds(5));
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void Write()
    {
        try
        {
            // Blocks until the reader opens its end; a reader that never does leaves the thread to the test's disposal.
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 1);
            while (!_stop.IsCancellationRequested)
            {
                stream.WriteByte(0);
                stream.Flush();
                _stop.Token.WaitHandle.WaitOne(20);
            }
        }
        catch (IOException)
        {
            ReaderClosed.Set();
        }
    }
}

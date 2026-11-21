using System.Diagnostics;
using DualPlayer.Media.Audio;

namespace DualPlayer.Media.Tests.Generated;

/// <summary>
/// A throwaway folder of sample files made at test time with the FFmpeg command line (the samples are a few seconds long,
/// so nothing is committed). Creating it skips the test, with the reason, when the <c>ffmpeg</c> program (found through
/// <c>DUALPLAYER_FFMPEG</c> or the PATH) or the FFmpeg shared libraries are not there. Each test creates and deletes its own.
/// </summary>
internal sealed class GeneratedMedia : IDisposable
{
    public const string FfmpegVariable = "DUALPLAYER_FFMPEG";

    private GeneratedMedia(string ffmpeg) => Ffmpeg = ffmpeg;

    public string Ffmpeg { get; }

    public string Folder { get; } = Directory.CreateTempSubdirectory("dualplayer-generated-").FullName;

    /// <summary>Makes the folder, or skips the running test when FFmpeg is unavailable.</summary>
    public static GeneratedMedia CreateOrSkip()
    {
        var ffmpeg = FindFfmpeg();
        Assert.SkipUnless(ffmpeg is not null, $"The ffmpeg program was not found on the PATH (or in {FfmpegVariable}); samples cannot be generated.");

        var loaded = FfmpegLibrary.Load();
        Assert.SkipUnless(loaded.IsSuccess, $"The FFmpeg libraries are not available: {loaded.Error?.Message}");
        return new GeneratedMedia(ffmpeg!);
    }

    public string PathOf(string name) => Path.Combine(Folder, name);

    /// <summary>Runs FFmpeg with <paramref name="arguments"/> (everything before the output) and writes <paramref name="name"/>; fails the test when FFmpeg fails.</summary>
    public string Make(string name, params string[] arguments)
    {
        var output = PathOf(name);
        Run([.. arguments, output]);
        return output;
    }

    /// <summary>Runs FFmpeg and returns what it wrote to standard output; fails the test when FFmpeg fails.</summary>
    public string Run(params string[] arguments)
    {
        var start = new ProcessStartInfo(Ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit(TimeSpan.FromSeconds(60)).ShouldBeTrue("ffmpeg did not finish within 60 s");
        process.ExitCode.ShouldBe(0, $"ffmpeg failed: {stderr.Result}");
        return stdout.Result;
    }

    /// <summary>A 640x360 gray x264 picture with the given number of 440 Hz FLAC audio tracks (1 second).</summary>
    public string MakeFilm(string name, int audioTracks)
    {
        var arguments = new List<string> { "-f", "lavfi", "-i", "color=c=gray:s=640x360:r=25:d=1" };
        for (var i = 0; i < audioTracks; i++)
        {
            arguments.AddRange(["-f", "lavfi", "-i", "sine=f=440:d=1:r=48000"]);
        }

        arguments.AddRange(["-map", "0:v"]);
        for (var i = 0; i < audioTracks; i++)
        {
            arguments.AddRange(["-map", $"{i + 1}:a"]);
        }

        arguments.AddRange(["-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p", "-c:a", "flac"]);
        return Make(name, [.. arguments]);
    }

    /// <summary>The given number of 1 second FLAC audio tracks and no picture, in a Matroska file.</summary>
    public string MakeAudioOnly(string name, int audioTracks)
    {
        var arguments = new List<string>();
        for (var i = 0; i < audioTracks; i++)
        {
            arguments.AddRange(["-f", "lavfi", "-i", "sine=f=440:d=1:r=48000"]);
        }

        for (var i = 0; i < audioTracks; i++)
        {
            arguments.AddRange(["-map", $"{i}:a"]);
        }

        arguments.AddRange(["-c:a", "flac"]);
        return Make(name, [.. arguments]);
    }

    public void Dispose() => Directory.Delete(Folder, recursive: true);

    private static string? FindFfmpeg()
    {
        var named = Environment.GetEnvironmentVariable(FfmpegVariable);
        if (!string.IsNullOrWhiteSpace(named))
        {
            return File.Exists(named) ? named : null;
        }

        var file = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return directories.Select(d => Path.Combine(d, file)).FirstOrDefault(File.Exists);
    }
}

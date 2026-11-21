using DualPlayer.Core;
using FFmpeg.AutoGen;

namespace DualPlayer.Media;

/// <summary>Maps FFmpeg open failures to the domain's error codes, shared by the audio decoder and the picture probe.</summary>
internal static class FfmpegErrors
{
    internal const string AccessDenied = "playback.file_access_denied";
    internal const string Unreadable = "playback.file_unreadable";
    private const int PosixPermissionDenied = 1; // EPERM
    private const int PosixAccessDenied = 13; // EACCES; with EPERM both mean the OS refused the read

    internal static Error OpenFailure(string path, int rc, string? detail = null) =>
        rc == ffmpeg.AVERROR(PosixAccessDenied) || rc == ffmpeg.AVERROR(PosixPermissionDenied)
            ? new Error(AccessDenied, $"The operating system denied the player access to '{path}'.")
            : new Error(Unreadable, $"The file '{path}' cannot be opened as media{(detail is null ? string.Empty : ": " + detail)}.");
}

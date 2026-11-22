namespace DualPlayer.Core.Playback;

/// <summary>The picture of a file that this PC can decode in hardware.</summary>
public sealed record PictureInfo(string Codec, string Profile, int Width, int Height);

/// <summary>Decides, without a window, whether a file has a picture this PC decodes in hardware.</summary>
public interface IPictureProbe
{
    /// <summary>
    /// Error codes: <c>playback.file_access_denied</c>, <c>playback.file_unreadable</c>, <c>video.no_picture</c>,
    /// <c>video.not_hardware_decodable</c> (a picture was found or decoded, but not in hardware), <c>video.probe_timeout</c>
    /// (the first picture was not decoded within <paramref name="timeLimit"/>; <c>codec</c>/<c>profile</c> in the fields when known).
    /// </summary>
    Result<PictureInfo> Probe(string path, TimeSpan timeLimit);

    /// <summary>
    /// As <see cref="Probe(string, TimeSpan)"/>, but a cancelled <paramref name="cancel"/> stops the probe promptly and closes
    /// the file; the caller has given up on it. A probe that cannot be interrupted keeps the default, which ignores the token.
    /// </summary>
    Result<PictureInfo> Probe(string path, TimeSpan timeLimit, CancellationToken cancel) => Probe(path, timeLimit);
}

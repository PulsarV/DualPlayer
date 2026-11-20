namespace DualPlayer.Core.Playback;

/// <summary>The picture of a file that this PC can decode in hardware.</summary>
public sealed record PictureInfo(string Codec, string Profile, int Width, int Height);

/// <summary>Decides, without a window, whether a file has a picture this PC decodes in hardware.</summary>
public interface IPictureProbe
{
    /// <summary>
    /// Error codes: <c>playback.file_access_denied</c>, <c>playback.file_unreadable</c>, <c>video.no_picture</c>,
    /// <c>video.not_hardware_decodable</c> (also when the first picture is not decoded within <paramref name="timeLimit"/>).
    /// </summary>
    Result<PictureInfo> Probe(string path, TimeSpan timeLimit);
}

using DualPlayer.Core.Playback;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>Scripted probe: each file has its own outcome; unscripted files probe as a 1920x1080 h264 picture.</summary>
internal sealed class FakePictureProbe : IPictureProbe
{
    private readonly Dictionary<string, Result<PictureInfo>> _outcomes = new(StringComparer.Ordinal);

    public List<string> Probed { get; } = [];

    public void Script(string path, Result<PictureInfo> outcome) => _outcomes[path] = outcome;

    public void Fail(string path, string code, string message) => Script(path, Result<PictureInfo>.Failure(code, message));

    public Result<PictureInfo> Probe(string path, TimeSpan timeLimit)
    {
        Probed.Add(path);
        return _outcomes.TryGetValue(path, out var outcome)
            ? outcome
            : Result<PictureInfo>.Success(new PictureInfo("h264", "High", 1920, 1080));
    }
}

namespace DualPlayer.Core.Outputs;

/// <summary>An open shared-mode playback stream.</summary>
public interface IAudioDevice
{
    MixFormat MixFormat { get; }

    TimeSpan GrantedBuffer { get; }

    /// <summary>Raised once, off the render thread, when the device disappears.</summary>
    event EventHandler? DeviceLost;

    void Start();

    void Close();
}

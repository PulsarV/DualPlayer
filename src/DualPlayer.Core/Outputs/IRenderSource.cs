namespace DualPlayer.Core.Outputs;

/// <summary>Core-provided source the backend pulls from on its real-time thread. Must not allocate, lock, block or throw.</summary>
public interface IRenderSource
{
    void Render(Span<float> buffer, DevicePosition position);
}

using DualPlayer.Core.Settings;
using Microsoft.Extensions.Logging;

namespace DualPlayer.Core.Sync;

/// <summary>Outcome of one offset key press.</summary>
public readonly record struct OffsetStepResult(int OffsetMs, bool AtLimit);

/// <summary>Per-device offsets: ±500 ms clamp, 10 ms step, save at most 1 s after the last press.</summary>
public sealed class OffsetBook : IDisposable
{
    public const int LimitMs = 500;
    public const int StepMs = 10;

    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private readonly IStateStore _store;
    private readonly TimeProvider _time;
    private readonly ILogger<OffsetBook> _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _offsets = [];
    private readonly ITimer _saveTimer;
    private bool _dirty;

    public OffsetBook(IStateStore store, TimeProvider time, ILogger<OffsetBook> logger)
    {
        _store = store;
        _time = time;
        _logger = logger;
        _saveTimer = time.CreateTimer(_ => Save(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Raised when a debounced save has written the file; carries when (the book's clock).</summary>
    public event Action<DateTimeOffset>? Saved;

    /// <summary>Raised when a debounced save fails; the in-memory offset is kept.</summary>
    public event Action<Error>? SaveFailed;

    /// <summary>Loads the file once; returns warnings (<c>offsets.clamped</c>, <c>state.*</c>).</summary>
    public IReadOnlyList<Error> Load()
    {
        var warnings = new List<Error>();
        var result = _store.Load(DeviceOffsetsFile.Definition);

        lock (_gate)
        {
            _offsets.Clear();

            if (!result.IsSuccess)
            {
                warnings.Add(result.Error);
                _logger.LogWarning("Remembered offsets unreadable ({Code}): {Message}; starting at 0", result.Error.Code, result.Error.Message);
                return warnings;
            }

            // System.Text.Json lets a hand-edited "offsets": null replace the initializer.
            if (result.Value.Offsets is null)
            {
                const string message = "Remembered offsets file has no offsets object; starting at 0.";
                warnings.Add(new Error("state.invalid", message));
                _logger.LogWarning("{Message}", message);
                return warnings;
            }

            foreach (var (deviceId, stored) in result.Value.Offsets)
            {
                var corrected = Normalize(stored);
                _offsets[deviceId] = corrected;

                if (corrected != stored)
                {
                    var message = $"Offset {stored} ms for device '{deviceId}' corrected to {corrected} ms.";
                    warnings.Add(new Error("offsets.clamped", message));
                    _logger.LogWarning("{Message}", message);
                }
            }
        }

        return warnings;
    }

    public int Get(string deviceId)
    {
        lock (_gate)
        {
            return _offsets.GetValueOrDefault(deviceId);
        }
    }

    public OffsetStepResult Step(string deviceId, int deltaMs)
    {
        lock (_gate)
        {
            var current = _offsets.GetValueOrDefault(deviceId);
            var target = Math.Clamp(current + deltaMs, -LimitMs, LimitMs);

            if (target == current)
            {
                return new OffsetStepResult(current, AtLimit: true);
            }

            _offsets[deviceId] = target;
            _dirty = true;
            _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
            return new OffsetStepResult(target, AtLimit: false);
        }
    }

    /// <summary>Stops the timer and writes a press that is still waiting for its debounced save.</summary>
    public void Dispose()
    {
        _saveTimer.Dispose();
        Save();
    }

    private static int Normalize(int value)
    {
        var rounded = (int)Math.Round(value / (double)StepMs, MidpointRounding.AwayFromZero) * StepMs;
        return Math.Clamp(rounded, -LimitMs, LimitMs);
    }

    private void Save()
    {
        DeviceOffsets snapshot;
        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            _dirty = false;
            snapshot = new DeviceOffsets { Offsets = new Dictionary<string, int>(_offsets) };
        }

        var result = _store.Save(DeviceOffsetsFile.Definition, snapshot);
        if (!result.IsSuccess)
        {
            _logger.LogWarning("Saving offsets failed ({Code}): {Message}", result.Error.Code, result.Error.Message);
            SaveFailed?.Invoke(result.Error);
            return;
        }

        Saved?.Invoke(_time.GetUtcNow());
    }
}

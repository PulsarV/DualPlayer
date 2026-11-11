namespace DualPlayer.Core.Settings;

/// <summary>Port for persisted app state (settings, offsets, positions, playlist) — ADR-0004.</summary>
public interface IStateStore
{
    /// <summary>
    /// Loads a state file, lifting older <c>schemaVersion</c>s through the definition's upgraders.
    /// A missing file yields the definition's default value.
    /// </summary>
    Result<T> Load<T>(StateFileDefinition<T> definition) where T : class;

    /// <summary>Saves a state file atomically (temp file + rename) stamped with the current schema version.</summary>
    Result Save<T>(StateFileDefinition<T> definition, T value) where T : class;
}

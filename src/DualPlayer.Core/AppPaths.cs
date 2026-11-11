namespace DualPlayer.Core;

/// <summary>Per-user folders: state files and logs (ADR-0004). The OS-specific root is chosen by each composition root.</summary>
public sealed record AppPaths(string StateDirectory, string LogDirectory)
{
    public const string AppFolderName = "DualPlayer";

    public static AppPaths FromRoot(string appDirectory) =>
        new(appDirectory, Path.Combine(appDirectory, "logs"));
}

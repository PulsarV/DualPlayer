namespace DualPlayer.App;

/// <summary>Per-user folders: state files and logs (ADR-0004).</summary>
public sealed record AppPaths(string StateDirectory, string LogDirectory)
{
    public const string AppFolderName = "DualPlayer";

    /// <summary>Windows: <c>%APPDATA%/DualPlayer</c>; macOS: <c>~/Library/Application Support/DualPlayer</c>.</summary>
    public static AppPaths ForCurrentUser()
    {
        var root = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return FromRoot(Path.Combine(root, AppFolderName));
    }

    public static AppPaths FromRoot(string appDirectory) =>
        new(appDirectory, Path.Combine(appDirectory, "logs"));
}

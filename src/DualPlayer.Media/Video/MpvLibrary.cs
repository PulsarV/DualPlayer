using System.Reflection;
using System.Runtime.InteropServices;
using DualPlayer.Core;

namespace DualPlayer.Media.Video;

/// <summary>The loaded libmpv: where it came from and its client API version (major in the high 16 bits).</summary>
public sealed record MpvLibraryInfo(string Path, uint ClientApiVersion);

/// <summary>Locates and loads libmpv (libmpv-2.dll beside the exe, Homebrew libmpv on macOS) and checks its client API.</summary>
public static class MpvLibrary
{
    /// <summary>The oldest libmpv client API version the renderer works with (2.0).</summary>
    public const uint RequiredClientApiVersion = 2u << 16;

    private static readonly Lock Gate = new();
    private static nint _loadedHandle;
    private static string? _loadedPath;
    private static MpvLibraryInfo? _loadedInfo;

    /// <summary>
    /// Loads libmpv from <paramref name="libraryPath"/>, or from the default location when null. Must succeed before any
    /// <see cref="MpvNative"/> call. Idempotent: once a library has loaded, a repeat call for the same (or the default)
    /// location with a satisfied version requirement returns the cached info.
    /// </summary>
    public static Result<MpvLibraryInfo> Load(string? libraryPath = null, uint requiredClientApiVersion = RequiredClientApiVersion)
    {
        lock (Gate)
        {
            if (_loadedInfo is { } cached
                && (libraryPath is null || libraryPath == cached.Path)
                && cached.ClientApiVersion >= requiredClientApiVersion)
            {
                return Result<MpvLibraryInfo>.Success(cached);
            }
        }

        string path;
        if (libraryPath is not null)
        {
            if (!File.Exists(libraryPath))
            {
                return Result<MpvLibraryInfo>.Failure("mpv.not_found", $"libmpv was not found at '{libraryPath}'.");
            }

            path = libraryPath;
        }
        else
        {
            var candidates = DefaultCandidates();
            var found = candidates.FirstOrDefault(File.Exists);
            if (found is null)
            {
                return Result<MpvLibraryInfo>.Failure(
                    "mpv.not_found",
                    $"libmpv was not found; looked in: {string.Join(", ", candidates)}. Install it (brew install mpv / build/fetch-libmpv.ps1).");
            }

            path = found;
        }

        if (!NativeLibrary.TryLoad(path, out var handle))
        {
            return Result<MpvLibraryInfo>.Failure("mpv.load_failed", $"'{path}' could not be loaded as a native library.");
        }

        if (!NativeLibrary.TryGetExport(handle, "mpv_client_api_version", out var export))
        {
            NativeLibrary.Free(handle);
            return Result<MpvLibraryInfo>.Failure("mpv.load_failed", $"'{path}' does not export mpv_client_api_version; it is not libmpv.");
        }

        uint foundVersion;
        unsafe
        {
            foundVersion = ((delegate* unmanaged<uint>)export)();
        }

        var checkedResult = CheckVersion(path, foundVersion, requiredClientApiVersion);
        if (!checkedResult.IsSuccess)
        {
            NativeLibrary.Free(handle);
            return checkedResult;
        }

        Publish(handle, path, checkedResult.Value);
        return checkedResult;
    }

    /// <summary>Checks a found client API version against the required one.</summary>
    internal static Result<MpvLibraryInfo> CheckVersion(string path, uint foundClientApiVersion, uint requiredClientApiVersion) =>
        foundClientApiVersion < requiredClientApiVersion
            ? Result<MpvLibraryInfo>.Failure(
                "mpv.load_failed",
                $"libmpv at '{path}' has client API {FormatVersion(foundClientApiVersion)}; {FormatVersion(requiredClientApiVersion)} or newer is required.")
            : Result<MpvLibraryInfo>.Success(new MpvLibraryInfo(path, foundClientApiVersion));

    internal static string FormatVersion(uint version) => $"{version >> 16}.{version & 0xFFFF}";

    private static string[] DefaultCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            return [Path.Combine(AppContext.BaseDirectory, "libmpv-2.dll")];
        }

        if (OperatingSystem.IsMacOS())
        {
            return ["/opt/homebrew/lib/libmpv.dylib", "/usr/local/lib/libmpv.dylib"];
        }

        return ["/usr/lib/libmpv.so.2", "/usr/lib/x86_64-linux-gnu/libmpv.so.2", "/usr/lib/libmpv.so"];
    }

    // Binds the "mpv" import name of MpvNative to the library that passed the version check.
    private static void Publish(nint handle, string path, MpvLibraryInfo info)
    {
        lock (Gate)
        {
            if (_loadedHandle == 0)
            {
                NativeLibrary.SetDllImportResolver(typeof(MpvLibrary).Assembly, Resolve);
            }

            _loadedHandle = handle;
            _loadedPath = path;
            _loadedInfo = info;
        }
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        lock (Gate)
        {
            return libraryName == MpvNative.LibraryName ? _loadedHandle : 0;
        }
    }

    /// <summary>The path of the library bound to <see cref="MpvNative"/>, or null before a successful <see cref="Load"/>.</summary>
    internal static string? LoadedPath
    {
        get
        {
            lock (Gate)
            {
                return _loadedPath;
            }
        }
    }
}

/// <summary>The minimal libmpv client API surface the renderer needs. Valid only after <see cref="MpvLibrary.Load"/> succeeded.</summary>
internal static partial class MpvNative
{
    internal const string LibraryName = "mpv";

    [LibraryImport(LibraryName, EntryPoint = "mpv_create")]
    internal static partial nint Create();

    [LibraryImport(LibraryName, EntryPoint = "mpv_initialize")]
    internal static partial int Initialize(nint ctx);

    [LibraryImport(LibraryName, EntryPoint = "mpv_set_option_string", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SetOptionString(nint ctx, string name, string data);

    [LibraryImport(LibraryName, EntryPoint = "mpv_set_property_string", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SetPropertyString(nint ctx, string name, string data);

    /// <summary><paramref name="args"/> points to a null-terminated array of UTF-8 C strings.</summary>
    [LibraryImport(LibraryName, EntryPoint = "mpv_command")]
    internal static partial int Command(nint ctx, nint args);

    /// <summary>Queues the command and returns at once; mpv copies <paramref name="args"/>. The reply arrives as an event.</summary>
    [LibraryImport(LibraryName, EntryPoint = "mpv_command_async")]
    internal static partial int CommandAsync(nint ctx, ulong replyUserdata, nint args);

    [LibraryImport(LibraryName, EntryPoint = "mpv_observe_property", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int ObserveProperty(nint ctx, ulong replyUserdata, string name, int format);

    /// <summary>Returns a pointer to an mpv_event owned by libmpv, valid until the next call.</summary>
    [LibraryImport(LibraryName, EntryPoint = "mpv_wait_event")]
    internal static partial nint WaitEvent(nint ctx, double timeoutSeconds);

    [LibraryImport(LibraryName, EntryPoint = "mpv_request_log_messages", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int RequestLogMessages(nint ctx, string minLevel);

    [LibraryImport(LibraryName, EntryPoint = "mpv_wakeup")]
    internal static partial void Wakeup(nint ctx);

    [LibraryImport(LibraryName, EntryPoint = "mpv_terminate_destroy")]
    internal static partial void TerminateDestroy(nint ctx);

    [LibraryImport(LibraryName, EntryPoint = "mpv_error_string")]
    internal static partial nint ErrorString(int error);
}

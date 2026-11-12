# DualPlayer

Video player for two people watching one video, each hearing their own audio track on their own
audio device, lip-synced with the picture. Target: Windows 11 x64. Development: macOS.

Sources of truth: `docs/idea-brief.md` (the idea), `docs/roadmap.md` (steps and order),
`docs/architecture-map.md` (foundation), `docs/adr/` (decisions), `docs/features/<slug>/` (per-feature specs).

## Commands

```sh
dotnet build DualPlayer.slnx                       # build (warnings are errors)
dotnet test DualPlayer.slnx                        # all tests
dotnet format DualPlayer.slnx --verify-no-changes  # lint; drop --verify-no-changes to fix
dotnet run --project src/DualPlayer.App            # run the app
```

The SDK is pinned by `global.json` (.NET 10, latest feature band). If `dotnet` is not on PATH,
it lives in `~/.dotnet` (`export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`).
Hardware tests run only with `DUALPLAYER_HARDWARE=1`.

## Module boundaries (ADR-0003)

| Project | Holds | May reference |
|---|---|---|
| `src/DualPlayer.Core` | Domain + ports, by area: `Playback/`, `Sync/`, `Outputs/`, `Playlist/`, `Subtitles/`, `Settings/`, `Diagnostics/` | BCL + `Microsoft.Extensions.*.Abstractions` only — **no OS, UI or native library** |
| `src/DualPlayer.Media` | FFmpeg audio decoder, libmpv video renderer adapters | Core |
| `src/DualPlayer.Platform.Windows` | WASAPI render loop, device enumeration/hotplug, BT diagnostics; `[SupportedOSPlatform("windows")]` | Core |
| `src/DualPlayer.Platform.MacOS` | Dev-only AudioQueue backend — never shipped, not held to sync targets; `[SupportedOSPlatform("macos")]` | Core |
| `src/DualPlayer.App` | Avalonia app: `Views/`, `ViewModels/`, `Controls/`, `Themes/`; the composition root for the UI | everything |
| `src/DualPlayer.Console` | Developer console executable (no Avalonia): command loop, own Serilog setup; the second composition root (ADR-0001) | everything but App |

- Ports live in Core (`IAudioBackend`, `IAudioDecoder`, `IVideoRenderer`, `IDeviceWatcher`, `IStateStore`); adapters implement them.
- Where things go: new playback/audio capability → domain + port in `Core/<Area>/`, OS part as an adapter in `Platform.Windows/` (dev stub in `Platform.MacOS/` if needed); codec/media concern → `Media/`; screen/component → View + ViewModel in `App/`, composed from `Controls/` and `Themes/Tokens.axaml`.

## Conventions

- **Module wiring:** each module exposes `IServiceCollection Add<Module>(this IServiceCollection)`; only `src/DualPlayer.App/CompositionRoot.cs` and `src/DualPlayer.Console/ConsoleCompositionRoot.cs` (the only two composition roots) call them.
- **Platform selection:** runtime `OperatingSystem.IsWindows()` / `IsMacOS()` in the composition root. All projects target plain `net10.0` so the solution builds on macOS; the CA1416 analyzer enforces the guards.
- **Errors:** expected runtime failures (device lost, file unreadable, decoder error) return `Result` / `Result<T>` (`src/DualPlayer.Core/Result.cs`) or raise a domain event — never thrown across module boundaries. Exceptions mean bugs; log them at the top of each background loop.
- **Time and clocks:** no `DateTime.Now` / `Stopwatch` in Core. Time comes from an injected `TimeProvider` or the audio master clock, so sync logic is testable with fakes.
- **Threading:** audio render loops run on dedicated threads (MMCSS "Pro Audio" on Windows), never on the UI thread; UI updates marshal through `Dispatcher.UIThread`.
- **IDs:** audio devices keyed by the OS endpoint ID string; media files keyed by normalized absolute path + size + mtime.
- **Persistence (ADR-0004):** JSON files via System.Text.Json **source-generated** contexts in the per-user app-data folder (`%APPDATA%/DualPlayer`, macOS `~/Library/Application Support/DualPlayer`), through `IStateStore`. Writes are atomic (temp + rename).
- **Migrations:** no DB. Each JSON file carries `schemaVersion`; a new persisted field = a record change in `Core/Settings/` + bump the file's `CurrentVersion` + add an `IStateUpgrader` from the previous version.
- **Logging:** `ILogger<T>` everywhere; Serilog rolling file in the app-data `logs/` folder; sync/underrun/drift counters as structured events.
- **Inter-module communication:** direct calls through Core ports; Core raises domain events (C# events / `IObservable`) that ViewModels subscribe to.
- **UI:** Avalonia Fluent, dark variant; MVVM with CommunityToolkit.Mvvm (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`); compiled bindings (`x:DataType` on every view). Sizes and colours come from `Themes/Tokens.axaml` (1080p baselines: body ≥ 32 px, headings ≥ 48 px, min 24 px, buttons ≥ 96×96, focus border 4 px, contrast ≥ 7:1) — no inline styling in views.
- **Build settings:** `Directory.Build.props` (nullable, `TreatWarningsAsErrors`, `AnalysisLevel=latest`, code style enforced in build); package versions only in `Directory.Packages.props`.

## Tests

- xUnit v3 + Shouldly + NSubstitute; one test project per module under `tests/`; test names are sentences with underscores.
- Sync logic is tested with a fake clock + fake audio device.
- Tests needing real audio hardware use `[HardwareFact]` (`tests/Shared/HardwareFactAttribute.cs`) — skipped unless `DUALPLAYER_HARDWARE=1`.
- `tests/DualPlayer.App.Tests/CompositionRootSmokeTests.cs` is the skeleton smoke test: the DI container must build and resolve the main ViewModel.

## Constraints

- libmpv and FFmpeg are LGPL: dynamic linking only (`libmpv-2.dll` beside the exe); FFmpeg builds must be LGPL.
- Native libs on the dev Mac (libmpv, FFmpeg) are installed outside the repo (Homebrew); on Windows they ship in the package.
- CI: GitHub Actions, `windows-latest` + `macos-latest`, build + test + lint (`.github/workflows/ci.yml`).

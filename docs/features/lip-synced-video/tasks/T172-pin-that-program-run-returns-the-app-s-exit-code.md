---
id: T172
title: "Pin that Program.Run returns the app's exit code"
layer: "test"
deps: []
blocks: ["T23", "T173"]
acs: ["AC-02", "AC-03", "AC-03b"]
files_hint: ["tests/DualPlayer.App.Tests/Startup/ProgramRunTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "XS"
context_budget: "XS"
origin: "review-2026-11-24 (AD4)"
---

<!-- Follow-up task from review-2026-11-24. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-24.md) · repo `CLAUDE.md`. Do not guess. -->

# T172 — Pin that Program.Run returns the app's exit code

## Why

- **AD4.** `src/DualPlayer.App/Program.cs:63` returns `startApp`'s code, but the only test reaching `startApp` (`tests/DualPlayer.App.Tests/Startup/ProgramRunTests.cs:67-84`) stubs 0 and expects 0. The mutant `_ = startApp(...); return 0;` passes App 202/202, so every failure after the app starts (`desktop.Shutdown(StartExitCodes.For(...))`, App.axaml.cs:68) would exit 0. cli.md §4 (exit 2, 4).

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-02, AC-03, AC-03b.

## Definition of Done

- [x] A `ProgramRunTests` theory over exit codes 2 and 4: a valid command whose `startApp` returns the code makes `Run` return the same code. *(ProgramRunTests theory A_valid_command_returns_the_exit_code_the_app_ended_with_AC_02_AC_03_AC_03b has cases 2 and 4. startApp returns the code and Run returns the same code. Both cases pass.)*
- [x] Mutant on a scratch copy fails the new test: `_ = startApp(parsed.Value, loggerFactory); return 0;`. *(The mutant `_ = startApp(parsed.Value, loggerFactory); return 0;` was re-applied to Program.cs. Both new cases (appExitCode 2 and 4) failed. Reverted with git checkout -- src.)*
- [x] No production code changes. *(`git diff -- src` is empty. Only tests/DualPlayer.App.Tests/Startup/ProgramRunTests.cs is modified.)*
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean *(`dotnet build DualPlayer.slnx` gave 0 warnings and 0 errors. `dotnet test DualPlayer.slnx` exited 0 with 0 failures. `dotnet format DualPlayer.slnx --verify-no-changes` exited 0.)*

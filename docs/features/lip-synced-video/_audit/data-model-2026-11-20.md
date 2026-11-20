# Data-model audit — lip-synced-video — 2026-11-20

## Staged migrations

None. The project has no database and no live `migrations/` tree; persistence is versioned JSON through `IStateStore` (project ADR-0004). This feature's only schema change is a **new** state file, `device-offsets.json` at `schemaVersion` 1 (`sad.md` ADR-0006), created in code by `Core/Settings/DeviceOffsetsFile.cs`. No `.up.sql` / `.down.sql` applies; nothing awaits promotion by `implement`.

## Convention source and adherence

- Architecture: `CLAUDE.md` Persistence + Migrations, project ADR-0004, `sad.md` §5 / §8 and ADR-0006.
- Code corroboration: `src/DualPlayer.Core/Settings/AppSettingsFile.cs` (definition + upgrader chain), `StateFileDefinition.cs`, `JsonFileStateStore.cs` (atomic temp + rename, `state.*` error codes), `AppSettings.cs` (source-generated, camelCase, indented context).
- The new file follows every one of them. One deliberate difference: its upgrader list is empty, because unlike `settings.json` there was never an unversioned v0 file to upgrade.
- `docs/architecture-map.md` is stale (reflects `546fd39`); it records `migration_tool: ""`, which matches: there is none.

## Self-check (stack-agnostic)

| Check | Result |
|---|---|
| Naming follows the repo | ✓ kebab-case file name like `settings.json`; camelCase JSON properties per the existing context |
| Reversibility | ✓ n/a for SQL; deleting `device-offsets.json` returns to "no remembered offsets" with no effect on other state |
| FK indexes | ✓ n/a — no foreign keys |
| Convention adherence | ✓ see above; the empty upgrader list is the only deviation, explained |

## Drift

No drift check possible yet: no `DeviceOffsets` type exists in code. Drift is checked by `review` once `implement` adds it.

## Breaking changes

None — a new file; `settings.json` is untouched.

## TBD

None.

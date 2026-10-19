# Crest Loadouts

Mix five attack sources in Hollow Knight: Silksong while the equipped crest continues to control tool slots and binding. Supported slots are neutral, up, down, dash, and Needle Strike. Sources are Hunter, Reaper, Wanderer, Beast, Witch, Architect, and Shaman; alternate Hunter forms retain their equipped crest identity.

Requires BepInEx 5 and Save Scoped Config **0.4.0 or newer**. Configuration Manager is recommended.

## Attack editor

Select a crest in the inventory and press **C** or the mapped controller **Action3** button to open its attack editor. Both shortcuts can be rebound under `Controls`; `None` disables a controller shortcut. Use the editor's displayed controls to cycle attacks and reset the selected crest. Each save stores a separate five-slot loadout for each of the seven crest families; Hunter upgrades share the Hunter loadout. Default sources restore that crest's attacks.

`Require Bench To Change Attack` controls whether editing requires resting at a bench. `Allow Locked Crest Attacks` controls whether unavailable sources can be selected. These rules also apply when loading or copying presets. The equipped crest's normal tool-management rules still apply.

## Named presets

The Configuration Manager `Loadout Manager` panel saves the equipped crest's five attacks under a name, loads a preset onto the equipped crest, deletes presets, copies attacks from another crest, or resets the equipped crest to its default attacks. `Copy From Crest` chooses the source crest.

Presets belong to the active save, so loading a different save changes the available names. Names are case insensitive, support Unicode, and are limited to 80 characters and 32 presets per save. Saving an existing name replaces it. Presets containing locked or invalid sources are rejected before any attacks change. Saving a preset only records the current selection; applying one follows the bench rule.

Startup compatibility checks report missing APIs and roll back this mod's patches if patching fails. They cannot guarantee compatibility with every future game update.

## Build and package

Requires the .NET 8 SDK and a local Silksong installation with BepInEx 5. Run from this repository:

```sh
python3 build.py --game-dir "/path/to/Hollow Knight Silksong"
python3 build.py --game-dir "/path/to/Hollow Knight Silksong" --package
```

`SILKSONG_GAME_DIR` can supply the game directory. Use `--dotnet /path/to/dotnet` for a local SDK. Use `--save-scoped-dll /path/to/SaveScopedConfig.dll` when building against a newer helper than the installed copy.
Builds go to `bin/Release/netstandard2.1`. Packaging writes `dist/CrestLoadouts-<version>.zip` with the plugin, README, and SHA-256 checksum; it includes no game assemblies. `--check` validates version metadata without building. Identical inputs produce identical package contents and timestamps. Release metadata excludes Git revision suffixes so checked-in binaries do not identify an earlier source commit.

Builds do not deploy by default. Install the DLL under `BepInEx/plugins/CrestLoadouts/` after closing the game. On Windows, `build.ps1 -GameDir "C:\path\to\game"` also builds locally; explicit `-Target Plugin` deploys a plugin and `-Target Dev` deploys for ScriptEngine. Do not install both copies.

## Regression tests

`Tests/run.py` builds all five sibling mod projects, runs the helper's core tests, and starts the native Linux game with a separate BepInEx tree and disposable configuration/save directory under `/tmp`. It does not install the test plugin or touch normal saves. Close any running copy of the game first.

```sh
python3 Tests/run.py --game-dir "/path/to/Hollow Knight Silksong" --dotnet /path/to/dotnet
```

The default suite checks events, compatibility rollback, named presets, save isolation, bench/unlock rules, tool presets, status panels, controller rebinding, save-slot preserve/reset behavior, and 315 mixed-loadout attacks against 35 source baselines, plus four focused drill and neutral follow-up attacks. Mixed rows rotate seven sources through all five slots on nine crest forms. `--full-matrix --timeout 3600` uses 49 pairwise rows per crest (2,205 mixed attacks), covering every pair of sources across slots. Neither mode exhausts all 16,807 five-slot combinations per crest. Runtime depends on the machine; the full matrix takes substantially longer.

`--features-only` checks controls, presets, status, cloak, and four focused drill/follow-up attacks without repeating the broad attack matrix.

`--keep-workspace` preserves disposable files for investigation; otherwise they are removed when the runner exits. Expected compatibility-failure and subscriber-failure diagnostics are deliberately injected. The runner reports failure if an assertion fails and terminates only its own game process.

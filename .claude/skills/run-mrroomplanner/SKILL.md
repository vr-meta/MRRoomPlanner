---
name: run-mrroomplanner
description: Build, run, test and inspect MRRoomPlanner (Unity 6 MR app for Meta Quest 3). Use when asked to run tests, run EditMode/PlayMode tests, rebuild the rig / Setup Measure Rig, build the APK, check compilation errors, inspect or screenshot the Unity scene, or drive the Unity Editor over the Meta MCP bridge.
---

# Run MRRoomPlanner

Unity 6 (6000.0.81f1) MR app for Meta Quest 3. There is **no desktop window to click** —
the app runs on the headset. What you drive is the **Unity Editor**, through one driver:

```
.claude/skills/run-mrroomplanner/driver.mjs
```

All paths below are relative to the project root (`D:\Dev\MRRoomPlanner`).

The driver picks one of two transports automatically:

| Transport | When | Speed | Can do |
|---|---|---|---|
| **BRIDGE** | Unity Editor **open** + Meta MCP bridge running | EditMode ~3s | tests, scene inspection, screenshots, compile status |
| **BATCH** | Unity Editor **closed** | ~60–90s per run | everything incl. **PlayMode** tests, rig setup, APK |

They are **mutually exclusive**: batchmode needs the project lock that the open Editor holds.

## Prerequisites

Unity 6000.0.81f1 at `D:\Unity\Editors\6000.0.81f1\Editor\Unity.exe` (override with `UNITY_EXE`).
Node 22+ for the driver. Nothing else to install.

For the BRIDGE transport, the Editor must have the bridge enabled **once**:
`Edit → Preferences… → Meta XR → AI Tools → Enable AI Agent Bridge` (default **off**).
It then auto-starts on port 48736 and writes `%TEMP%\mcpbridge_*.info` with its access token —
the driver reads that file, so **the driver works without any MCP registration**.

## Run (agent path)

Start here. `status` tells you which transport you're on:

```bash
node .claude/skills/run-mrroomplanner/driver.mjs status
```
```
project      : D:\Dev\MRRoomPlanner
unity        : D:\Unity\Editors\6000.0.81f1\Editor\Unity.exe
editor open  : YES (bridge path; batchmode blocked)
mcp bridge   : port 48736 (pid 35304)
compilation  : clean (errors: 0)
```

### Tests

```bash
node .claude/skills/run-mrroomplanner/driver.mjs test EditMode   # → "EditMode: 32/32 passed (3.2s)"
node .claude/skills/run-mrroomplanner/driver.mjs test PlayMode   # close the Editor first
node .claude/skills/run-mrroomplanner/driver.mjs test All
```

EditMode = pure logic/geometry (`RoomPlanner.Core`). PlayMode = live scene + **physics**,
needed because `SceneModel.TryPick` selects objects with a real `Physics.RaycastAll`.

**Run PlayMode with the Editor closed.** Under the bridge it enters play mode, which is slow
and blocks the Editor main thread.

### Inspect the live scene (bridge only)

```bash
node .claude/skills/run-mrroomplanner/driver.mjs scene MeasureRig
node .claude/skills/run-mrroomplanner/driver.mjs inspect 61184     # id from `scene`
node .claude/skills/run-mrroomplanner/driver.mjs errors
```

This is how you verify a rig rebuild — check the components are present and wired, instead of
grepping GUIDs in `Assets/Measure.unity`.

### Screenshots (bridge only)

```bash
node .claude/skills/run-mrroomplanner/driver.mjs shot SceneView            # → ci-shots/SceneView.png
node .claude/skills/run-mrroomplanner/driver.mjs shot ConsoleWindow
```
Window type names come from `mcp UIVerificationTools ListOpenWindows`
(`SceneView`, `GameView`, `ConsoleWindow`, `InspectorWindow`, `SceneHierarchyWindow`, `ProjectBrowser`).
**Open the PNG and look at it** — a capture of the wrong window still "succeeds".

### Rig setup and APK (Editor must be CLOSED)

```bash
node .claude/skills/run-mrroomplanner/driver.mjs setup    # = RoomPlanner > Setup Measure Rig, saves the scene
node .claude/skills/run-mrroomplanner/driver.mjs build    # → Build/MRRoomPlanner.apk
```

Run `setup` whenever the **rig composition** changes (new components, new serialized fields,
new prefabs). A plain build does **not** backfill new serialized fields on the existing scene
object. Logic-only edits don't need it.

### Raw bridge access

Any tool/method the bridge exposes, without MCP registration:

```bash
node .claude/skills/run-mrroomplanner/driver.mjs mcp CompilationTools GetCompilationStatus
node .claude/skills/run-mrroomplanner/driver.mjs mcp TestRunnerTools GetResults
node .claude/skills/run-mrroomplanner/driver.mjs mcp UIVerificationTools ListOpenWindows
```
Tools: `CompilationTools`, `TestRunnerTools`, `SceneObjectsTools`, `DiagnosticTools`,
`UIVerificationTools`, `CodeAnalysisTools`, `UPSTTools`, `BuildingBlocksTools`, `ToolHelp`,
`DebugLog`, `DrawingTools`, `IReflectionService`, `ImmersiveDebuggerTools`,
`InteractionTestingTools`, `DesignTokenResource`, `IGetResourcesService`.

## Run (human path)

The PowerShell scripts behind the batch transport, if you want them directly:

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File ci/run-tests.ps1 -Mode All
powershell -NoProfile -ExecutionPolicy Bypass -File ci/unity-run.ps1 -Method RoomPlanner.EditorTools.CiTools.SetupRig
```

Verifying a build on device: `adb logcat` should show `[Tools] v10 started …` / `[Measure] v10 started …`.

## Gotchas

- **Editor open ⇄ batchmode.** `Temp/UnityLockfile` exists while the Editor runs; batchmode then
  exits instantly with no log at all. The driver checks this and says so.
- **A crashed Editor leaves a stale `Temp/UnityLockfile`** (and a stale `%TEMP%\mcpbridge_*.info`).
  Believing either one sends you down the wrong transport — the driver ended up calling a dead
  bridge and got `ECONNREFUSED`. Both markers are now validated against a live process
  (`tasklist` / `process.kill(pid, 0)`); `status` prints `STALE … (ignored)` when it sees one.
- **A run that dies on compiler errors leaves the PREVIOUS `TestResults-*.xml` in place.** Reading
  it reports last run's numbers as if they were fresh — worse than no result. The driver deletes
  the XML before each run and prints the `error CS…` lines when no results appear.
- **Never use `TestRunnerTools.WaitForTestRun` from a script.** It holds the HTTP response open
  while the Editor runs tests; node's fetch kills it with `UND_ERR_HEADERS_TIMEOUT` and you get a
  hang with no output. The driver polls `GetResults` instead. Same reason `RunAll` for PlayMode
  appears to "time out" while the run actually succeeds.
- **The bridge reports every test twice.** A 32-test run comes back as `total: 64`. Dedupe by
  `fullName` (the driver does).
- **`CaptureWindow` returns the image in `base64Png`**, not `base64`/`image`/`data`.
- **Instance IDs are not stable.** `MeasureRig` was 57392 → 58678 → 61184 across domain reloads.
  Always `scene <name>` first, then `inspect <id>` — never reuse an old id.
- **Unity registers the MCP bridge with Claude Code at *local* scope**, tied to the project
  directory. A session whose cwd is elsewhere (e.g. `D:\Dev`) won't see it. Re-register at user
  scope with the token from `%TEMP%\mcpbridge_*.info`:
  `claude mcp add --scope user meta-xr-unity-runtime --transport http http://127.0.0.1:48736/mcpbridge/ --header "Authorization:Bearer <token>"`.
  MCP tools only load at session start — restart the session after registering.
  **The driver needs none of this** and works either way.
- **`Unity.exe` is a GUI-subsystem binary.** Launched as `& unity …` from PowerShell it detaches
  and the shell returns immediately with an empty exit code — the run is still going. Use
  `Start-Process -Wait` (the `ci/*.ps1` scripts do).
- **These console errors are pre-existing and harmless:** "Meta XR Simulator Installer: Failed to
  download installer" and "[McpRegistration] claude mcp remove … failed (exit 1)" (it removes
  before adding). Don't chase them.
- **`EditorUtility.DisplayDialog` auto-returns in batchmode**, which is why `setup` doesn't hang
  on the summary dialog that the interactive menu item shows.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Unity Editor is OPEN — batchmode needs the project lock` | Close the Editor, or use bridge-only commands (`status`, `scene`, `shot`, `test EditMode`) |
| `MCP bridge not running (no discovery file)` | Editor closed, or bridge disabled → `Preferences → Meta XR → AI Tools → Enable AI Agent Bridge` |
| `test` hangs with no output | You're on an old driver using `WaitForTestRun`; poll `GetResults` instead |
| batchmode exits instantly, `ci-*.log` empty/absent | Lockfile present (Editor open), or wrong `-projectPath` |
| `Unity … not found` | Set `UNITY_EXE=<path to Unity.exe>` |
| New serialized field is null at runtime | You skipped `driver.mjs setup` after changing the rig |

# MR Room Planner

[![Build APK](https://github.com/vr-meta/MRRoomPlanner/actions/workflows/build-apk.yml/badge.svg)](https://github.com/vr-meta/MRRoomPlanner/actions/workflows/build-apk.yml)

Mixed-reality interior planner for **Meta Quest 3 / 3S** (Horizon OS). Scan your room,
then measure, rebuild and restyle it right in passthrough: virtual walls and floors,
doors and windows, stairs, real-scale paint and textures, an electrical layer with
cable-length takeoff, and IFC import so you can walk through a building designed in Revit.

Built with **Unity 6** and the **Meta MR Utility Kit** (MRUK), starting from the
Virtual Home sample. All geometry and finishes are parametric data — meshes and
materials are generated procedurally (no CSG, no baked unwraps).

## Features

- **Tape measure** — Layout-style "hands" mode (pin point A, walk away, pin B) with
  corner/surface magnets and draggable vertices, plus a far-ray mode.
- **Walls & floors** — chain-drawn parametric walls with vertex handles (drag a node,
  every wall on it follows); polygonal floor slabs with holes (stairwells), multiple levels.
- **Openings & stairs** — doors and windows cut into walls parametrically; straight stairs.
- **Paint & materials** — 8 color presets + 39 CC0 textures (ambientCG) in
  Walls / Floors / Tiles / Ceiling tabs; 12 laminate looks baked from real plank scans
  (deck, herringbone, basket-weave × natural / grey / dark / bleached) with relief
  normal maps; 18 procedural ceramic tiles (subway "kabanchik", grid, herringbone × 6
  glazes). Metric tile size (Tile W/H), 15°-step texture rotation, gloss control.
  Every application is per-surface and undoable.
- **Electrical** — outlets (1–5 posts) and switches (1–3 keys) snapped to walls, wire
  routing along walls and ceiling with orthogonal bends, junction boxes, and a
  distribution panel that totals cable length per cable type.
- **Blueprint mode** — put a floor-plan image on the floor, calibrate scale and rotation
  with two point pairs, then trace the walls over it.
- **IFC import** — walls, slabs, openings, stairs, storeys and plumbing fixtures from a
  Revit-exported IFC; walk the model with teleport and smooth locomotion.
- **Projects** — save/load: geometry, finishes (including texture rotation), electrical.
- **UI** — radial tool menu (hold A), floating inspector with tool settings, snap
  toggles strip, global undo/redo on X/Y.

## Requirements

- **Meta Quest 3 or Quest 3S** — color passthrough and the Scene API are required.
- A one-time **Space Setup** room scan (Settings → Environment Setup) for MR mode.
  There is also a scan-off mode (virtual sky + ground) for working from a blueprint
  or an imported IFC without scanning.

## Install (sideload from Releases)

The app is distributed as an APK on the [Releases page](../../releases/latest) and is
installed by sideloading — the standard way to run your own builds on a Quest.

1. **Enable Developer Mode** (one time):
   - Create a (free) developer organization at
     [developer.oculus.com](https://developer.oculus.com/manage/organizations/create/)
     and verify your account (phone or card).
   - In the **Meta Horizon** phone app: *Menu → Devices → your headset → Settings →
     Developer Mode → ON*, then reboot the headset.
2. **Download** `MRRoomPlanner.apk` from the [latest release](../../releases/latest).
3. **Sideload it** — pick whichever tool you prefer:
   - **[SideQuest](https://sidequestvr.com/setup-howto)** (easiest): connect the headset
     over USB, allow USB debugging in the headset, then *Install APK file from folder*
     and select the downloaded APK.
   - **[Meta Quest Developer Hub](https://developers.meta.com/horizon/documentation/unity/ts-odh/)**:
     drag the APK onto your device in the Device Manager.
   - **adb** (command line):

     ```
     adb install -r MRRoomPlanner.apk
     ```

4. **Launch**: in the headset go to *Library → Unknown Sources → MR Room Planner*.
5. On first run allow the **spatial data** permission (the room scan) — and if you have
   never scanned the room, run *Settings → Environment Setup → Space Setup* once.

Updating to a new release is the same: install over the top (`adb install -r`), saved
projects are kept.

## Controls (short version)

| Input | Action |
|---|---|
| **Hold A** (or L3 / ≡) | Radial tool menu |
| **Right trigger** | Place point / select / drag |
| **Right grip** | Snap modifier (axis / 15° angle), drag panels |
| **B** | Delete or cancel under cursor; on empty — back to Select |
| **X / Y** | Undo / Redo |
| **Tap A** | Teleport to pointed floor spot |
| **Left trigger (hold)** | Teleport arc |
| **Left stick** | Smooth move (scan-off mode) |

Full layout: [`docs/design/10-controls.md`](docs/design/10-controls.md) (Russian).

## Build from source

- **Unity 6000.0.81f1** with Android Build Support (all packages, including the Meta XR
  SDK, resolve from the standard Unity registry — see `Packages/manifest.json`).
- Clone and open the project — the CC0 texture set and the procedural ceramic bakes are
  committed, so a clean checkout builds a working APK right away.
- **Laminate is the one exception**: it is baked from proprietary plank scans that
  cannot be redistributed, so those textures are not in the repo. Without them the app
  builds and runs fine — the Laminate tab just falls back to plain color. If you have
  your own per-plank scans, point `RP_LAMINATE_SRC` at them and run
  *RoomPlanner → Bake Laminate*.

Headless pipeline (Unity Editor must be closed — batchmode takes the project lock):

| Task | Command |
|---|---|
| Run all tests | `powershell -File ci/run-tests.ps1 -Mode All` |
| Rebuild the scene rig | `powershell -File ci/unity-run.ps1 -Method RoomPlanner.EditorTools.CiTools.SetupRig` |
| Build the APK | `powershell -File ci/unity-run.ps1 -Method RoomPlanner.EditorTools.CiTools.BuildAndroid -TimeoutMin 30` |
| Full release (build + GitHub release) | `powershell -File ci/release.ps1 -Version vX.Y.Z` |

## CI

[GitHub Actions](.github/workflows/build-apk.yml) builds the APK with
[GameCI](https://game.ci/) on every push to `main` and uploads it as a workflow
artifact. To make it work in your fork, add these repository secrets
(see [GameCI activation docs](https://game.ci/docs/github/activation)):

| Secret | Value |
|---|---|
| `UNITY_LICENSE` | Contents of your `Unity_lic.ulf` |
| `UNITY_EMAIL` | Unity account e-mail |
| `UNITY_PASSWORD` | Unity account password |

CI builds exclude the laminate materials (see above); release APKs on the Releases
page are full-content builds made with `ci/release.ps1`.

## Documentation

Design docs and the development checklist are in Russian:

- [`ARCHITECTURE.md`](ARCHITECTURE.md) — top-level map of the codebase.
- [`docs/CHECKLIST.md`](docs/CHECKLIST.md) — phase-by-phase development log.
- [`docs/design/`](docs/design/README.md) — per-module design decisions
  (parametric surfaces, openings, electrical, UI system, laminate/tile generators…).

## Licensing notes

- **Code**: no open-source license has been granted yet — all rights reserved.
- **Textures**: the committed texture set is [CC0 from ambientCG](https://ambientcg.com);
  ceramic tiles are generated procedurally by this project. The laminate source scans
  are proprietary and are **not** part of the repository; release APKs contain only
  baked derivatives embedded in the product.

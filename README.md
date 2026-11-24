# ShoeMod — Skate Story In-Game Editor

**Source repository for ShoeMod.** The `main` branch holds version 1.2.0 (in development). The exact source of the released 1.1.0 (the Nexus Mods download) is on the [`release-1.1.0`](../../tree/release-1.1.0) branch.

ShoeMod is a fan-made in-game editor for the Windows/Steam version of *Skate Story*. Press **F6** in-game to open or close the editor.

> This repository contains the full mod: the C# source, the patch files, and a ready-to-install development build in [`dist/ShoeMod-1.2.0-dev.zip`](dist/ShoeMod-1.2.0-dev.zip) (the compiled installer, the mod DLL and the patch files). It contains no game files: the patch files hold only the changes, and the installer makes the changed files from your own copy of the game. The stable release (1.1.0) is on the project's [Nexus Mods page](https://www.nexusmods.com/skatestory/mods/3).

## Features

- **Shoes:** customize shoe-part colors and paint designs onto the shoe model.
- **Board:** customize deck colors and paint designs.
- **Screen:** move or hide selected HUD elements and skip selected startup screens.
- **Camera:** offsets on the game camera, rig shots (front, side, chase, fisheye, top down, drone), fixed camera spots per level, and visual effects.
- **Movement:** push power, power slide boost, jump height, air spins past 360, front and back flips (drawn only, with timing sliders), grind snap and hold, game speed, and health/bail options.
- **Effects:** board effects, body particles, body material and layers, and a finder that hides objects near the board.
- **Levels (1.2.0):** free skate in any level, with story objectives and level exits turned off. The real save is copied first and put back afterwards.
- **Screen (1.2.0):** toggles for subtitles and boss headers (boss health bars and fight timers).

The editor's detailed controls and known limitations are described in the release README distributed with the [Nexus Mods download](https://www.nexusmods.com/skatestory/mods/3).

## Requirements

- *Skate Story* for Windows, installed through Steam.
- A game build compatible with the mod release. The 1.1.0 release README identifies the target as the November 2026 game version.

No separate mod loader is required by the release instructions.

## Installation

The stable release (1.1.0) is on [Nexus Mods](https://www.nexusmods.com/skatestory/mods/3).

The newest development build (1.2.0) is in this repository: download [`dist/ShoeMod-1.2.0-dev.zip`](dist/ShoeMod-1.2.0-dev.zip), extract the full ZIP file, start `ShoeModInstaller.exe` and type 1. The instructions are in `README.txt` in the ZIP file. Some new features in this build are not fully tested.

SHA-256 of the files in the ZIP:

```text
9c30c511ab9e93dc59a59747142e620b98d9c5c24f82a10c1aea9c3093f07736  ShoeMod.dll
89aec89130b8a5b4a7f33633c89f251aaa94bc4d63fe232a736e0008665f2cf6  ShoeModInstaller.exe
```

Windows can show a "Windows protected your PC" message, because the installer has no paid signature. Click "More info", then "Run anyway". The installer source is `source/Installer.cs`.

## Source layout

```text
source/
  ShoeMod.cs       Main shoe/board editor and loader
  ShoeCore.cs      Shoe editor painting and geometry logic
  MoveTools.cs     Movement controls
  FxTools.cs       Board/body effects controls
  ScreenTools.cs   HUD and startup-screen controls
  CameraTools.cs   Camera and post-processing controls
  WarpTools.cs     Levels tab: free skate in any level (1.2.0)
  SubTools.cs      Subtitle and boss header toggles (1.2.0)
  ReplayTools.cs   Replay editor: camera, free cam, camera paths, clips (1.2.0)
  ReplayLens.cs    Replay-only lens: zoom, focus, aperture, vignette (1.2.0)
  LookTools.cs     Green screen and depth map (1.2.0)
  EditorTools.cs   Park and level editor: blocks, materials, vert ride (1.2.0)
  FlipPose.cs      Pose layer for flips and spins, feet on the board (1.2.0)
  TrickTools.cs    Darkslide and hippy jump (1.2.0)
  TextDump.cs      Writes the game's text tables to files (1.2.0)
  Installer.cs     Windows installer and patch/restore logic
```

```text
patches/           Patch files for the shoe, costume and deck asset files (only the changes, no game files)
package/README.txt The readme that goes in the ZIP file
dist/              The ready-to-install ZIP file of the development build
package.sh         Builds the mod and makes the ZIP file
```

## Building

See [`docs/BUILD.md`](docs/BUILD.md). Run `build.bat` (Windows) or `build.sh` (Linux or macOS) with your own Skate Story folder, which supplies the reference DLLs. A build with Mono 6.8 `mcs` is byte for byte the same as the released `ShoeMod.dll` and `ShoeModInstaller.exe`; the SHA-256 checksums are in the build guide.

## Version notes

See [`CHANGELOG.md`](CHANGELOG.md). The released 1.1.0 is built from the `release-1.1.0` branch (see the checksums in the build guide). `main` is 1.2.0, in development, and is not released yet.

## Distribution and permissions

This is an unofficial fan-made project and is not affiliated with Sam Eng or Devolver Digital. *Skate Story* and its game assets remain the property of their respective rights holders.

No license granting reuse, redistribution, or modification rights is currently provided in this repository. Unless the author adds a license, all rights in the source code are reserved by the author. Do not redistribute game files or proprietary game assets here.

## Links

- Nexus Mods release: https://www.nexusmods.com/skatestory/mods/3
- Issues: use the GitHub Issues tab for reproducible bugs and feature requests

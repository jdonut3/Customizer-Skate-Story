# ShoeMod — Skate Story In-Game Editor

**Source repository for ShoeMod.** The `main` branch holds version 1.2.0 (in development). The exact source of the released 1.1.0 (the Nexus Mods download) is on the [`release-1.1.0`](../../tree/release-1.1.0) branch.

ShoeMod is a fan-made in-game editor for the Windows/Steam version of *Skate Story*. Press **F6** in-game to open or close the editor.

> This repository contains C# source and project documentation only. It intentionally does not include the compiled installer, compiled mod DLL, game files, or binary patch files. Get the playable release from the project's Nexus Mods page.

## Features

- **Shoes:** customize shoe-part colors and paint designs onto the shoe model.
- **Board:** customize deck colors and paint designs.
- **Screen:** move or hide selected HUD elements and skip selected startup screens.
- **Camera:** offsets on the game camera, rig shots (front, side, chase, fisheye, top down, drone), fixed camera spots per level, and visual effects.
- **Movement:** push power, power slide boost, jump height, air spins past 360, front and back flips (drawn only, with timing sliders), grind snap and hold, game speed, and health/bail options.
- **Effects:** board effects, body particles, body material and layers, and a finder that hides objects near the board.
- **Levels (1.2.0):** free skate in any level, with story objectives and level exits turned off. The real save is copied first and put back afterwards.
- **Screen (1.2.0):** toggles for subtitles and boss headers (boss health bars and fight timers).

The editor's detailed controls and known limitations are described in the release README distributed with the Nexus Mods download.

## Requirements

- *Skate Story* for Windows, installed through Steam.
- A game build compatible with the mod release. The 1.1.0 release README identifies the target as the November 2026 game version.

No separate mod loader is required by the release instructions.

## Installation

Download the packaged release from Nexus Mods and follow the instructions included with that release. This source repository is not an installer and cannot install the mod by itself.

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
  Installer.cs     Windows installer and patch/restore logic
```

## Building

See [`docs/BUILD.md`](docs/BUILD.md). Run `build.bat` (Windows) or `build.sh` (Linux or macOS) with your own Skate Story folder, which supplies the reference DLLs. A build with Mono 6.8 `mcs` is byte for byte the same as the released `ShoeMod.dll` and `ShoeModInstaller.exe`; the SHA-256 checksums are in the build guide.

## Version notes

See [`CHANGELOG.md`](CHANGELOG.md). The released 1.1.0 is built from the `release-1.1.0` branch (see the checksums in the build guide). `main` is 1.2.0, in development, and is not released yet.

## Distribution and permissions

This is an unofficial fan-made project and is not affiliated with Sam Eng or Devolver Digital. *Skate Story* and its game assets remain the property of their respective rights holders.

No license granting reuse, redistribution, or modification rights is currently provided in this repository. Unless the author adds a license, all rights in the source code are reserved by the author. Do not redistribute game files or proprietary game assets here.

## Links

- Nexus Mods release: **add your mod page URL here**
- Issues: use the GitHub Issues tab for reproducible bugs and feature requests

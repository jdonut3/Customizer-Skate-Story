# ShoeMod — Skate Story In-Game Editor

**Source repository for ShoeMod 1.1.0**

ShoeMod is a fan-made in-game editor for the Windows/Steam version of *Skate Story*. Press **F6** in-game to open or close the editor.

> This repository contains C# source and project documentation only. It intentionally does not include the compiled installer, compiled mod DLL, game files, or binary patch files. Get the playable release from the project's Nexus Mods page.

## Features

- **Shoes:** customize shoe-part colors and paint designs onto the shoe model.
- **Board:** customize deck colors and paint designs.
- **Screen:** move or hide selected HUD elements and skip selected startup screens.
- **Camera:** adjust camera position and view settings; configure visual effects.
- **Movement:** adjust push power, jump height, grind behavior, game speed, and selected health/bail behavior.
- **Effects:** configure board effects, body particles/material appearance, and nearby object visibility.

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
  Installer.cs     Windows installer and patch/restore logic
```

## Building

See [`docs/BUILD.md`](docs/BUILD.md). The source snapshot supplied with release 1.1.0 does not include a Visual Studio solution, project file, dependency assemblies, or a fully specified build script. Do not assume it is reproducibly buildable from this repository alone until those are added and tested.

## Version notes

See [`CHANGELOG.md`](CHANGELOG.md). The source snapshot currently contains version strings identifying the mod loader as **1.0.0**, while the installer identifies itself as **1.1.0**. This discrepancy is recorded rather than silently changed; verify the source against the exact DLL shipped in the 1.1.0 release before treating this repository as an exact source match.

## Distribution and permissions

This is an unofficial fan-made project and is not affiliated with Sam Eng or Devolver Digital. *Skate Story* and its game assets remain the property of their respective rights holders.

No license granting reuse, redistribution, or modification rights is currently provided in this repository. Unless the author adds a license, all rights in the source code are reserved by the author. Do not redistribute game files or proprietary game assets here.

## Links

- Nexus Mods release: **add your mod page URL here**
- Issues: use the GitHub Issues tab for reproducible bugs and feature requests

# Build instructions

The release files are built from the files in `source/` with no other code. The game is not in this repository:
you need your own copy of Skate Story (Steam, Windows) for the reference DLLs, which are not redistributed here.

## What the release contains

| File | Built from | How |
|---|---|---|
| `ShoeMod.dll` | `source/ShoeMod.cs`, `ShoeCore.cs`, `ScreenTools.cs`, `CameraTools.cs`, `MoveTools.cs`, `FxTools.cs` (1.2.0 adds the files listed under Branches) | C# compiler, against the game's own DLLs |
| `ShoeModInstaller.exe` | `source/Installer.cs` | C# compiler, .NET Framework 4 |
| `patches/*.smp` | `tools/mkpatch.py` | Binary differences between original and edited game asset files (see below) |

No other files, no downloads at run time, and no network access. The mod uses no mod loader: the installer adds it
to the game's own start-up list (`ScriptingAssemblies.json` and `RuntimeInitializeOnLoads.json`).

## Requirements

- Skate Story installed (game version of November 2026). The DLLs come from `SkateStory_Data\Managed`:
  mscorlib, System, System.Core, UnityEngine.CoreModule, UnityEngine.IMGUIModule, UnityEngine.TextRenderingModule,
  UnityEngine.InputLegacyModule, UnityEngine.UIModule, UnityEngine.UI, Unity.Postprocessing.Runtime,
  UnityEngine.PhysicsModule, Unity.Addressables, Unity.ResourceManager, UnityEngine.ParticleSystemModule, Assembly-CSharp,
  and on `main` also Unity.Mathematics.
- A C# compiler. One of:
  - Windows: `csc.exe` of .NET Framework 4, which comes with Windows
    (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`).
  - Linux or macOS: Mono 6.8 (`mcs`). The release was built with Mono 6.8.0.105 `mcs`.

## Build

Windows:

    build.bat "C:\Program Files (x86)\Steam\steamapps\common\Skate Story"

Linux or macOS (with a copy of the game folder):

    ./build.sh "/path/to/Skate Story"

The output goes to `build/`.

## Branches

- `main`: version 1.2.0 (in development). It adds `WarpTools.cs`, `SubTools.cs`, `ReplayTools.cs`, `ReplayLens.cs`, `LookTools.cs`, `EditorTools.cs`, `FlipPose.cs`, `TrickTools.cs`, `TextDump.cs` and a reference to `Unity.Mathematics.dll`. The build scripts include them.
- `release-1.1.0`: the exact source of the released 1.1.0. The checksums below are for that branch.

## Check the result against the release

With Mono 6.8 `mcs`, a build of the `release-1.1.0` branch is byte for byte the same as the 1.1.0 release files:

    ShoeMod.dll           SHA-256  818cffc4e4a0d85ac7bf31a3a0a1ff9c69862b0543db9dcb906e812455deb45e
    ShoeModInstaller.exe  SHA-256  c2c31d5f293557192031dccd111e6e37a4416c64c26785a40e65069dbda729bf

Windows: `certutil -hashfile build\ShoeMod.dll SHA256`.  Linux: `sha256sum build/*`.
The Windows `csc.exe` makes a different file (other compiler), with the same code.

Note: on the `release-1.1.0` branch, the comment at the top of `ShoeMod.cs` and its start-up log line say 1.0.0. That is only text; the checksums above prove that the 1.1.0 DLL is built from those files. On `main` the text says 1.2.0.

## The patch files

The mod changes three asset bundles of the game (the shoe and deck models get more triangles, so that they can be painted).
The ZIP holds no game files. Each `.smp` file holds only the changed bytes, plus "copy this range from your own file" steps.
The installer applies a patch only when the SHA-256 of the user's file is the original one, checks the SHA-256 of the result,
and keeps a backup of the original. The format and the code that makes and applies the patches are in `tools/mkpatch.py`
and `source/Installer.cs` (class `Patch`).

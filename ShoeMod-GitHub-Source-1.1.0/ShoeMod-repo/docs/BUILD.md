# Build information

## Current state

The supplied source archive contains these C# files:

- `source/ShoeMod.cs`
- `source/ShoeCore.cs`
- `source/MoveTools.cs`
- `source/FxTools.cs`
- `source/ScreenTools.cs`
- `source/CameraTools.cs`
- `source/Installer.cs`

It does **not** contain a `.sln`, `.csproj`, package manifest, compiler script, or the game/Unity reference assemblies used to compile the mod DLL. The archive alone therefore does not establish a reproducible build procedure.

## What is needed for a reproducible build

1. Identify the exact compiler and target .NET/Mono framework used for the shipped DLL and installer.
2. Record the exact Unity and game-managed reference assemblies required by the mod source, including the post-processing assembly used by `CameraTools.cs`.
3. Add project files or build scripts that reference those assemblies without copying proprietary game binaries into this public repository.
4. Record compiler versions, build configuration, and commands.
5. Build the DLL and installer from a clean checkout and compare the resulting outputs with the release artifacts.
6. Confirm that the source version strings match the release version before tagging a release.

## Important release audit note

The release archive labels the installer as 1.1.0, but `source/ShoeMod.cs` contains `version 1.0.0` in its header and loader log message. Verify whether the DLL was built from these exact source files. Do not describe this repository as a verified, exact source match until that is resolved.

## Installer behavior

The release README describes an installer that locates the Steam game installation, verifies expected game files, applies the included patches, backs up originals, and can restore the originals. The installer binary and patch files are intentionally excluded from this source repository. The installer source is included for inspection.

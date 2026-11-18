#!/bin/sh
# Builds ShoeMod.dll and ShoeModInstaller.exe on Linux or macOS with Mono (the release was built with Mono 6.8 mcs).
# Usage: ./build.sh "/path/to/Skate Story"   (a copy of the game folder, for the reference DLLs)
set -e
GAME="${1:?give the Skate Story folder}"; M="$GAME/SkateStory_Data/Managed"; mkdir -p build
# shellcheck disable=SC2086
IFS='
'
mcs -nologo -nostdlib -noconfig -target:library -optimize -out:build/ShoeMod.dll \
  $(for d in mscorlib System System.Core UnityEngine.CoreModule UnityEngine.IMGUIModule UnityEngine.TextRenderingModule \
    UnityEngine.InputLegacyModule UnityEngine.UIModule UnityEngine.UI Unity.Postprocessing.Runtime UnityEngine.PhysicsModule \
    Unity.Addressables Unity.ResourceManager UnityEngine.ParticleSystemModule Unity.Mathematics Assembly-CSharp; do echo "-r:$M/$d.dll"; done) \
  source/ShoeMod.cs source/ShoeCore.cs source/ScreenTools.cs source/CameraTools.cs source/MoveTools.cs source/FxTools.cs source/WarpTools.cs source/SubTools.cs
mcs -nologo -target:exe -platform:anycpu -out:build/ShoeModInstaller.exe source/Installer.cs
echo "Done: build/ShoeMod.dll and build/ShoeModInstaller.exe"

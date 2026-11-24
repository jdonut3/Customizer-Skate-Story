#!/bin/sh
# Builds the mod and makes the installable ZIP: build/ShoeMod-1.2.0-dev.zip
# Usage: ./package.sh "/path/to/Skate Story"   (a copy of the game folder, for the reference DLLs)
set -e
./build.sh "${1:?give the Skate Story folder}"
rm -rf build/ShoeMod && mkdir -p build/ShoeMod/patches build/ShoeMod/source
cp build/ShoeMod.dll build/ShoeModInstaller.exe package/README.txt build/ShoeMod/
cp patches/*.smp build/ShoeMod/patches/
cp source/*.cs build/ShoeMod/source/
(cd build && rm -f ShoeMod-1.2.0-dev.zip && zip -qrX ShoeMod-1.2.0-dev.zip ShoeMod)
echo "Done: build/ShoeMod-1.2.0-dev.zip"

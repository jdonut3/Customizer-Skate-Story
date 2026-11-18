# Changelog

## 1.2.0 (in development, on `main`)

- Levels tab: free skate in any level. Uses the game's scene loader only (not the story loader), turns off objectives, challenges and level exits, and puts the copied save back at the end (also after a crash).
- Screen tab: toggles for subtitles and for boss headers (boss health bars, fight timers). The player's own health bar stays.
- Camera tab: rework. Three modes: game camera with offsets, rig shots, and fixed spots per level. The wall check ignores the skater and is smoothed. During a flip, the game camera holds its view.
- Movement tab: air spins past 360 (turns the skater and the game's saved pose and body smoothing, so the body follows the board; the trick count measures the real turn). Front and back flips with two-button combos that can be set in the menu; the flip is drawn only (physics, speed and landing do not change), with sliders for the start delay, flip time, start speed, soft end, turn point, finish before landing, lift, and single or double.
- Movement tab: "Normal health in the fragile mode" keeps the end-game look (the fragile flag is not changed).
- Effects tab: board effects are copies that the game does not stop; body particles per body type; body look and layers; a finder for objects near the board.
- Performance: slow searches run only after a level load.
- The version text in `ShoeMod.cs` now says 1.2.0.

## 1.1.0

- Release package identifies this version as 1.1.0.
- The included installer reports version 1.1.0.
- The supplied release README documents the Shoes, Board, Screen, Camera, Movement, and Effects editor tabs.
- The released `ShoeMod.dll` and `ShoeModInstaller.exe` are built from the files in `source/`. A build with Mono 6.8 `mcs` is byte for byte the same; see `docs/BUILD.md`.
- The comment and the start-up log line in `ShoeMod.cs` still say 1.0.0. This is only text and changes in the next version.

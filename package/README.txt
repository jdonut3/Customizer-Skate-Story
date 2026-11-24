SKATE STORY - IN-GAME EDITOR  1.2.0 (in development)
====================================================

An editor inside the game. Press F6 while you play to open it.
Tabs: Shoes, Board, Screen, Camera, Movement, Effects, Levels, Replay.
This is a development build from the GitHub repository. Some new features are not fully tested.
All changes show immediately and are saved. Each feature can be turned on and off.


WHAT YOU NEED
- Skate Story on Steam (Windows). The mod was made for the game version of November 2026.
- No mod loader is necessary.


INSTALL
1. Close the game.
2. Extract the full ZIP file to a folder (for example, your Desktop).
3. Start "ShoeModInstaller.exe".
   Windows can show a "Windows protected your PC" message, because the file has no
   paid signature. Click "More info", then "Run anyway". The source code is in the "source" folder.
4. Type 1 and press Enter.
5. Start the game. Press F6.

The installer finds the game through Steam. If it cannot find the game, it asks you for the folder.
It changes five game files and adds one file. It saves the originals in "ShoeMod_backup" in the game folder.
To update from an older version, do the same steps with the new ZIP file.


REMOVE
1. Close the game.
2. Start "ShoeModInstaller.exe", type 2 and press Enter.
The installer puts the original files back. As an alternative, use Steam:
Properties > Installed Files > Verify integrity of game files.


HOW TO USE
F6 opens and closes the editor.

SHOES and BOARD
- Colors: click a part in the list, then set its color with the color square, the sliders,
  the hex box or the quick colors.
- Paint: click a part in the list, then drag on the large picture. The area becomes that part.
  Brush = drag. Fill = click an area. Right-click = select the part under the pointer.
  "Both sides" paints the opposite side at the same time. Undo has 30 steps.
- Shoes: "Three side stripes" changes the original side mark to three stripes.
- Glass: gives the shoes or the board the material of the player's body.
- Designs: the list at the top holds your designs. A built-in design is read-only:
  your first change makes a copy.
- "Show scarf" at the top hides or shows the scarf.

SCREEN
- Move or hide the trick name and the combo counter.
- Show or hide the speed bar.
- Find and hide other things that show on the screen in a jump.
- Skip the opening screen and the seizure warning at the game start.
- Subtitles and boss headers can be hidden.
- Green screen (the skater and the board stay in front) and a depth map.

CAMERA
- Move the game camera: height, distance, side (over the shoulder), tilt, turn and zoom.
  All at 0 = the game camera. Presets and saved views.
- The camera does not go into walls (option), and can be for skating only.
- Effects: depth of field (with automatic focus on the player), motion blur, glow,
  vignette, color fringe and film grain.

MOVEMENT
- Push power, and a speed boost from power slides (with no speed loss during the slide).
- Jump height.
- Grinds: snap onto rails below you, and stay on the rail until you jump off.
- Health: normal health in the fragile mode (after the end of the game), no damage, no bails.
- Slow motion for the full game (F7 turns it on and off), or only in the air and in tricks.
- Game speed, with a readout of the speed now and a reset button.
- Extra air spins past 360, with a tuck and grab pose.
- Front and back flips with two-button combos. The board stays at the feet.
- Darkslide: press X and Y in a grind. The board rolls over.
- Hippy jump: hold the set button (default B) and ollie. The board stays on the ground.

EFFECTS
- Board effects (fire wheel trails, board on fire, lightning, wet trails, speed trails,
  shadows and more): Game = the level decides. On = in every level. Off = never.
- Body particles: the particles of each body type, in every level or never.
- Body look: the material of any body type on your skater, normal or end-game version.
  Extra layers on the body can be hidden.
- Find a shape near the board: lists the objects near the board, and each one can be hidden.

LEVELS
- Free skate in any level. Your save does not change: it is copied first and put back at the end.
  Objectives, challenges and level exits are turned off.
- Favorite levels (click the star), search, and "Show cutscenes" to also list cutscene scenes.
- Park editor: build on a grid with blocks (boxes, rails, ramps, quarter pipes, half pipes and more),
  and give each block a material from the game. Pipes keep you on the ramp in the air.
- The editor also works in the game's own levels. Blocks are saved for each level.
- "Write all game text to files" writes the game's text to the ShoeMod folder.

REPLAY
- Play back your last run. Free camera (W A S D, Q and E, Shift fast, Ctrl slow) with speed sliders.
- Go to the skater, camera paths with a path view (V) and points that you can drag.
- Lens for the replay only: zoom, focus, aperture and vignette, which can change along a path.
- Green screen and depth map in the replay.

Your designs and settings are in:
  %USERPROFILE%\AppData\LocalLow\<game company>\<game name>\ShoeMod


THINGS TO KNOW
- After a game update or a Steam file check, start the installer again.
  If the game files changed in the update, the installer tells you, and the mod needs an update.
- With custom shoe paint on, the game's own shoe colors do not show. Set it to off to see them.
- The board paint goes on each deck that uses the standard deck material, not only on your board.
- The game also gets the keys that you type into the hex box and the name box.
- Painted edges are rough, because the paint goes on triangles of the model.
- "No bails" uses the game's own god mode: all tricks land.
- With a long snap distance, grinds can also start on edges that you did not aim for.


FILES THAT THE INSTALLER CHANGES
  SkateStory_Data\StreamingAssets\aa\StandaloneWindows64\71e5e8adb29c34fc67055940f70bebef.bundle  (player, shoes)
  SkateStory_Data\StreamingAssets\aa\StandaloneWindows64\27a60a716d4f849e5bcb9d18b7d2322a.bundle  (costume shoes)
  SkateStory_Data\StreamingAssets\aa\StandaloneWindows64\29f7529af32fd767d5d46b3a4dfef808.bundle  (deck)
  SkateStory_Data\ScriptingAssemblies.json          (one line added)
  SkateStory_Data\RuntimeInitializeOnLoads.json     (one line added)
  SkateStory_Data\Managed\ShoeMod.dll               (new file)

The ZIP file contains no game files. The patch files hold only the changes, and the installer
makes the changed files from your own copy of the game.

Skate Story is made by Sam Eng and published by Devolver Digital. This is a fan-made mod.

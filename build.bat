@echo off
rem Builds ShoeMod.dll and ShoeModInstaller.exe on Windows.
rem Needs: Skate Story installed (for the reference DLLs) and the .NET Framework 4 compiler that comes with Windows.
rem Usage: build.bat "C:\Program Files (x86)\Steam\steamapps\common\Skate Story"
setlocal
set GAME=%~1
if "%GAME%"=="" set GAME=C:\Program Files (x86)\Steam\steamapps\common\Skate Story
set M=%GAME%\SkateStory_Data\Managed
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%M%\Assembly-CSharp.dll" (echo Game DLLs not found in "%M%" & exit /b 1)
if not exist build mkdir build
"%CSC%" /nologo /noconfig /nostdlib /target:library /optimize /out:build\ShoeMod.dll ^
 /r:"%M%\mscorlib.dll" /r:"%M%\System.dll" /r:"%M%\System.Core.dll" ^
 /r:"%M%\UnityEngine.CoreModule.dll" /r:"%M%\UnityEngine.IMGUIModule.dll" /r:"%M%\UnityEngine.TextRenderingModule.dll" ^
 /r:"%M%\UnityEngine.InputLegacyModule.dll" /r:"%M%\UnityEngine.UIModule.dll" /r:"%M%\UnityEngine.UI.dll" ^
 /r:"%M%\Unity.Postprocessing.Runtime.dll" /r:"%M%\UnityEngine.PhysicsModule.dll" /r:"%M%\Unity.Addressables.dll" ^
 /r:"%M%\Unity.ResourceManager.dll" /r:"%M%\UnityEngine.ParticleSystemModule.dll" /r:"%M%\Unity.Mathematics.dll" /r:"%M%\Assembly-CSharp.dll" ^
 source\ShoeMod.cs source\ShoeCore.cs source\ScreenTools.cs source\CameraTools.cs source\MoveTools.cs source\FxTools.cs source\WarpTools.cs source\SubTools.cs ^
 source\ReplayTools.cs source\ReplayLens.cs source\LookTools.cs source\EditorTools.cs source\FlipPose.cs source\TrickTools.cs source\TextDump.cs
if errorlevel 1 exit /b 1
"%CSC%" /nologo /target:exe /platform:anycpu /out:build\ShoeModInstaller.exe source\Installer.cs
if errorlevel 1 exit /b 1
echo Done: build\ShoeMod.dll and build\ShoeModInstaller.exe

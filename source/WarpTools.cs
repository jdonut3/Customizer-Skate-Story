// Levels tab of the editor: free skate in any level.
//  The level is loaded with the game's scene loader only. The game's story loader (SkateStoryFunctions.LoadLevel)
//  is not used, because it also moves the story forward and marks levels as visited and unlocked.
//  While free skate is on:
//   - The real save files are copied first. Saves during free skate are undone: the copy is put back at the end,
//     and also at the next start of the game, if the game closed during free skate.
//   - Story objects in the level are turned off: objectives, challenges, quests, and the doors, arches and shrines
//     that load another level.
//  "End free skate" goes to the main menu, puts the copy of the save back, and loads the real save again.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class WarpTools
{
    public string status = "";

    static string Dir { get { return Path.Combine(Path.Combine(Application.persistentDataPath, "ShoeMod"), "freeskate_save_copy"); } }
    static string Marker { get { return Path.Combine(Dir, "free_skate_is_on.txt"); } }
    bool checkedStart;
    public bool active;
    int endStep; float endTime;                      // 1 = going to the main menu, 2 = waiting for it, then the save is put back

    class Entry { public SkateLevel level; public string label; }
    class Chapter { public string name; public Color color; public bool open; public List<Entry> levels = new List<Entry>(); }
    List<Chapter> chapters = new List<Chapter>();
    Vector2 scroll; float nextList;

    // the story objects that are turned off in free skate (type names in the game code)
    static readonly string[] storyTypes = {
        "ObjectiveSetV2", "ObjectiveCanvasV2", "LineObjective", "LineObjectiveSet", "LineObjectiveUI", "FreeformObjective",
        "FreeformObjectiveSequence", "InfernalObjective", "InfernalObjectiveSet", "HintObjective", "SkateChallengeGoal",
        "SkateChallenge", "HighscoreQuest", "HighscoreQuestSheet", "IceDeliveryQuest", "AutoTrickQuestSystem", "QuestPopup",
        "LetterQuestHUD", "PrismGoal", "PrismScoreGoal", "SpecificTrickPrismGoal", "FloatingLetterPrismGoal", "EffigyPrismGoal",
        "ArchwayMessages" };
    // things that load another level: these are turned off, and their trigger shapes too
    static readonly string[] exitTypes = {
        "LevelDoor", "LevelShrine", "PedestalV2", "BlockerRift", "TVShrine", "SimpleLevel", "WorldMapV2Tunnel", "SubwayStationTeleport" };
    List<Type> story = new List<Type>(), exits = new List<Type>();
    float nextScan; int seenLoads = -1, scansLeft; public int turnedOff;

    // ---------- the level list ----------
    void BuildList()
    {
        chapters.Clear();
        SkateAssetLibrary lib = null;
        try { lib = SkateSingleton.main != null ? SkateSingleton.main.assetLib : null; } catch (Exception) { }
        if (lib == null) return;
        HashSet<SkateLevel> seen = new HashSet<SkateLevel>();
        if (lib.chapters != null)
            foreach (SkateStoryChapterAsset c in lib.chapters)
            {
                if (c == null || c.levels == null) continue;
                Chapter ch = new Chapter { name = string.IsNullOrEmpty(c.chapterName_english) ? c.name : c.chapterName_english, color = c.color };
                foreach (SkateStoryChapterAsset.LevelElement e in c.levels)
                    if (e != null && Usable(e.level) && seen.Add(e.level)) ch.levels.Add(new Entry { level = e.level, label = Name(e.level) });
                if (ch.levels.Count > 0) chapters.Add(ch);
            }
        Chapter other = new Chapter { name = "Other levels", color = Color.gray };
        List<SkateLevel> rest = new List<SkateLevel>();
        if (lib.levelList_Runtime != null) rest.AddRange(lib.levelList_Runtime);
        if (lib.levelList_Hubs != null) rest.AddRange(lib.levelList_Hubs);
        foreach (SkateLevel l in rest) if (Usable(l) && seen.Add(l)) other.levels.Add(new Entry { level = l, label = Name(l) });
        other.levels.Sort((a, b) => string.Compare(a.label, b.label, StringComparison.OrdinalIgnoreCase));
        if (other.levels.Count > 0) chapters.Add(other);
    }
    static bool Usable(SkateLevel l)
    {
        if (l == null || l.scene == null) return false;
        try { if (!l.scene.RuntimeKeyIsValid()) return false; } catch (Exception) { return false; }
        return l.type == SkateLevel.Type.Level || l.type == SkateLevel.Type.Tutorial;
    }
    static string Name(SkateLevel l)
    {
        string n = null;
        try { n = l.GetDisplayName_Localized(); } catch (Exception) { }
        return string.IsNullOrEmpty(n) || n == l.name ? l.name : n + "   (" + l.name + ")";
    }

    // ---------- free skate ----------
    static string[] SaveFiles()
    {
        return new string[] { GameDataMan.saveDataFilePath_full, GameDataMan.saveDataFilePathCompressed_full };
    }
    // copy the real save files (only at the start of free skate, not again for each level)
    bool Backup()
    {
        if (File.Exists(Marker)) return true;
        Directory.CreateDirectory(Dir);
        int n = 0;
        foreach (string f in SaveFiles())
            if (File.Exists(f)) { File.Copy(f, Path.Combine(Dir, Path.GetFileName(f)), true); n++; }
        if (n == 0) { status = "No save file found. Free skate did not start."; return false; }
        File.WriteAllText(Marker, "Free skate started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ". The files here are the real save from before.");
        return true;
    }
    // put the real save back and load it
    void RestoreSave()
    {
        foreach (string f in SaveFiles())
        {
            string b = Path.Combine(Dir, Path.GetFileName(f));
            if (File.Exists(b)) File.Copy(b, f, true);
        }
        File.Delete(Marker);
        try { SkateSingleton.main.dataMan.LoadDataFromDisk(); } catch (Exception) { }
    }
    void Go(SkateLevel l)
    {
        try
        {
            if (MultiScene.isLoading) { status = "The game is loading. Try again in a moment."; return; }
            if (!Backup()) return;       // first: the save at the scene change must not reach the copy
            active = true; endStep = 0;
            try { if (SkateSingleton.main.musicMan != null) SkateSingleton.main.musicMan.StopAndClear(); } catch (Exception) { }
            LevelInfo.SetLoadSpawnType(LevelInfo.LoadType.Default);
            try { SkateSingleton.main.loadFade.SetLoadingLevel(l); } catch (Exception) { }
            MultiScene.CURRENT_LOADING = l;
            MultiScene.LoadScene(l.scene);
            status = "Loading " + Name(l) + ". Free skate is on: your save does not change.";
            scansLeft = 4; nextScan = Time.unscaledTime + 1f;
        }
        catch (Exception e) { status = "Could not load the level: " + e.Message; }
    }
    void End()
    {
        if (MultiScene.isLoading) { status = "The game is loading. Try again in a moment."; return; }
        endStep = 1;
        try { SkateStoryFunctions.LoadMainMenu(); } catch (Exception e) { status = "Could not go to the main menu: " + e.Message; endStep = 0; return; }
        endStep = 2; endTime = Time.unscaledTime;
        status = "Going back to the main menu...";
    }

    public void Update(float now)
    {
        try
        {
            if (!checkedStart && SkateSingleton.main != null)
            {   // the game closed during free skate: put the real save back
                checkedStart = true;
                if (File.Exists(Marker) && !active) { RestoreSave(); status = "The game closed during free skate. Your real save is back."; }
            }
            if (endStep == 2 && !MultiScene.isLoading && Time.unscaledTime - endTime > 3f)
            {
                RestoreSave();
                active = false; endStep = 0;
                status = "Free skate is off. Your real save is loaded again.";
            }
            // a search for story objects is slow: only a few times after each level load, not all the time
            if (SceneLoads.count != seenLoads) { seenLoads = SceneLoads.count; scansLeft = 4; nextScan = now + 1f; }
            if (active && endStep == 0 && scansLeft > 0 && now >= nextScan && !MultiScene.isLoading)
            {
                TurnOffStory(); scansLeft--;
                nextScan = now + (scansLeft == 3 ? 2f : scansLeft == 2 ? 3f : 6f);     // at about 1, 3, 6 and 12 seconds
            }
        }
        catch (Exception e) { status = "Levels error: " + e.Message; }
    }

    void FindTypes()
    {
        if (story.Count > 0 || exits.Count > 0) return;
        System.Reflection.Assembly a = typeof(SkateLevel).Assembly;
        foreach (string n in storyTypes) { Type t = a.GetType(n); if (t != null) story.Add(t); }
        foreach (string n in exitTypes) { Type t = a.GetType(n); if (t != null) exits.Add(t); }
    }
    void TurnOffStory()
    {
        FindTypes();
        foreach (Type t in story)
            foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(t))
            {
                MonoBehaviour b = o as MonoBehaviour;
                if (b == null || !b.enabled) continue;
                b.enabled = false; turnedOff++;
                // objective markers and their screen parts: hide the object, unless it is the skater
                if (ThePlayer.instance == null || !b.transform.IsChildOf(ThePlayer.instance.transform)) b.gameObject.SetActive(false);
            }
        foreach (Type t in exits)
            foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(t))
            {
                MonoBehaviour b = o as MonoBehaviour;
                if (b == null || !b.enabled) continue;
                b.enabled = false; turnedOff++;
                foreach (Collider c in b.GetComponentsInChildren<Collider>(true)) if (c.isTrigger) c.enabled = false;
            }
    }

    // ---------- menu ----------
    public void Draw()
    {
        if (status.Length > 0) GUILayout.Label(status);
        float now = Time.unscaledTime;
        if (chapters.Count == 0 && now >= nextList) { nextList = now + 2f; BuildList(); }
        GUILayout.BeginHorizontal();
        GUILayout.Label(active ? "FREE SKATE IS ON.  Your real save does not change.  Story objects off: " + turnedOff
                               : "Click a level to skate there. Story objectives and level exits are turned off, and your save does not change.");
        if (active && GUILayout.Button("End free skate", GUILayout.Width(140))) End();
        GUILayout.EndHorizontal();
        GUILayout.Label("Some levels start with a scene or a talk. Some scripted events can still happen. End free skate to go back to your game.");
        if (chapters.Count == 0) { GUILayout.Label("The level list shows when the game has loaded."); return; }
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(500));
        foreach (Chapter ch in chapters)
        {
            Color old = GUI.color; GUI.color = Color.Lerp(Color.white, ch.color, 0.5f);
            if (GUILayout.Button((ch.open ? "v  " : ">  ") + ch.name + "   (" + ch.levels.Count + ")", GUILayout.Width(400))) ch.open = !ch.open;
            GUI.color = old;
            if (!ch.open) continue;
            int i = 0;
            while (i < ch.levels.Count)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                for (int k = 0; k < 3 && i < ch.levels.Count; k++, i++)
                    if (GUILayout.Button(ch.levels[i].label, GUILayout.Width(310))) Go(ch.levels[i].level);
                GUILayout.EndHorizontal();
            }
        }
        GUILayout.EndScrollView();
    }
}

// Counts the scene loads of the game, so that the slow searches run only after a load.
public static class SceneLoads
{
    public static int count;
    static bool hooked;
    public static void Hook()
    {
        if (hooked) return; hooked = true;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => count++;
    }
}

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
    public bool dirty;

    // ---------- favorite levels (saved by the level's asset name) ----------
    List<string> favs = new List<string>();
    string search = ""; bool searchFocus;
    bool favsOpen = true;
    bool showCut;                                    // also list cutscene and transition scenes
    public void Load(string line)
    {
        line = line.Trim();
        if (line == "cutscenes=1") showCut = true;
        if (line.StartsWith("fav=") && line.Length > 4) { string n = line.Substring(4); if (!favs.Contains(n)) favs.Add(n); }
    }
    public void Write(System.Text.StringBuilder sb) { if (showCut) sb.Append("cutscenes=1\n"); foreach (string n in favs) sb.Append("fav=").Append(n).Append('\n'); }
    void ToggleFav(SkateLevel l)
    {
        if (favs.Contains(l.name)) favs.Remove(l.name); else favs.Add(l.name);
        dirty = true;
    }
    // one level button with a star in front: click the star to add or remove the favorite
    void LevelButton(Entry en, float width)
    {
        bool fav = favs.Contains(en.level.name);
        Color old = GUI.color;
        if (fav) GUI.color = new Color(1f, 0.85f, 0.3f);
        if (GUILayout.Button(fav ? "\u2605" : "\u2606", GUILayout.Width(28))) ToggleFav(en.level);
        GUI.color = old;
        if (GUILayout.Button(en.label, GUILayout.Width(width))) Go(en.level);
    }

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
                    if (e != null && Usable(e.level, showCut) && seen.Add(e.level)) ch.levels.Add(new Entry { level = e.level, label = Name(e.level) });
                if (ch.levels.Count > 0) chapters.Add(ch);
            }
        Chapter other = new Chapter { name = "Other levels", color = Color.gray };
        List<SkateLevel> rest = new List<SkateLevel>();
        if (lib.levelList_Runtime != null) rest.AddRange(lib.levelList_Runtime);
        if (lib.levelList_Hubs != null) rest.AddRange(lib.levelList_Hubs);
        AddAll(lib.levelList, rest);
        try
        {   // the full level table of the game (not public)
            System.Reflection.FieldInfo fd = typeof(SkateAssetLibrary).GetField("levelDict_Runtime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            System.Collections.IDictionary d = fd != null ? fd.GetValue(fd.IsStatic ? null : lib) as System.Collections.IDictionary : null;
            if (d != null) AddAll(d.Values, rest);
        }
        catch (Exception) { }
        foreach (SkateLevel l in rest) if (Usable(l, showCut) && seen.Add(l)) other.levels.Add(new Entry { level = l, label = Name(l) });
        other.levels.Sort((a, b) => string.Compare(a.label, b.label, StringComparison.OrdinalIgnoreCase));
        if (other.levels.Count > 0) chapters.Add(other);
    }
    // the game's full level list: its type is not known here, so read any list of levels in it
    static void AddAll(object o, List<SkateLevel> to)
    {
        if (o == null) return;
        try
        {
            System.Collections.IEnumerable en = o as System.Collections.IEnumerable;
            if (en != null) { foreach (object x in en) { SkateLevel l = x as SkateLevel; if (l != null) to.Add(l); } return; }
            foreach (System.Reflection.FieldInfo f in o.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                if (typeof(System.Collections.IEnumerable).IsAssignableFrom(f.FieldType) && f.FieldType != typeof(string))
                {
                    System.Collections.IEnumerable e2 = f.GetValue(o) as System.Collections.IEnumerable;
                    if (e2 != null) foreach (object x in e2) { SkateLevel l = x as SkateLevel; if (l != null) to.Add(l); }
                }
        }
        catch (Exception) { }
    }
    static bool Usable(SkateLevel l, bool cut)
    {
        if (l == null || l.scene == null) return false;
        try { if (!l.scene.RuntimeKeyIsValid()) return false; } catch (Exception) { return false; }
        if (l.type == SkateLevel.Type.Level || l.type == SkateLevel.Type.Tutorial) return true;
        return cut && (l.type == SkateLevel.Type.Cutscene || l.type == SkateLevel.Type.Transition);
    }
    static string Name(SkateLevel l)
    {
        string n = null;
        try { n = l.GetDisplayName_Localized(); } catch (Exception) { }
        string s = string.IsNullOrEmpty(n) || n == l.name ? l.name : n + "   (" + l.name + ")";
        if (l.type == SkateLevel.Type.Cutscene) s += "  [cutscene]";
        else if (l.type == SkateLevel.Type.Transition) s += "  [transition]";
        return s;
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
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Write all game text to files", GUILayout.Width(220))) status = TextDump.Run();
        GUILayout.Label("(the game's text tables, and every text object in this level, to the ShoeMod folder)");
        GUILayout.EndHorizontal();
        if (chapters.Count == 0) { GUILayout.Label("The level list shows when the game has loaded."); return; }
        GUILayout.BeginHorizontal();
        GUILayout.Label("Search", GUILayout.Width(50));
        search = MiniText.Layout(ref searchFocus, search, 40, 300);
        if (search.Length > 0 && GUILayout.Button("Clear", GUILayout.Width(60))) search = "";
        GUILayout.Space(20);
        bool sc = GUILayout.Toggle(showCut, " Show cutscenes", GUILayout.Width(150));
        if (sc != showCut) { showCut = sc; dirty = true; BuildList(); }
        GUILayout.EndHorizontal();
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(440));
        if (search.Trim().Length > 0)
        {   // search: every level whose name has the text (in any chapter), in one list
            string q = search.Trim().ToLower();
            List<Entry> hits = new List<Entry>();
            foreach (Chapter ch in chapters) foreach (Entry en in ch.levels) if (en.label.ToLower().Contains(q) && !hits.Contains(en)) hits.Add(en);
            GUILayout.Label(hits.Count == 0 ? "No level has \"" + search.Trim() + "\" in its name." : hits.Count + (hits.Count == 1 ? " level found." : " levels found."));
            int hi = 0;
            while (hi < hits.Count)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                for (int k = 0; k < 3 && hi < hits.Count; k++, hi++) LevelButton(hits[hi], 280);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            return;
        }
        // favorites first
        List<Entry> fl = new List<Entry>();
        foreach (string n in favs)
            foreach (Chapter ch in chapters) { Entry en = ch.levels.Find(x => x.level.name == n); if (en != null) { fl.Add(en); break; } }
        Color o2 = GUI.color; GUI.color = new Color(1f, 0.9f, 0.5f);
        if (GUILayout.Button((favsOpen ? "v  " : ">  ") + "\u2605 Favorites   (" + fl.Count + ")", GUILayout.Width(400))) favsOpen = !favsOpen;
        GUI.color = o2;
        if (favsOpen)
        {
            if (fl.Count == 0) { GUILayout.BeginHorizontal(); GUILayout.Space(20); GUILayout.Label("No favorites yet. Click the star in front of a level to add it here."); GUILayout.EndHorizontal(); }
            int fi = 0;
            while (fi < fl.Count)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                for (int k = 0; k < 3 && fi < fl.Count; k++, fi++) LevelButton(fl[fi], 280);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
        }
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
                    LevelButton(ch.levels[i], 280);
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

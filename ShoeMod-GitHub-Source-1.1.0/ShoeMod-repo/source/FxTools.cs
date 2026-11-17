// Effects tab of the editor.
//  Board effects: the skater has effect objects that the game and the levels turn on and off (fire wheel trails,
//  board fire, lightning, wet trails and more). Each group can be left to the game, forced on, or forced off.
//  "On" makes a copy of the effect next to the original. The game's scripts do not know the copy, so they do not
//  stop it. "Off" turns off the drawing of the original.
//  Body particles: the game's "glass" body items can bring particles that come from the body (for example the smoke
//  and black fire of some levels). Each one can be left to the game, added in every level, or hidden.
//  Body look: the material of a glass item (its normal version, or its end-game version) on the body, in place of the
//  game's choice. Extra material layers on the body (for example an outline) can be hidden.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

public class FxTools
{
    public bool dirty;
    public string status = "";

    // ---------- board effect groups ----------
    class Group
    {
        public string label, help; public string[] names;
        public int mode;                                   // 0 = the game decides, 1 = always on, 2 = always off
        public List<Transform> roots = new List<Transform>();
        public List<GameObject> copies = new List<GameObject>();
        public List<Renderer> rends = new List<Renderer>();
        public Dictionary<Renderer, bool> oldEnabled = new Dictionary<Renderer, bool>();
        public bool liveNow;
        public Group(string label, string help, params string[] names) { this.label = label; this.help = help; this.names = names; }
    }
    List<Group> groups = new List<Group> {
        new Group("Fire wheel trails", "Fire lines behind the wheels", "WheelFX"),
        new Group("Board on fire", "Flames, feathers and light on the board", "ON FIRE VFX"),
        new Group("Lightning", "Lightning around the board", "Effect21"),
        new Group("Glow cloud", "Fog, particles and light around the board", "Effect6"),
        new Group("Burner trail", "Particles from the board", "Ride BurnerVFX"),
        new Group("Wet trails", "Water lines behind the board", "WetTrails"),
        new Group("Speed trails", "Short lines behind the board", "SpeedTrails"),
        new Group("Board trail", "One long line behind the board", "BoardTrail"),
        new Group("Manual trail", "Lines from the front truck", "ManualTrail"),
        new Group("Speed lines", "Lines around you at high speed", "SpeedLines"),
        new Group("Fast ride sparks", "Sparks at high speed", "fast ride sparks"),
        new Group("Goo", "Goo under the wheels", "VFX Ride Goo", "VFX Ride Goo Yellow", "VFX Ride YellowPad"),
        new Group("Board shadow", "The dark shapes under the board", "VolumeShadows", "ShadowCube", "ShadowCube (1)"),
        new Group("Wheel shadows", "Small dark spots under the wheels", "wheelcontactshadow 1", "wheelcontactshadow 2", "wheelcontactshadow 3", "wheelcontactshadow 4"),
    };

    // ---------- body particles ----------
    class BodyFx
    {
        public string id, label; public int mode;          // 0 = the game decides, 1 = always on, 2 = always off
        public string prefabName;                          // the name of the particle object, to find the game's copies
        public List<GameObject> made = new List<GameObject>();
        public bool liveNow;
    }
    List<BodyFx> body = new List<BodyFx>();
    Dictionary<string, int> savedBody = new Dictionary<string, int>();   // from the settings file, before the item list is known
    Dictionary<Renderer, bool> hiddenBody = new Dictionary<Renderer, bool>();
    static FieldInfo addedField;

    // ---------- body look ----------
    class Look { public string id, label; public Material normal, end; }
    List<Look> looks = new List<Look>();
    string lookId = ""; int lookVariant;                  // "" = the game decides; variant 0 = normal, 1 = end-game
    HashSet<string> hiddenLayers = new HashSet<string>();
    Dictionary<SkinnedMeshRenderer, List<Material>> removedLayers = new Dictionary<SkinnedMeshRenderer, List<Material>>();
    float nextLook; bool lookTouched;

    // ---------- the finder: things that are drawn near the board, each of which can be hidden ----------
    class Found { public string key, label; public float below; }
    List<Found> found = new List<Found>();
    HashSet<string> hiddenThings = new HashSet<string>();      // key = parent name + "/" + object name
    Dictionary<Renderer, bool> hiddenThingR = new Dictionary<Renderer, bool>();
    List<Renderer> thingCache = new List<Renderer>(); float nextThingScan;
    static string KeyOf(Renderer r) { Transform t = r.transform; return (t.parent != null ? t.parent.name : "") + "/" + t.name; }

    ThePlayer player; SkatePlayer sp; float nextFind, nextRetry;
    Vector2 scroll;

    // ---------- settings file ----------
    public void Load(string line)
    {
        line = line.Trim();
        if (line.StartsWith("fx.groups="))
        {
            foreach (string part in line.Substring(10).Split(','))
            {
                string[] kv = part.Split(':'); int m;
                if (kv.Length != 2 || !int.TryParse(kv[1], out m)) continue;
                foreach (Group g in groups) if (g.label == kv[0]) g.mode = Mathf.Clamp(m, 0, 2);
            }
        }
        else if (line.StartsWith("fx.bodies="))
        {
            savedBody.Clear();
            foreach (string part in line.Substring(10).Split(','))
            {
                string[] kv = part.Split(':'); int m;
                if (kv.Length == 2 && int.TryParse(kv[1], out m)) savedBody[kv[0]] = Mathf.Clamp(m, 0, 2);
            }
            foreach (BodyFx b in body) { int m; b.mode = savedBody.TryGetValue(b.id, out m) ? m : 0; }
        }
        else if (line.StartsWith("fx.look="))
        {
            string[] f = line.Substring(8).Split(',');
            lookId = f.Length > 0 ? f[0] : ""; int v; lookVariant = f.Length > 1 && int.TryParse(f[1], out v) ? v : 0;
            hiddenLayers.Clear();
            for (int i = 2; i < f.Length; i++) if (f[i].Length > 0) hiddenLayers.Add(f[i]);
        }
        else if (line.StartsWith("fx.hide="))
        {
            hiddenThings.Clear();
            foreach (string k in line.Substring(8).Split('|')) if (k.Length > 0) hiddenThings.Add(k);
        }
        else if (line.StartsWith("fx.body="))
        {   // the first version: a list of body particles that were on
            string[] f = line.Substring(8).Split(',');
            for (int i = 1; i < f.Length; i++) if (f[i].Length > 0 && !savedBody.ContainsKey(f[i])) savedBody[f[i]] = 1;
        }
    }
    public void Write(StringBuilder sb)
    {
        sb.Append("fx.groups=");
        for (int i = 0; i < groups.Count; i++) { if (i > 0) sb.Append(','); sb.Append(groups[i].label).Append(':').Append(groups[i].mode); }
        sb.Append('\n');
        sb.Append("fx.bodies=");
        bool first = true;
        if (body.Count > 0) { foreach (BodyFx b in body) if (b.mode != 0) { if (!first) sb.Append(','); sb.Append(b.id).Append(':').Append(b.mode); first = false; } }
        else foreach (KeyValuePair<string, int> kv in savedBody) if (kv.Value != 0) { if (!first) sb.Append(','); sb.Append(kv.Key).Append(':').Append(kv.Value); first = false; }
        sb.Append('\n');
        sb.Append("fx.look=").Append(lookId).Append(',').Append(lookVariant);
        foreach (string h in hiddenLayers) sb.Append(',').Append(h);
        sb.Append('\n');
        sb.Append("fx.hide=");
        bool f1 = true;
        foreach (string k in hiddenThings) { if (!f1) sb.Append('|'); sb.Append(k); f1 = false; }
        sb.Append('\n');
    }

    // ---------- each frame ----------
    public void Update(float now)
    {
        try
        {
            FindPlayer(now);
            if (body.Count == 0) ListBodyItems();
            KeepBody(now);
            if (now >= nextLook) { nextLook = now + 0.25f; KeepLook(); }
        }
        catch (Exception e) { status = "Effects error: " + e.Message; }
    }
    // The forcing is done in LateUpdate, after the game's own scripts set the effects for this frame.
    public void LateUpdate()
    {
        try
        {
            if (sp == null) return;
            foreach (Group g in groups) ApplyGroup(g);
            ApplyBodyOff();
            HideThings();
        }
        catch (Exception e) { status = "Effects error: " + e.Message; }
    }

    Transform BoardT()
    {
        if (sp == null) return null;
        foreach (Transform t in sp.GetComponentsInChildren<Transform>(false)) if (t.name == "TheSkateboard") return t;
        return sp.transform;
    }
    // Every drawn object within 2 m of the board, from the skater and from the level.
    void FindThings()
    {
        found.Clear();
        Transform b = BoardT();
        if (b == null) { status = "Ride the board, then press Find."; return; }
        Vector3 c = b.position;
        HashSet<string> seen = new HashSet<string>();
        foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
            Bounds bo = r.bounds;
            if (bo.size.magnitude > 6f) continue;                       // big level objects
            if ((bo.center - c).magnitude > 2f) continue;
            if (r is SkinnedMeshRenderer && r.transform.IsChildOf(player != null ? player.transform : r.transform)) continue;   // the body and the feet
            string k = KeyOf(r);
            if (!seen.Add(k)) continue;
            float below = c.y - bo.center.y;
            found.Add(new Found { key = k, below = below, label = k + (below > 0.02f ? "   (" + below.ToString("0.00") + " m below the board)" : "") });
        }
        found.Sort((x, y) => y.below.CompareTo(x.below));
        status = found.Count + " objects near the board. The ones lowest under the board are first.";
    }
    void HideThings()
    {
        float now = Time.unscaledTime;
        if (hiddenThings.Count > 0 && now >= nextThingScan)
        {
            nextThingScan = now + 2f;
            thingCache.Clear();
            Transform b = BoardT();
            if (b != null)
                foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                    if (r != null && hiddenThings.Contains(KeyOf(r)) && (r.bounds.center - b.position).magnitude < 4f) thingCache.Add(r);
        }
        foreach (Renderer r in thingCache)
        {
            if (r == null || !r.enabled) continue;
            if (!hiddenThings.Contains(KeyOf(r))) continue;
            if (!hiddenThingR.ContainsKey(r)) hiddenThingR[r] = true;
            r.enabled = false;
        }
        if (hiddenThingR.Count == 0) return;
        List<Renderer> back = new List<Renderer>();
        foreach (KeyValuePair<Renderer, bool> kv in hiddenThingR) if (kv.Key == null || !hiddenThings.Contains(KeyOf(kv.Key))) back.Add(kv.Key);
        foreach (Renderer r in back) { if (r != null) r.enabled = true; hiddenThingR.Remove(r); }
    }

    void FindPlayer(float now)
    {
        if (sp != null && player != null && now < nextFind) return;
        nextFind = now + 2f;
        ThePlayer p = null; SkatePlayer s = null;
        try { p = ThePlayer.instance; s = ThePlayer.SkatePlayer; } catch (Exception) { }
        if (p == player && s == sp) return;
        foreach (Group g in groups) { Restore(g); DestroyCopies(g); g.roots.Clear(); g.rends.Clear(); }
        RestoreBody();
        foreach (BodyFx b in body) b.made.Clear();
        player = p; sp = s;
        if (sp == null) return;
        // Only the effects of the board that you ride (the skater object). The other board copies (for grind poses
        // and for walking) are not used.
        foreach (Transform t in sp.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("ShoeMod")) continue;
            foreach (Group g in groups)
                for (int i = 0; i < g.names.Length; i++)
                    if (t.name == g.names[i]) { g.roots.Add(t); break; }
        }
        foreach (Group g in groups)
            foreach (Transform r in g.roots)
                g.rends.AddRange(r.GetComponentsInChildren<Renderer>(true));
    }

    void ApplyGroup(Group g)
    {
        g.liveNow = false;
        for (int i = 0; i < g.rends.Count && !g.liveNow; i++) { Renderer r = g.rends[i]; if (r != null && r.enabled && r.gameObject.activeInHierarchy) g.liveNow = true; }

        if (g.mode == 1)
        {
            g.copies.RemoveAll(o => o == null);
            if (g.copies.Count == 0) MakeCopies(g);
            foreach (GameObject c in g.copies) KeepPlaying(c);
            g.liveNow = g.copies.Count > 0 || g.liveNow;
        }
        else DestroyCopies(g);

        if (g.mode == 2)
            foreach (Renderer r in g.rends)
            {
                if (r == null || !r.enabled) continue;
                if (!g.oldEnabled.ContainsKey(r)) g.oldEnabled[r] = true;
                r.enabled = false;
            }
        else if (g.oldEnabled.Count > 0) Restore(g);
    }
    void MakeCopies(Group g)
    {
        foreach (Transform root in g.roots)
        {
            if (root == null) continue;
            GameObject c = UnityEngine.Object.Instantiate(root.gameObject, root.parent, false);
            c.name = "ShoeMod copy of " + root.name;
            c.SetActive(true);
            // the copy starts with the drawing on, also when the original is turned off
            foreach (Renderer r in c.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            g.copies.Add(c);
        }
    }
    static void KeepPlaying(GameObject c)
    {
        foreach (ParticleSystem p in c.GetComponentsInChildren<ParticleSystem>(false))
        {
            ParticleSystem.EmissionModule em = p.emission;
            if (!em.enabled) em.enabled = true;
            if (!p.isPlaying) p.Play(false);
        }
        foreach (TrailRenderer t in c.GetComponentsInChildren<TrailRenderer>(false))
        {
            t.emitting = true;
            if (t.time < 0.2f) t.time = 0.4f;
        }
    }
    static void DestroyCopiesList(List<GameObject> l)
    {
        foreach (GameObject o in l) if (o != null) UnityEngine.Object.Destroy(o);
        l.Clear();
    }
    void DestroyCopies(Group g) { if (g.copies.Count > 0) DestroyCopiesList(g.copies); }
    void Restore(Group g)
    {
        foreach (KeyValuePair<Renderer, bool> kv in g.oldEnabled) if (kv.Key != null) kv.Key.enabled = kv.Value;
        g.oldEnabled.Clear();
    }

    // ---------- body particles ----------
    void ListBodyItems()
    {
        SkateAssetLibrary lib = null;
        try { SkateSingleton m = SkateSingleton.main; if (m != null) lib = m.assetLib; } catch (Exception) { }
        if (lib == null || lib.allItems == null) return;
        foreach (SkateItemNode n in lib.allItems)
        {
            if (n == null || n.type != SkateItemNode.Type.Glass) continue;
            if (n.material != null || n.material2 != null)
            {
                string ll = n.name;
                try { string d0 = n.GetDisplayName_Localized(); if (!string.IsNullOrEmpty(d0)) ll = d0; } catch (Exception) { }
                looks.Add(new Look { id = n.name, label = ll, normal = n.material, end = n.material2 });
            }
            bool hasFx = false;
            try { hasFx = n.prefabAssetReference != null && n.prefabAssetReference.RuntimeKeyIsValid(); } catch (Exception) { }
            if (!hasFx) continue;
            string label = n.name;
            try { string d = n.GetDisplayName_Localized(); if (!string.IsNullOrEmpty(d)) label = d; } catch (Exception) { }
            int mode; savedBody.TryGetValue(n.name, out mode);
            body.Add(new BodyFx { id = n.name, label = label, mode = mode });
        }
        body.Sort((a, c) => string.Compare(a.label, c.label, StringComparison.OrdinalIgnoreCase));
        looks.Sort((a, c) => string.Compare(a.label, c.label, StringComparison.OrdinalIgnoreCase));
    }

    // ---------- body look ----------
    List<SkinnedMeshRenderer> BodyRenderers()
    {
        List<SkinnedMeshRenderer> l = new List<SkinnedMeshRenderer>();
        if (player == null) return l;
        if (player.playerLoadoutApplier != null && player.playerLoadoutApplier.bodyRenders != null)
            foreach (SkaterMesh sm in player.playerLoadoutApplier.bodyRenders)
                if (sm != null && sm.skinnedMeshRenderer != null && !l.Contains(sm.skinnedMeshRenderer)) l.Add(sm.skinnedMeshRenderer);
        if (player.finalSkaterMesh != null && player.finalSkaterMesh.skaterMeshRenderer != null && !l.Contains(player.finalSkaterMesh.skaterMeshRenderer))
            l.Add(player.finalSkaterMesh.skaterMeshRenderer);
        return l;
    }
    // The material that the game itself would use now.
    Material GameLook()
    {
        try
        {
            Loadout lo = player.playerLoadoutApplier.currentLoadout;
            SkateItemNode n = lo != null && lo.person != null && lo.person.Glass != null ? lo.person.Glass.SkateItem : null;
            if (n == null) return null;
            bool frag = false;
            try { frag = SkateSingleton.data.skull_fragile; } catch (Exception) { }
            return frag && n.material2 != null ? n.material2 : n.material;
        }
        catch (Exception) { return null; }
    }
    Material WantedLook()
    {
        if (lookId.Length == 0) return null;
        foreach (Look l in looks) if (l.id == lookId) return lookVariant == 1 && l.end != null ? l.end : l.normal;
        return null;
    }
    void KeepLook()
    {
        if (player == null) return;
        Material want = WantedLook();
        if (want == null && lookTouched) { want = GameLook(); }       // back to the game: put the game's material back once
        List<SkinnedMeshRenderer> rs = BodyRenderers();
        foreach (SkinnedMeshRenderer r in rs)
        {
            Material[] m = r.sharedMaterials;
            bool changed = false;
            if (want != null && m.Length > 0 && m[0] != want) { m[0] = want; changed = true; }
            // extra layers (slot 2 and after) that are hidden
            List<Material> keep = new List<Material>();
            List<Material> gone; removedLayers.TryGetValue(r, out gone);
            for (int i = 0; i < m.Length; i++)
            {
                if (i > 0 && m[i] != null && hiddenLayers.Contains(m[i].name)) { if (gone == null) { gone = new List<Material>(); removedLayers[r] = gone; } if (!gone.Contains(m[i])) gone.Add(m[i]); changed = true; continue; }
                keep.Add(m[i]);
            }
            if (gone != null)
                for (int i = gone.Count - 1; i >= 0; i--)
                    if (gone[i] == null || !hiddenLayers.Contains(gone[i].name)) { if (gone[i] != null && !keep.Contains(gone[i])) keep.Add(gone[i]); gone.RemoveAt(i); changed = true; }
            if (changed) r.sharedMaterials = keep.ToArray();
        }
        lookTouched = want != null && lookId.Length > 0;
        // a hit makes the body flash, and after the flash the game puts back the materials it saved: save ours there
        try
        {
            FinalSkaterMesh f = player.finalSkaterMesh;
            if (f != null && f.flicker != null && f.skaterMeshRenderer != null && f.flicker.materialOnDisable != null && !f.flicker.enabled)
                f.flicker.materialOnDisable.mats = f.skaterMeshRenderer.sharedMaterials;
        }
        catch (Exception) { }
    }
    List<string> CurrentLayers()
    {
        List<string> l = new List<string>();
        if (player == null || player.finalSkaterMesh == null || player.finalSkaterMesh.skaterMeshRenderer == null) return l;
        foreach (Material m in player.finalSkaterMesh.skaterMeshRenderer.sharedMaterials) if (m != null) l.Add(m.name);
        return l;
    }

    List<GameObject> GameBodyCopies()
    {
        if (player == null || player.playerLoadoutApplier == null) return null;
        if (addedField == null) addedField = typeof(PlayerLoadoutApplier).GetField("addedPrefabs", BindingFlags.NonPublic | BindingFlags.Instance);
        return addedField != null ? addedField.GetValue(player.playerLoadoutApplier) as List<GameObject> : null;
    }
    static bool SameObject(GameObject o, string prefabName)
    {
        if (o == null || prefabName == null) return false;
        string n = o.name;
        return n == prefabName || n == prefabName + "(Clone)" || n.StartsWith(prefabName + " (");
    }

    void KeepBody(float now)
    {
        if (player == null || player.playerLoadoutApplier == null) return;
        List<GameObject> game = GameBodyCopies();
        foreach (BodyFx b in body)
        {
            b.made.RemoveAll(o => o == null);
            if (b.mode != 1 && b.made.Count > 0) DestroyCopiesList(b.made);
            if (b.mode == 2 && b.prefabName == null && now >= nextRetry) { nextRetry = now + 1f; LearnName(b); }
            bool gameHasIt = false;
            if (game != null && b.prefabName != null) foreach (GameObject o in game) if (o != null && SameObject(o, b.prefabName)) { gameHasIt = true; break; }
            // "On": add the particles, but not a second time when the level already gave them
            if (b.mode == 1 && b.made.Count == 0 && !gameHasIt && now >= nextRetry) { nextRetry = now + 1f; AddBody(b); }
            b.liveNow = b.mode != 2 && (gameHasIt || b.made.Count > 0);
        }
    }
    void AddBody(BodyFx b)
    {
        SkateItemNode n = null;
        try { n = SkateItemNode.GetFromId(b.id); } catch (Exception) { }
        if (n == null) { status = "Could not find " + b.id + "."; return; }
        // The particles come from the body mesh that you see (the game's final skater mesh). The game uses its
        // body meshes for each mode, and some of these are not drawn, so particles from them can be missing.
        List<SkinnedMeshRenderer> rs = new List<SkinnedMeshRenderer>();
        if (player.finalSkaterMesh != null && player.finalSkaterMesh.skaterMeshRenderer != null) rs.Add(player.finalSkaterMesh.skaterMeshRenderer);
        else if (player.playerLoadoutApplier.bodyRenders != null)
            foreach (SkaterMesh sm in player.playerLoadoutApplier.bodyRenders) if (sm != null && sm.skinnedMeshRenderer != null) rs.Add(sm.skinnedMeshRenderer);
        foreach (SkinnedMeshRenderer smr in rs)
        {
            GameObject o = n.InstantiateItemInstant(smr.transform, false);
            if (o == null) continue;
            if (b.prefabName == null) b.prefabName = o.name.Replace("(Clone)", "").Trim();
            o.name = "ShoeMod " + b.id;
            foreach (ParticleSystem p in o.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.ShapeModule sh = p.shape;
                sh.skinnedMeshRenderer = smr;
            }
            b.made.Add(o);
        }
    }
    // The name of the particle object, from a short test copy.
    void LearnName(BodyFx b)
    {
        try
        {
            SkateItemNode n = SkateItemNode.GetFromId(b.id);
            if (n == null) return;
            GameObject o = n.InstantiateItemInstant(null, false);
            if (o == null) return;
            b.prefabName = o.name.Replace("(Clone)", "").Trim();
            UnityEngine.Object.Destroy(o);
        }
        catch (Exception) { }
    }
    void ApplyBodyOff()
    {
        List<GameObject> game = GameBodyCopies();
        HashSet<Renderer> keep = new HashSet<Renderer>();
        if (game != null)
            foreach (BodyFx b in body)
            {
                if (b.mode != 2 || b.prefabName == null) continue;
                foreach (GameObject o in game)
                {
                    if (!SameObject(o, b.prefabName)) continue;
                    foreach (Renderer r in o.GetComponentsInChildren<Renderer>(true))
                    {
                        keep.Add(r);
                        if (!r.enabled) continue;
                        if (!hiddenBody.ContainsKey(r)) hiddenBody[r] = true;
                        r.enabled = false;
                    }
                }
            }
        if (hiddenBody.Count == 0) return;
        List<Renderer> back = new List<Renderer>();
        foreach (KeyValuePair<Renderer, bool> kv in hiddenBody) if (!keep.Contains(kv.Key)) back.Add(kv.Key);
        foreach (Renderer r in back) { if (r != null) r.enabled = hiddenBody[r]; hiddenBody.Remove(r); }
    }
    void RestoreBody()
    {
        foreach (KeyValuePair<Renderer, bool> kv in hiddenBody) if (kv.Key != null) kv.Key.enabled = kv.Value;
        hiddenBody.Clear();
    }

    // ---------- menu ----------
    int ModeButtons(int m)
    {
        if (GUILayout.Toggle(m == 0, "Game", GUI.skin.button, GUILayout.Width(55)) && m != 0) m = 0;
        if (GUILayout.Toggle(m == 1, "On", GUI.skin.button, GUILayout.Width(40)) && m != 1) m = 1;
        if (GUILayout.Toggle(m == 2, "Off", GUI.skin.button, GUILayout.Width(40)) && m != 2) m = 2;
        return m;
    }
    public void Draw()
    {
        if (status.Length > 0) GUILayout.Label(status);
        if (sp == null) GUILayout.Label("The skater is not loaded yet.");
        GUILayout.Label("Game = the level decides, as normal.   On = in every level.   Off = never.   > = showing now.");
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(540));
        GUILayout.BeginHorizontal();

        GUILayout.BeginVertical(GUILayout.Width(480));
        GUILayout.Label("BOARD EFFECTS");
        foreach (Group g in groups)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label((g.liveNow ? "> " : "   ") + g.label, GUILayout.Width(140));
            int m = ModeButtons(g.mode);
            if (m != g.mode) { g.mode = m; dirty = true; }
            GUILayout.Label(g.help);
            GUILayout.EndHorizontal();
        }
        GUILayout.Label("Trails only show when you move.");
        if (GUILayout.Button("All board effects back to the game", GUILayout.Width(250)))
        { foreach (Group g in groups) g.mode = 0; dirty = true; }

        GUILayout.Space(14);
        GUILayout.Label("FIND A SHAPE NEAR THE BOARD");
        GUILayout.Label("Stand on the board where you see the shape, and press Find. Hide one item at a time to find it.");
        if (GUILayout.Button("Find", GUILayout.Width(120))) FindThings();
        foreach (Found f in found)
        {
            GUILayout.BeginHorizontal();
            bool hid = hiddenThings.Contains(f.key);
            GUILayout.Label(f.label, GUILayout.Width(380));
            if (GUILayout.Button(hid ? "Show" : "Hide", GUILayout.Width(55)))
            { if (hid) hiddenThings.Remove(f.key); else hiddenThings.Add(f.key); nextThingScan = 0f; dirty = true; }
            GUILayout.EndHorizontal();
        }
        if (hiddenThings.Count > 0)
        {
            GUILayout.Label("Hidden:");
            foreach (string k in new List<string>(hiddenThings))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("   " + k, GUILayout.Width(380));
                if (GUILayout.Button("Show", GUILayout.Width(55))) { hiddenThings.Remove(k); nextThingScan = 0f; dirty = true; }
                GUILayout.EndHorizontal();
            }
        }
        GUILayout.EndVertical();

        GUILayout.Space(16);
        GUILayout.BeginVertical(GUILayout.Width(470));
        GUILayout.Label("BODY PARTICLES");
        GUILayout.Label("Particles that come from the body. Your body type and some levels give them. The body look does not change.");
        if (body.Count == 0) GUILayout.Label("The list shows when the game has loaded.");
        foreach (BodyFx b in body)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label((b.liveNow ? "> " : "   ") + b.label, GUILayout.Width(200));
            int m = ModeButtons(b.mode);
            if (m != b.mode) { b.mode = m; dirty = true; }
            GUILayout.EndHorizontal();
        }
        if (GUILayout.Button("All body particles back to the game", GUILayout.Width(250)))
        { foreach (BodyFx b in body) b.mode = 0; dirty = true; }

        GUILayout.Space(14);
        GUILayout.Label("BODY LOOK");
        GUILayout.Label("The material on the body. End-game = the version the game uses after the end of the game.");
        GUILayout.BeginHorizontal();
        GUILayout.Label((lookId.Length == 0 ? "> " : "   ") + "The game decides", GUILayout.Width(200));
        if (GUILayout.Button("Game", GUILayout.Width(55)) && lookId.Length > 0) { lookId = ""; dirty = true; nextLook = 0f; }
        GUILayout.EndHorizontal();
        foreach (Look l in looks)
        {
            GUILayout.BeginHorizontal();
            bool sel = lookId == l.id;
            GUILayout.Label((sel ? "> " : "   ") + l.label, GUILayout.Width(200));
            if (l.normal != null && GUILayout.Toggle(sel && lookVariant == 0, "Normal", GUI.skin.button, GUILayout.Width(70)) && !(sel && lookVariant == 0)) { lookId = l.id; lookVariant = 0; dirty = true; nextLook = 0f; }
            if (l.end != null && GUILayout.Toggle(sel && lookVariant == 1, "End-game", GUI.skin.button, GUILayout.Width(80)) && !(sel && lookVariant == 1)) { lookId = l.id; lookVariant = 1; dirty = true; nextLook = 0f; }
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(8);
        GUILayout.Label("Layers on the body now (the first one is the main material):");
        List<string> layers = CurrentLayers();
        for (int i = 0; i < layers.Count; i++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("   " + (i + 1) + ". " + layers[i], GUILayout.Width(330));
            if (i > 0 && GUILayout.Button("Hide", GUILayout.Width(55))) { hiddenLayers.Add(layers[i]); dirty = true; nextLook = 0f; }
            GUILayout.EndHorizontal();
        }
        foreach (string h in new List<string>(hiddenLayers))
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("   Hidden: " + h, GUILayout.Width(330));
            if (GUILayout.Button("Show", GUILayout.Width(55))) { hiddenLayers.Remove(h); dirty = true; nextLook = 0f; }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
        GUILayout.EndScrollView();
    }
}

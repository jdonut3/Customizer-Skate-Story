// Screen tab additions: toggles that hide parts of the game's screen.
//  Subtitles: the floating story text, the two subtitle bars, the subtitles of cutscenes, and the subtitle of timed
//  sections. Character talk bubbles are not changed, because some of them ask for a choice.
//  Boss headers: the health bars of bosses and the timers of timed fights. The player's own health bar stays.
// Only the drawing is turned off: the game's own logic stays the same.
using System;
using System.Collections.Generic;
using UnityEngine;

public class SubTools
{
    public bool dirty;

    class Group
    {
        public string key, label; public string[] types; public bool hide;
        public List<Component> parts = new List<Component>();       // canvases, screen texts or world texts
        public Dictionary<Component, bool> changed = new Dictionary<Component, bool>();
    }
    List<Group> groups = new List<Group> {
        new Group { key = "subtitles", label = " Show the subtitles (story text, subtitle bars, cutscene subtitles)",
                    types = new[] { "AmbientSubtitle", "SharedSubtitle", "TimelineSubtitleMan" } },
        new Group { key = "bossbars", label = " Show the boss headers (boss health bars and fight timers)",
                    types = new[] { "BossBar", "DevilTimer", "DevilTimerText", "TrialTimerBar" } },
    };
    int seenLoads = -1, scansLeft; float nextScan;

    public void Load(string line)
    {
        line = line.Trim();
        foreach (Group g in groups) if (line.StartsWith(g.key + "=")) g.hide = line.Substring(g.key.Length + 1) == "0";
    }
    public void Write(System.Text.StringBuilder sb)
    {
        foreach (Group g in groups) sb.Append(g.key).Append('=').Append(g.hide ? "0" : "1").Append('\n');
    }

    // a search is slow: only a few times after each level load
    void Scan()
    {
        System.Reflection.Assembly a = typeof(SkateLevel).Assembly;
        // the player's own health bar is a BossBar too: keep it
        HashSet<GameObject> keep = new HashSet<GameObject>();
        foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(typeof(PlayerHealthMan)))
        {
            PlayerHealthMan p = o as PlayerHealthMan;
            if (p != null) keep.Add(p.gameObject);
        }
        foreach (Group g in groups)
        {
            if (!g.hide) continue;
            g.parts.Clear();
            foreach (string n in g.types)
            {
                Type t = a.GetType(n); if (t == null) continue;
                foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(t))
                {
                    Component c = o as Component;
                    if (c == null || !c.gameObject.scene.IsValid()) continue;   // not prefab assets, only objects in the game
                    bool mine = false;
                    foreach (GameObject k in keep) if (c.transform.IsChildOf(k.transform)) { mine = true; break; }
                    if (!mine) AddParts(g, c);
                }
            }
            if (g.key == "subtitles")
                try
                {   // the subtitle line of timed sections
                    foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(typeof(LinearSectionHUD)))
                    {
                        LinearSectionHUD h = o as LinearSectionHUD;
                        if (h != null && h.subtitle != null && h.gameObject.scene.IsValid()) AddParts(g, h.subtitle);
                    }
                }
                catch (Exception) { }
        }
    }
    // boss bars and timers often start with the fight, later than the level load: a light check of the active ones
    float nextBoss;
    void BossCheck()
    {
        Group g = groups[1];
        if (!g.hide) return;
        System.Reflection.Assembly a = typeof(SkateLevel).Assembly;
        foreach (string n in g.types)
        {
            Type t = a.GetType(n); if (t == null) continue;
            foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(t))
            {
                Component c = o as Component;
                if (c == null || c.GetComponentInParent<PlayerHealthMan>() != null) continue;
                AddParts(g, c);
            }
        }
    }
    static void AddParts(Group g, Component c)
    {
        Canvas[] cs = c.GetComponentsInChildren<Canvas>(true);
        if (cs.Length > 0) { foreach (Canvas k in cs) if (!g.parts.Contains(k)) g.parts.Add(k); return; }
        foreach (UnityEngine.UI.Graphic x in c.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) if (!g.parts.Contains(x)) g.parts.Add(x);
        foreach (Renderer r in c.GetComponentsInChildren<Renderer>(true)) if (!g.parts.Contains(r)) g.parts.Add(r);   // world text
    }
    static bool On(Component c) { Behaviour b = c as Behaviour; if (b != null) return b.enabled; Renderer r = c as Renderer; return r != null && r.enabled; }
    static void Set(Component c, bool v) { Behaviour b = c as Behaviour; if (b != null) { b.enabled = v; return; } Renderer r = c as Renderer; if (r != null) r.enabled = v; }

    public void LateUpdate()
    {
        float now = Time.unscaledTime;
        if (SceneLoads.count != seenLoads) { seenLoads = SceneLoads.count; scansLeft = 3; nextScan = now + 1f; }
        bool any = false; foreach (Group g in groups) any |= g.hide;
        if (any && scansLeft > 0 && now >= nextScan) { Scan(); scansLeft--; nextScan = now + 4f; }
        if (now >= nextBoss) { nextBoss = now + 3f; BossCheck(); }
        foreach (Group g in groups)
        {
            if (g.hide)
                foreach (Component c in g.parts)
                {
                    if (c == null || !On(c)) continue;
                    if (!g.changed.ContainsKey(c)) g.changed[c] = true;
                    Set(c, false);
                }
            else if (g.changed.Count > 0)
            {
                foreach (KeyValuePair<Component, bool> kv in g.changed) if (kv.Key != null) Set(kv.Key, kv.Value);
                g.changed.Clear();
            }
        }
    }

    public void Draw()
    {
        GUILayout.Space(8);
        foreach (Group g in groups)
        {
            bool n = GUILayout.Toggle(!g.hide, g.label);
            if (n == g.hide) { g.hide = !n; dirty = true; if (g.hide) { scansLeft = 3; nextScan = 0f; } }
        }
    }
}

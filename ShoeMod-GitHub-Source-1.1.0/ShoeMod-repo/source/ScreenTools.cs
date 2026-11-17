// Screen tab of the editor: moves or hides the trick popup and the combo counter, hides the speed bar,
// and finds and hides other things that show on the screen during a jump.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class ScreenTools
{
    // 0 = original place, 1 = bottom left, 2 = hidden
    public int trickMode, comboMode;
    public bool hideSpeed;
    public bool skipIntro = true;                          // skip the opening screen and the seizure warning at the game start
    public bool dirty;                                     // a setting changed: the owner saves the state file

    class HideItem { public string path; public bool ui; public Renderer r; public Graphic g; public float nextTry; }
    List<HideItem> hidden = new List<HideItem>();

    // finder
    class Found { public string path; public bool ui; public int seen; public Renderer r; public Graphic g; }
    bool armed; float armedUntil, nextSample;
    HashSet<int> groundSet = new HashSet<int>();
    Dictionary<int, Found> found = new Dictionary<int, Found>();
    List<Found> results = new List<Found>();
    int airSamples;
    public string finderStatus = "";
    Vector2 scroll;

    // the game objects (found again after a scene change)
    TrickNotificationV2 trick; RectTransform trickPanel; RectTransform[] trickParts;
    Vector2 tpAnchorMin, tpAnchorMax, tpPos; Vector2[] tpPivots; bool trickMoved;
    RectTransform comboGroup, comboRoot; Canvas comboCanvas; Vector2 cgPos; bool comboMoved;
    Canvas[] speedCanvases;
    float nextFind;

    // ---------- settings file ----------
    public void Load(string line)
    {
        line = line.Trim();
        int v;
        if (line.StartsWith("trick=") && int.TryParse(line.Substring(6), out v)) trickMode = Mathf.Clamp(v, 0, 2);
        else if (line.StartsWith("combo=") && int.TryParse(line.Substring(6), out v)) comboMode = Mathf.Clamp(v, 0, 2);
        else if (line == "speed=0") hideSpeed = true;
        else if (line == "intro=1") skipIntro = false;
        else if (line.StartsWith("hide=") && line.Length > 8)
        {
            string s = line.Substring(5); bool ui = s.StartsWith("U:");
            string path = s.Substring(2);
            if (!hidden.Exists(delegate (HideItem h) { return h.path == path; })) { HideItem h = new HideItem(); h.path = path; h.ui = ui; hidden.Add(h); }
        }
    }
    public void Write(StringBuilder sb)
    {
        sb.Append("trick=").Append(trickMode).Append('\n');
        sb.Append("combo=").Append(comboMode).Append('\n');
        sb.Append("speed=").Append(hideSpeed ? "0" : "1").Append('\n');
        sb.Append("intro=").Append(skipIntro ? "0" : "1").Append('\n');
        foreach (HideItem h in hidden) sb.Append("hide=").Append(h.ui ? "U:" : "R:").Append(h.path).Append('\n');
    }

    // ---------- helpers ----------
    static string PathOf(Transform t)
    {
        List<string> parts = new List<string>();
        for (int i = 0; t != null && i < 12; i++, t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }
    static bool Visible(Graphic g)
    {
        if (g == null || !g.enabled || !g.gameObject.activeInHierarchy) return false;
        Canvas c = g.canvas;
        if (c == null || !c.enabled) return false;
        CanvasRenderer cr = g.canvasRenderer;
        if (cr == null || cr.cull || cr.GetInheritedAlpha() < 0.05f) return false;
        return g.color.a > 0.05f;
    }
    bool IsMine(Transform t)
    {   // the parts that the Screen tab controls directly are not shown in the finder list
        if (trick != null && t.IsChildOf(trick.transform)) return true;
        if (comboRoot != null && t.IsChildOf(comboRoot)) return true;
        return false;
    }

    void FindGameObjects()
    {
        if (trick == null)
        {
            TrickNotificationV2[] a = UnityEngine.Object.FindObjectsByType<TrickNotificationV2>(FindObjectsSortMode.None);
            if (a.Length > 0)
            {
                trick = a[0]; trickMoved = false;
                trickPanel = trick.transform.Find("Panel") as RectTransform;
                if (trickPanel != null)
                {
                    List<RectTransform> l = new List<RectTransform>();
                    foreach (string n in new[] { "panel", "modifiers", "dmg" }) { RectTransform c = trickPanel.Find(n) as RectTransform; if (c != null) l.Add(c); }
                    trickParts = l.ToArray();
                }
            }
        }
        if (comboGroup == null)
        {
            ComboManV2[] a = UnityEngine.Object.FindObjectsByType<ComboManV2>(FindObjectsSortMode.None);
            if (a.Length > 0)
            {
                comboRoot = a[0].transform.Find("canvas") as RectTransform;
                comboGroup = comboRoot != null ? comboRoot.Find("GROUP") as RectTransform : null;
                comboCanvas = comboRoot != null ? comboRoot.GetComponent<Canvas>() : null;
                comboMoved = false;
            }
        }
        if (speedCanvases == null || speedCanvases.Length == 0 || speedCanvases[0] == null)
        {
            SpedometerV2[] a = UnityEngine.Object.FindObjectsByType<SpedometerV2>(FindObjectsSortMode.None);
            Canvas sc = a.Length > 0 ? a[0].GetComponentInChildren<Canvas>(true) : null;
            speedCanvases = sc != null ? new[] { sc } : null;
        }
    }

    // ---------- each frame ----------
    public void Update(float now)
    {
        if (now >= nextFind)
        {
            nextFind = now + 1f;
            try { FindGameObjects(); } catch (Exception e) { finderStatus = "Screen error: " + e.Message; }
            for (int i = 0; i < hidden.Count; i++)
            {
                HideItem h = hidden[i];
                if ((h.ui ? (UnityEngine.Object)h.g : h.r) != null || now < h.nextTry) continue;
                h.nextTry = now + 0.5f;
                GameObject go = GameObject.Find(h.path);
                if (go == null) continue;
                if (h.ui) h.g = go.GetComponent<Graphic>(); else h.r = go.GetComponent<Renderer>();
            }
        }
        if (armed) Sample(now);
    }

    public void LateUpdate()
    {
        try
        {
            ApplyTrick(); ApplyCombo();
            if (speedCanvases != null) for (int i = 0; i < speedCanvases.Length; i++) if (speedCanvases[i] != null && hideSpeed) speedCanvases[i].enabled = false;
            for (int i = 0; i < hidden.Count; i++)
            {
                HideItem h = hidden[i];
                if (h.r != null) h.r.forceRenderingOff = true;
                if (h.g != null && h.g.canvasRenderer != null) h.g.canvasRenderer.cull = true;
            }
        }
        catch (Exception e) { finderStatus = "Screen error: " + e.Message; }
    }

    void ApplyTrick()
    {
        if (trick == null || trickPanel == null) return;
        if (trickMode == 2 && trick.canvas != null) trick.canvas.enabled = false;      // the game sets it again in its next Update
        bool want = trickMode == 1;
        if (want && !trickMoved)
        {
            tpAnchorMin = trickPanel.anchorMin; tpAnchorMax = trickPanel.anchorMax; tpPos = trickPanel.anchoredPosition;
            tpPivots = new Vector2[trickParts.Length];
            for (int i = 0; i < trickParts.Length; i++) { tpPivots[i] = trickParts[i].pivot; trickParts[i].pivot = new Vector2(0f, trickParts[i].pivot.y); }
            trickPanel.anchorMin = Vector2.zero; trickPanel.anchorMax = Vector2.zero;
            trickMoved = true;
        }
        else if (!want && trickMoved)
        {
            trickPanel.anchorMin = tpAnchorMin; trickPanel.anchorMax = tpAnchorMax; trickPanel.anchoredPosition = tpPos;
            for (int i = 0; i < trickParts.Length; i++) trickParts[i].pivot = tpPivots[i];
            trickMoved = false;
        }
        // above the combo counter when that is in the bottom left too
        if (trickMoved) trickPanel.anchoredPosition = new Vector2(40f, comboMode == 1 ? 140f : 70f);
    }

    void ApplyCombo()
    {
        if (comboGroup == null) return;
        if (comboMode == 2 && comboCanvas != null) comboCanvas.enabled = false;
        bool want = comboMode == 1;
        if (want)
        {
            if (!comboMoved) { cgPos = comboGroup.anchoredPosition; comboMoved = true; }
            float w = comboRoot.rect.width;
            comboGroup.anchoredPosition = new Vector2(-w * 0.5f + 150f, cgPos.y);
        }
        else if (comboMoved) { comboGroup.anchoredPosition = cgPos; comboMoved = false; }
        if (comboMode != 2 && comboCanvas != null && !comboCanvas.enabled && wasComboHidden) comboCanvas.enabled = true;
        wasComboHidden = comboMode == 2;
    }
    bool wasComboHidden;

    // ---------- start screens ----------
    // The game's boot routine shows an opening screen (up to 8 seconds) and then a seizure warning (4 seconds),
    // and then it loads the first scene (the main menu). This stops that routine and does the same steps without the waits.
    BootScene boot; int bootState; float bootGiveUp = 20f;
    public void BootUpdate(float now)
    {
        if (bootState == 3 || !skipIntro) return;
        try
        {
            if (bootState == 0)
            {
                if (now > bootGiveUp) { bootState = 3; return; }
                BootScene[] a = UnityEngine.Object.FindObjectsByType<BootScene>(FindObjectsSortMode.None);
                if (a.Length == 0) return;
                boot = a[0];
                if (boot.firstScene == null || !boot.doLoad) { bootState = 3; return; }
                boot.StopAllCoroutines();
                if (boot.seizureWarning != null) boot.seizureWarning.SetActive(false);
                Cursor.visible = false;
                bootState = 1;
            }
            if (bootState == 1)
            {
                if (boot == null) { bootState = 3; return; }
                if (boot.seizureWarning != null) boot.seizureWarning.SetActive(false);
                if (SkateSingleton.main == null) return;          // the game systems are still loading
                if (boot.canvas != null) boot.canvas.SetActive(false);
                SkateSingleton.DONT_BLACK_SCREENFADE = true;
                MultiScene.LoadScene(boot.firstScene);
                bootState = 3;
            }
        }
        catch (Exception e) { finderStatus = "Start screen skip error: " + e.Message; bootState = 3; }
    }

    // ---------- finder ----------
    public void Arm(float now)
    {
        armed = true; armedUntil = now + 30f; nextSample = 0f; airSamples = 0;
        groundSet.Clear(); found.Clear(); results.Clear();
        finderStatus = "Searching. Close the editor (F6), then jump and do a trick 2 or 3 times within 30 seconds.";
    }
    void Sample(float now)
    {
        if (now > armedUntil)
        {
            armed = false;
            results = new List<Found>(found.Values);
            results.Sort(delegate (Found a, Found b) { return b.seen.CompareTo(a.seen); });
            if (results.Count > 30) results.RemoveRange(30, results.Count - 30);
            finderStatus = airSamples == 0 ? "No jump was seen. Click Find again and jump within 30 seconds."
                         : results.Count == 0 ? "Nothing new showed in the jumps." : "Found " + results.Count + " things that show in a jump. Hide the ones you do not want.";
            return;
        }
        if (now < nextSample) return;
        bool air = false;
        try { air = ThePlayer.AirborneOrTricking; } catch (Exception) { }
        nextSample = now + (air ? 0.15f : 0.3f);
        Vector3 pp = Vector3.zero;
        try { pp = ThePlayer.lastPlayerOnePosition; } catch (Exception) { }
        if (!air) groundSet.Clear();
        else airSamples++;
        // 3D objects near the player
        Renderer[] rs = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        for (int i = 0; i < rs.Length; i++)
        {
            Renderer r = rs[i];
            if (!r.enabled || !r.isVisible || !r.gameObject.activeInHierarchy) continue;
            if ((r.bounds.center - pp).sqrMagnitude > 100f) continue;          // within 10 m
            int id = r.GetInstanceID();
            if (!air) { groundSet.Add(id); continue; }
            if (groundSet.Contains(id) || IsMine(r.transform)) continue;
            Found f;
            if (!found.TryGetValue(id, out f)) { f = new Found(); f.r = r; f.path = PathOf(r.transform); found[id] = f; }
            f.seen++;
        }
        // screen items
        Graphic[] gs = UnityEngine.Object.FindObjectsByType<Graphic>(FindObjectsSortMode.None);
        for (int i = 0; i < gs.Length; i++)
        {
            Graphic g = gs[i];
            if (!Visible(g)) continue;
            int id = g.GetInstanceID();
            if (!air) { groundSet.Add(id); continue; }
            if (groundSet.Contains(id) || IsMine(g.transform)) continue;
            Found f;
            if (!found.TryGetValue(id, out f)) { f = new Found(); f.g = g; f.ui = true; f.path = PathOf(g.transform); found[id] = f; }
            f.seen++;
        }
    }
    bool IsHidden(string path) { return hidden.Exists(delegate (HideItem h) { return h.path == path; }); }
    void SetHidden(Found f, bool hide)
    {
        if (hide)
        {
            if (IsHidden(f.path)) return;
            HideItem h = new HideItem(); h.path = f.path; h.ui = f.ui; h.r = f.r; h.g = f.g; hidden.Add(h);
        }
        else
        {
            for (int i = hidden.Count - 1; i >= 0; i--)
                if (hidden[i].path == f.path)
                {
                    if (hidden[i].r != null) hidden[i].r.forceRenderingOff = false;
                    if (hidden[i].g != null && hidden[i].g.canvasRenderer != null) hidden[i].g.canvasRenderer.cull = false;
                    hidden.RemoveAt(i);
                }
        }
        dirty = true;
    }
    static string Short(string path)
    {
        string[] p = path.Split('/');
        int n = p.Length; string s = p[n - 1];
        if (n >= 2) s = p[n - 2] + " / " + s;
        if (n >= 3) s = p[n - 3] + " / " + s;
        return s;
    }

    // ---------- menu ----------
    int Choice(string label, int v, string[] names)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(230));
        for (int i = 0; i < names.Length; i++)
            if (GUILayout.Toggle(v == i, names[i], GUI.skin.button, GUILayout.Width(120)) && v != i) { v = i; dirty = true; }
        GUILayout.EndHorizontal();
        return v;
    }
    public void Draw()
    {
        GUILayout.Space(6);
        GUILayout.Label("Screen items of the game. The changes show immediately and are saved.");
        GUILayout.Space(4);
        string[] place = { "Original place", "Bottom left", "Hidden" };
        trickMode = Choice("Trick name (shows in a trick)", trickMode, place);
        comboMode = Choice("Combo counter", comboMode, place);
        bool si = GUILayout.Toggle(skipIntro, " Skip the opening screen and the seizure warning at the game start");
        if (si != skipIntro) { skipIntro = si; dirty = true; }
        bool hs = GUILayout.Toggle(!hideSpeed, " Show the speed bar");
        if (hs == hideSpeed) { hideSpeed = !hs; dirty = true; if (!hideSpeed && speedCanvases != null) foreach (Canvas c in speedCanvases) if (c != null) c.enabled = true; }
        if (trick == null && comboGroup == null) GUILayout.Label("The trick name and the combo counter are not loaded yet. Ride the board first.");

        GUILayout.Space(14);
        GUILayout.Label("Other things that show in a jump (for example, a marker on the ground)");
        GUILayout.Label("1. Click Find.  2. Close the editor and jump or do a trick 2 or 3 times.  3. Open the editor and hide the items you do not want.");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(armed ? "Searching..." : "Find", GUILayout.Width(120)) && !armed) Arm(Time.unscaledTime);
        GUILayout.Label(finderStatus);
        GUILayout.EndHorizontal();
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(250));
        if (results.Count > 0)
        {
            for (int i = 0; i < results.Count; i++)
            {
                Found f = results[i]; bool h = IsHidden(f.path);
                GUILayout.BeginHorizontal();
                bool nh = GUILayout.Toggle(h, " Hide", GUILayout.Width(70));
                if (nh != h) SetHidden(f, nh);
                GUILayout.Label((f.ui ? "[screen]  " : "[3D]  ") + Short(f.path) + "   (seen " + f.seen + "x)");
                GUILayout.EndHorizontal();
            }
        }
        else if (hidden.Count > 0)
        {
            GUILayout.Label("Hidden now:");
            for (int i = 0; i < hidden.Count; i++)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Show again", GUILayout.Width(100)))
                {
                    Found f = new Found(); f.path = hidden[i].path; f.r = hidden[i].r; f.g = hidden[i].g; f.ui = hidden[i].ui;
                    SetHidden(f, false); GUILayout.EndHorizontal(); break;
                }
                GUILayout.Label(Short(hidden[i].path));
                GUILayout.EndHorizontal();
            }
        }
        GUILayout.EndScrollView();
        if (results.Count > 0 && hidden.Count > 0 && GUILayout.Button("Show the list of hidden items", GUILayout.Width(220))) results.Clear();
    }
}

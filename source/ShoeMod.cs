// Shoe and Board Editor for Skate Story (ShoeMod), version 1.0.0.  Press F6 in the game.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public static class ShoeModLoader
{
    static bool started;
    public static void Init()
    {
        if (started) return;
        started = true;
        try
        {
            GameObject go = new GameObject("ShoeMod");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<ShoeModBehaviour>();
            Debug.Log("[ShoeMod] loaded (version 1.0.0)");
        }
        catch (Exception e) { Debug.Log("[ShoeMod] start failed: " + e); }
    }
}

class MeshInfo
{
    public Mesh mesh; public string name; public int tris; public bool canon, mirrorX;
    public int[] vidx;            // 3 vertex numbers for each paintable triangle
    public float[] pos;           // 9 numbers for each triangle
    public byte[] baseSlot;       // part of each triangle before paint (with or without the stripes)
    public byte[] rawBase, stripe; // part and stripe number as they came with the mesh
    public float[] fade;          // brightness factor for each triangle corner
    public int[] map;             // matching triangle of the editor mesh
    public Color32[] orig, buf;
}

class Painter
{
    public string kind, title;
    public string[] names; public int N;
    public string[] builtIn;                       // "name,hex,hex,..."
    public Color[] cur;
    public ShoeCore core;
    public Dictionary<int, MeshInfo> meshes = new Dictionary<int, MeshInfo>();
    public List<byte[]> undo = new List<byte[]>();
    public List<float> cloudPts; public List<byte> cloudSlots;
    public int[] shownViews; public int activeView;
    public Texture2D[] viewTex = new Texture2D[ShoeCore.Views];
    public byte[][] viewBytes = new byte[ShoeCore.Views][];
    public Color32[][] viewPix = new Color32[ShoeCore.Views][];
    public bool[] viewDirty = new bool[ShoeCore.Views];
    public int sel, tool = 1, brush = 8, lastPx, lastPy, mode = 1, badMeshes, found;   // mode: 0 = original colors, 1 = paint, 2 = white (glass)
    public bool mirror = true, dirty, needSave, glass, custom, grain, listOpen, stripes;
    public MeshInfo canonInfo;
    public float hue, sat, val;
    public string hexText = "", nameField = "", currentName = ""; public bool currentBuiltIn = true;
    public List<string> designs = new List<string>();
    public Vector2 listScroll;
}

class DeckRend { public MeshRenderer r; public int matIdx; public Material orig, clone; }

public class ShoeModBehaviour : MonoBehaviour
{
    static readonly string[] Quick = { "000000", "3A3A3A", "808080", "C8C8C8", "FFFFFF", "F5ECD7", "B38D3B", "7A4A21", "781F2B", "D0312D", "F2733A", "F4D35E",
                                       "3BA55D", "1F6F5C", "2EC4D6", "0548B0", "1B2A6B", "7D4FB3", "854D6F", "F28CB1" };
    const int ViewW = 640;
    Painter shoes, board, tab;
    Dictionary<int, Material> origMat = new Dictionary<int, Material>();
    Dictionary<int, bool> blockSet = new Dictionary<int, bool>();     // renderers that have our color settings
    List<SkinnedMeshRenderer> glassFeet = new List<SkinnedMeshRenderer>(), glassBodies = new List<SkinnedMeshRenderer>();
    Dictionary<int, DeckRend> decks = new Dictionary<int, DeckRend>();
    SkinnedMeshRenderer deckBody;
    ScreenTools screen = new ScreenTools(); bool screenTab;
    CameraTools camTools; bool camTab;
    MoveTools moveTools = new MoveTools(); bool moveTab;
    FxTools fxTools = new FxTools(); bool fxTab;
    WarpTools warpTools = new WarpTools(); bool warpTab;
    SubTools subTools = new SubTools();
    ReplayTools replay = new ReplayTools(); bool replayTab;
    LookTools looks = new LookTools();
    EditorTools editor = new EditorTools();
    public static bool ScarfHidden;
    bool hideScarf; List<SkinnedMeshRenderer> scarves = new List<SkinnedMeshRenderer>();
    MaterialPropertyBlock mpb;
    GameObject cursorHelper;
    bool open; int drag, focusField;
    float svHue = -1f; Painter svOwner;
    string status = "";
    float nextScan, nextDeckScan, nextAutoSave;
    Rect win = new Rect(20, 20, 1010, 100);
    Texture2D white, svTex, hueTex;
    string root;

    void Awake()
    {
        shoes = new Painter();
        shoes.kind = "shoes"; shoes.title = "Shoes"; shoes.N = 17;
        shoes.names = new[] { "Upper", "Sole side", "Ankle", "Stripe toe", "Stripe mid", "Stripe heel", "Toe cap", "Heel", "Sole bottom", "Side mark", "Extra 1", "Extra 2", "Extra 3", "Extra 4", "Extra 5", "Extra 6", "Extra 7" };
        string ex = ",E0483C,F2A33A,F4D35E,3BA55D,2E86C1,7D4FB3,854D6F";
        // name, stripes on (1) or off (0), then one color for each part
        shoes.builtIn = new[] {
            "Original,0,000000,D1D1D1,ABABAB,F5F5F5,F5F5F5,F5F5F5,000000,000000,D1D1D1,C9C9C9" + ex,
            "Tricolor,1,F5F5F5,B38D3B,ABABAB,781F2B,854D6F,0548B0,F5F5F5,F5F5F5,B38D3B,F5F5F5" + ex,
            "Black + white stripes,1,161616,F2F2F2,ABABAB,F2F2F2,F2F2F2,F2F2F2,161616,161616,F2F2F2,161616" + ex,
            "White + black stripes,1,F5F5F5,F5F5F5,ABABAB,161616,161616,161616,F5F5F5,F5F5F5,F5F5F5,F5F5F5" + ex,
            "Black + gum,0,161616,B38D3B,3A3A3A,F2F2F2,F2F2F2,F2F2F2,161616,161616,B38D3B,161616" + ex,
            "All white,0,F5F5F5,F5F5F5,ABABAB,F5F5F5,F5F5F5,F5F5F5,F5F5F5,F5F5F5,F5F5F5,F5F5F5" + ex };
        shoes.shownViews = new[] { 0, 1, 2, 3 }; shoes.custom = true;

        board = new Painter();
        board.kind = "board"; board.title = "Board"; board.N = 12;
        board.names = new[] { "Grip top", "Edge", "Bolts", "Extra 1", "Extra 2", "Extra 3", "Extra 4", "Extra 5", "Extra 6", "Extra 7", "Extra 8", "Extra 9" };
        string bx = ",F5F5F5,E0483C,F2A33A,F4D35E,3BA55D,2E86C1,7D4FB3,854D6F,0548B0";
        board.builtIn = new[] {
            "Original colors,0,0A0A0A,D05100,484848" + bx,
            "Black grip,0,161616,C9A66B,9A9A9A" + bx,
            "White grip,0,F2F2F2,C9A66B,3A3A3A" + bx,
            "Gum,0,B38D3B,7A4A21,161616" + bx,
            "Blue,0,0548B0,F5F5F5,F5F5F5" + bx };
        board.shownViews = new[] { 2 }; board.activeView = 2; board.brush = 10; board.mode = 0; shoes.mode = 0;
        tab = shoes;

        white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply();
        camTools = new CameraTools();
        hueTex = new Texture2D(1, 64); hueTex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < 64; y++) hueTex.SetPixel(0, y, Color.HSVToRGB(y / 63f, 1f, 1f));
        hueTex.Apply();
        svTex = new Texture2D(64, 64); svTex.wrapMode = TextureWrapMode.Clamp;
        mpb = new MaterialPropertyBlock();

        root = Path.Combine(Application.persistentDataPath, "ShoeMod");
        try { InitStorage(); } catch (Exception e) { Debug.Log("[ShoeMod] storage failed: " + e); }
        try
        {   // the game shows the mouse pointer while an AllowCursor component is active
            cursorHelper = new GameObject("ShoeModCursor");
            cursorHelper.transform.parent = transform;
            cursorHelper.SetActive(false);
            cursorHelper.AddComponent<AllowCursor>();
        }
        catch (Exception) { cursorHelper = null; }
    }

    // ---------- colors ----------
    static float Lin(float c) { return c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f); }
    static int B(float c) { return (int)Mathf.Round(Mathf.Clamp01(c) * 255f); }
    static string ToHex(Color c) { return B(c.r).ToString("X2") + B(c.g).ToString("X2") + B(c.b).ToString("X2"); }
    static bool FromHex(string s, out Color c)
    {
        c = Color.white;
        if (s == null) return false;
        s = s.Trim();
        if (s.StartsWith("#")) s = s.Substring(1);
        if (s.Length != 6) return false;
        int v;
        if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
        c = new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
        return true;
    }
    void SetColors(Painter p, string[] f, int first)
    {
        if (p.cur == null) { p.cur = new Color[p.N]; string[] d = p.builtIn[0].Split(','); for (int i = 0; i < p.N; i++) FromHex(d[i + 2], out p.cur[i]); }
        for (int i = 0; i < p.N; i++) { Color c; if (i + first < f.Length && FromHex(f[i + first], out c)) p.cur[i] = c; }
        p.dirty = true; SyncPicker(p);
    }
    string ColorLine(Painter p)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < p.N; i++) { if (i > 0) sb.Append(','); sb.Append(ToHex(p.cur[i])); }
        return sb.ToString();
    }
    void SyncPicker(Painter p) { p.hexText = ToHex(p.cur[p.sel]); Color.RGBToHSV(p.cur[p.sel], out p.hue, out p.sat, out p.val); }
    // Each change goes through here. A built-in design is read-only, so the first change makes a copy of it.
    void Fork(Painter p)
    {
        if (p.currentBuiltIn)
        {
            p.currentName = UniqueName(p, p.currentName + " copy"); p.currentBuiltIn = false; p.designs.Add(p.currentName); p.nameField = p.currentName;
            status = "Made a new design: " + p.currentName;
        }
        p.needSave = true;
    }
    void Touch(Painter p)
    {
        Fork(p);
        if (!p.custom && !p.glass) { p.custom = true; nextDeckScan = 0f; nextScan = 0f; }     // a paint or color change turns the custom paint on
    }
    void SetSel(Painter p, Color c, bool fromHsv)
    {
        c.a = 1f;
        if (ToHex(c) == ToHex(p.cur[p.sel])) return;
        p.cur[p.sel] = c; p.dirty = true; p.hexText = ToHex(c); Touch(p);
        if (!fromHsv) Color.RGBToHSV(c, out p.hue, out p.sat, out p.val);
    }

    // ---------- designs (files) ----------
    string Dir(Painter p) { return Path.Combine(root, p.kind); }
    string FileOf(Painter p, string name) { return Path.Combine(Dir(p), name + ".txt"); }
    string UniqueName(Painter p, string want)
    {
        string n = want; int k = 2;
        while (p.designs.Contains(n) || IsBuiltIn(p, n)) { n = want + " " + k; k++; }
        return n;
    }
    bool IsBuiltIn(Painter p, string n) { for (int i = 0; i < p.builtIn.Length; i++) if (p.builtIn[i].StartsWith(n + ",")) return true; return false; }
    void InitStorage()
    {
        bool fresh = !Directory.Exists(root);
        Directory.CreateDirectory(Dir(shoes)); Directory.CreateDirectory(Dir(board));
        if (fresh) MigrateOld();
        foreach (Painter p in new[] { shoes, board })
        {
            foreach (string f in Directory.GetFiles(Dir(p), "*.txt")) { string n = Path.GetFileNameWithoutExtension(f); if (!p.designs.Contains(n)) p.designs.Add(n); }
            p.designs.Sort();
        }
        string sShoes = null, sBoard = null;
        string st = Path.Combine(root, "state.txt");
        if (File.Exists(st))
            foreach (string line in File.ReadAllLines(st))
            {
                if (line.StartsWith("shoes=")) sShoes = line.Substring(6);
                if (line.StartsWith("board=")) sBoard = line.Substring(6);
                if (line.Trim() == "scarf=0") hideScarf = true;
                screen.Load(line);
                subTools.Load(line);
                replay.Load(line);
                looks.Load(line);
                warpTools.Load(line);
                editor.LoadSettings(line);
                camTools.Load(line);
                moveTools.Load(line);
                fxTools.Load(line);
            }
        LoadByState(shoes, sShoes); LoadByState(board, sBoard);
    }
    void LoadByState(Painter p, string s)
    {
        bool user = s != null && s.StartsWith("U:"); string n = s != null && s.Length > 2 ? s.Substring(2) : null;
        if (user && p.designs.Contains(n)) Select(p, n, false, false);
        else
        {
            int bi = 0; if (n != null) for (int i = 0; i < p.builtIn.Length; i++) if (p.builtIn[i].StartsWith(n + ",")) bi = i;
            Select(p, p.builtIn[bi].Split(',')[0], true, false);
            if (s == null) p.custom = false;     // first start: the game look stays until the player changes something
        }
    }
    void SaveState()
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("shoes=").Append(shoes.currentBuiltIn ? "B:" : "U:").Append(shoes.currentName).Append('\n');
            sb.Append("board=").Append(board.currentBuiltIn ? "B:" : "U:").Append(board.currentName).Append('\n');
            sb.Append("scarf=").Append(hideScarf ? "0" : "1").Append('\n');
            screen.Write(sb);
            subTools.Write(sb);
            replay.Write(sb);
            looks.Write(sb);
            warpTools.Write(sb);
            editor.WriteSettings(sb);
            camTools.Write(sb);
            moveTools.Write(sb);
            fxTools.Write(sb);
            File.WriteAllText(Path.Combine(root, "state.txt"), sb.ToString());
        }
        catch (Exception e) { status = "Save failed: " + e.Message; }
    }
    void MigrateOld()
    {   // files of versions 1 and 2
        string old = Application.persistentDataPath;
        string cf = Path.Combine(old, "ShoeMod_current.txt");
        if (File.Exists(cf))
        {
            string[] lines = File.ReadAllLines(cf);
            if (lines.Length > 0) WriteOld(lines[0].Split(','), Path.Combine(old, "ShoeMod_layout.txt"), "My shoes", lines.Length > 1 && lines[1].Trim() == "glass=1");
            File.WriteAllText(Path.Combine(root, "state.txt"), "shoes=U:My shoes\n");
        }
        string pf = Path.Combine(old, "ShoeMod_presets.txt");
        if (File.Exists(pf))
            foreach (string line in File.ReadAllLines(pf))
            {
                string[] f = line.Trim().Split(',');
                if (f.Length >= 10 && f[0].Length > 0 && f[0] != "My shoes") WriteOld(f, Path.Combine(old, "ShoeMod_layout_" + f[0] + ".txt"), f[0], false);
            }
    }
    void WriteOld(string[] f, string layoutFile, string name, bool glass)
    {
        StringBuilder sb = new StringBuilder("colors=");
        for (int i = 1; i < f.Length; i++) { if (i > 1) sb.Append(','); sb.Append(f[i]); }
        sb.Append("\nglass=").Append(glass ? "1" : "0").Append("\ncustom=1\n");
        if (File.Exists(layoutFile)) foreach (string l in File.ReadAllLines(layoutFile)) if (l.Trim().Length > 0) sb.Append("L ").Append(l.Trim()).Append('\n');
        File.WriteAllText(Path.Combine(Dir(shoes), name + ".txt"), sb.ToString());
    }
    void Save(Painter p)
    {
        p.needSave = false;
        if (p.currentBuiltIn) return;
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("v=4\n");
            sb.Append("colors=").Append(ColorLine(p)).Append('\n');
            sb.Append("stripes=").Append(p.stripes ? "1" : "0").Append('\n');
            sb.Append("glass=").Append(p.glass ? "1" : "0").Append('\n');
            sb.Append("custom=").Append(p.custom ? "1" : "0").Append('\n');
            sb.Append("grain=").Append(p.grain ? "1" : "0").Append('\n');
            ShoeCore c = p.core;
            if (c != null)
            {
                float sx = c.maxx - c.minx, sy = c.maxy - c.miny, sz = c.maxz - c.minz;
                for (int t = 0; t < c.triCount; t++)
                {
                    if (c.slot[t] == c.baseSlot[t]) continue;
                    sb.Append("L ").Append((int)Mathf.Round((c.cen[3 * t] - c.minx) / sx * 10000f)).Append(',')
                      .Append((int)Mathf.Round((c.cen[3 * t + 1] - c.miny) / sy * 10000f)).Append(',')
                      .Append((int)Mathf.Round((c.cen[3 * t + 2] - c.minz) / sz * 10000f)).Append(',').Append((int)c.slot[t]).Append('\n');
                }
            }
            else if (p.cloudPts != null)     // the mesh is not loaded yet: keep the paint that waits
                for (int i = 0; i < p.cloudSlots.Count; i++)
                    sb.Append("L ").Append((int)Mathf.Round(p.cloudPts[3 * i] * 10000f)).Append(',').Append((int)Mathf.Round(p.cloudPts[3 * i + 1] * 10000f)).Append(',')
                      .Append((int)Mathf.Round(p.cloudPts[3 * i + 2] * 10000f)).Append(',').Append((int)p.cloudSlots[i]).Append('\n');
            File.WriteAllText(FileOf(p, p.currentName), sb.ToString());
        }
        catch (Exception e) { status = "Save failed: " + e.Message; }
    }
    void Select(Painter p, string name, bool builtIn, bool saveFirst)
    {
        if (saveFirst && p.needSave) Save(p);
        p.undo.Clear(); p.needSave = false;
        p.currentName = name; p.currentBuiltIn = builtIn; p.nameField = name;
        p.glass = false; p.custom = true; p.grain = false; p.stripes = false; bool v4 = false;
        List<float> pts = new List<float>(); List<byte> sl = new List<byte>();
        if (builtIn)
        {
            for (int i = 0; i < p.builtIn.Length; i++) if (p.builtIn[i].StartsWith(name + ",")) { string[] f = p.builtIn[i].Split(','); p.stripes = f[1] == "1"; SetColors(p, f, 2); }
        }
        else
        {
            try
            {
                foreach (string raw in File.ReadAllLines(FileOf(p, name)))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("colors=")) SetColors(p, line.Substring(7).Split(','), 0);
                    else if (line == "v=4") v4 = true;
                    else if (line == "stripes=1") p.stripes = true;
                    else if (line == "glass=1") p.glass = true;
                    else if (line == "custom=0") p.custom = false;
                    else if (line == "grain=1") p.grain = true;
                    else if (line.StartsWith("L "))
                    {
                        string[] f = line.Substring(2).Split(','); int a, b, c, s;
                        if (f.Length == 4 && int.TryParse(f[0], out a) && int.TryParse(f[1], out b) && int.TryParse(f[2], out c) && int.TryParse(f[3], out s) && s >= 0 && s < p.N)
                        { pts.Add(a / 10000f); pts.Add(b / 10000f); pts.Add(c / 10000f); sl.Add((byte)s); }
                    }
                }
            }
            catch (Exception e) { status = "Load failed: " + e.Message; }
        }
        if (p.cur == null) SetColors(p, p.builtIn[0].Split(','), 2);
        if (!builtIn && !v4 && p == shoes)
        {   // a design of an older version: the stripes were always on, and "Side mark" was not a part
            p.stripes = true;
            for (int i = p.N - 1; i > 9; i--) p.cur[i] = p.cur[i - 1];
            p.cur[9] = p.cur[0];
            for (int i = 0; i < sl.Count; i++) if (sl[i] >= 9 && sl[i] < p.N - 1) sl[i]++;
            SyncPicker(p); p.needSave = true;
        }
        RefreshBase(p);
        p.cloudPts = pts; p.cloudSlots = sl;
        if (p.core != null) ApplyCloud(p);
        p.dirty = true; p.listOpen = false;
        nextScan = 0f; nextDeckScan = 0f;
        if (saveFirst) SaveState();
    }
    // The part of each triangle before paint. With the stripes on, the stripe triangles are the stripe parts.
    static byte Eff(Painter p, MeshInfo mi, int t) { return (p.stripes && mi.stripe != null && mi.stripe[t] > 0) ? (byte)(2 + mi.stripe[t]) : mi.rawBase[t]; }
    void RefreshBase(Painter p)
    {
        foreach (KeyValuePair<int, MeshInfo> kv in p.meshes) { MeshInfo mi = kv.Value; if (mi.rawBase != null) for (int t = 0; t < mi.tris; t++) mi.baseSlot[t] = Eff(p, mi, t); }
        MeshInfo c = p.canonInfo;
        if (c != null && p.core != null)
            for (int t = 0; t < c.tris; t++)
            {
                byte nb = Eff(p, c, t); bool painted = p.core.slot[t] != p.core.baseSlot[t];
                p.core.baseSlot[t] = nb; if (!painted) p.core.slot[t] = nb;
            }
        p.dirty = true;
    }
    void ApplyCloud(Painter p)
    {
        ShoeCore core = p.core;
        if (core == null || p.cloudPts == null) return;
        core.ResetLayout();
        int n = p.cloudSlots.Count;
        if (n > 0)
        {
            float[] q = new float[n * 3];
            for (int i = 0; i < n; i++)
            {
                q[3 * i] = core.minx + p.cloudPts[3 * i] * (core.maxx - core.minx);
                q[3 * i + 1] = core.miny + p.cloudPts[3 * i + 1] * (core.maxy - core.miny);
                q[3 * i + 2] = core.minz + p.cloudPts[3 * i + 2] * (core.maxz - core.minz);
            }
            PointGrid g = new PointGrid(q);
            for (int t = 0; t < core.triCount; t++)
            {
                int j = g.Nearest(core.cen[3 * t], core.cen[3 * t + 1], core.cen[3 * t + 2], 0.006f);
                if (j >= 0) core.slot[t] = p.cloudSlots[j];
            }
        }
        p.cloudPts = null; p.cloudSlots = null; p.dirty = true;
    }

    // ---------- meshes ----------
    void Register(Painter p, int id, Mesh m, string name, int[] tr, bool mirrorX)
    {
        MeshInfo mi = new MeshInfo();
        mi.mesh = m; mi.name = name; mi.mirrorX = mirrorX;
        p.meshes[id] = mi;
        if (!m.isReadable) { p.badMeshes++; return; }
        Vector3[] v = m.vertices; Color32[] c = m.colors32;
        if (v == null || c == null || tr == null || v.Length != c.Length || tr.Length < 3) { p.badMeshes++; return; }
        // each paintable triangle must have its own 3 vertices (the mod files are made this way)
        if (tr[1] != tr[0] + 1 || tr[2] != tr[0] + 2 || tr[tr.Length - 1] != tr[0] + tr.Length - 1) { p.badMeshes++; return; }
        int tris = tr.Length / 3;
        mi.tris = tris; mi.vidx = tr; mi.pos = new float[tris * 9]; mi.baseSlot = new byte[tris]; mi.rawBase = new byte[tris]; mi.fade = new float[tris * 3];
        if (p == shoes) mi.stripe = new byte[tris];
        mi.orig = c; mi.buf = (Color32[])c.Clone();
        float maxAnkle = 1f;
        for (int t = 0; t < tris; t++)
        {
            Color32 c0 = c[tr[3 * t]];
            int s;
            if (p == shoes) { s = c0.a & 15; if (s >= p.N) s = 0; int st = c0.a >> 4; mi.stripe[t] = (byte)(st <= 3 ? st : 0); }
            else s = (c0.b >= c0.r && c0.b >= c0.g) ? 0 : (c0.r >= c0.g ? 1 : 2);        // blue = grip top, red = edge, green = bolts
            mi.rawBase[t] = (byte)s;
            for (int k = 0; k < 3; k++)
            {
                int i = tr[3 * t + k]; Vector3 q = v[i];
                mi.pos[9 * t + 3 * k] = q.x; mi.pos[9 * t + 3 * k + 1] = q.y; mi.pos[9 * t + 3 * k + 2] = q.z;
                float f = 1f;
                if (p == shoes && s == 2) { f = Mathf.Max(c[i].r, Mathf.Max(c[i].g, c[i].b)); if (f > maxAnkle) maxAnkle = f; }
                mi.fade[3 * t + k] = f;
            }
        }
        for (int t = 0; t < tris; t++) mi.baseSlot[t] = Eff(p, mi, t);
        if (p == shoes) for (int t = 0; t < tris; t++) if (mi.rawBase[t] == 2) for (int k = 0; k < 3; k++) mi.fade[3 * t + k] /= maxAnkle;   // keep the ankle fade
        if (p.core == null && (p == board || name == "FootLMesh"))
        {
            p.core = new ShoeCore(mi.pos, (byte[])mi.baseSlot.Clone(), ViewW);
            mi.canon = true; p.canonInfo = mi;
            for (int k = 0; k < ShoeCore.Views; k++)
            {
                p.viewTex[k] = new Texture2D(p.core.vw[k], p.core.vh[k]);
                p.viewTex[k].filterMode = FilterMode.Bilinear; p.viewTex[k].wrapMode = TextureWrapMode.Clamp;
                p.viewBytes[k] = new byte[p.core.vw[k] * p.core.vh[k] * 4]; p.viewPix[k] = new Color32[p.core.vw[k] * p.core.vh[k]];
            }
            ApplyCloud(p);
        }
        p.dirty = true;
    }
    void ApplyColors(Painter p)
    {
        p.dirty = false;
        for (int k = 0; k < ShoeCore.Views; k++) p.viewDirty[k] = true;
        int N = p.N; ShoeCore core = p.core;
        byte[] lr = new byte[N], lg = new byte[N], lb = new byte[N];
        for (int i = 0; i < N; i++) { lr[i] = (byte)B(Lin(p.cur[i].r)); lg[i] = (byte)B(Lin(p.cur[i].g)); lb[i] = (byte)B(Lin(p.cur[i].b)); }
        List<int> dead = null;
        foreach (KeyValuePair<int, MeshInfo> kv in p.meshes)
        {
            MeshInfo mi = kv.Value;
            if (mi.mesh == null) { if (dead == null) dead = new List<int>(); dead.Add(kv.Key); continue; }
            if (mi.buf == null) continue;
            if (p.mode == 0) { mi.mesh.colors32 = mi.orig; continue; }
            if (p.mode == 2)
            {   // the body mesh has white vertex colors, so the glass material gets the same here
                Color32 w = new Color32(255, 255, 255, 255);
                for (int i = 0; i < mi.vidx.Length; i++) mi.buf[mi.vidx[i]] = w;
                mi.mesh.colors32 = mi.buf; continue;
            }
            if (!mi.canon && mi.map == null && core != null) mi.map = core.MapMesh(mi.pos, mi.mirrorX);
            for (int t = 0; t < mi.tris; t++)
            {
                int s = mi.baseSlot[t];
                if (core != null)
                {
                    if (mi.canon) s = core.slot[t];
                    else if (mi.map != null) { int j = mi.map[t]; if (core.slot[j] != core.baseSlot[j]) s = core.slot[j]; }
                }
                for (int k = 0; k < 3; k++)
                {
                    int i = mi.vidx[3 * t + k]; float f = mi.fade[3 * t + k]; byte a = mi.orig[i].a;
                    mi.buf[i] = f >= 0.999f ? new Color32(lr[s], lg[s], lb[s], a) : new Color32((byte)(lr[s] * f + 0.5f), (byte)(lg[s] * f + 0.5f), (byte)(lb[s] * f + 0.5f), a);
                }
            }
            mi.mesh.colors32 = mi.buf;
        }
        if (dead != null) foreach (int id in dead) p.meshes.Remove(id);
    }
    void UpdateView(Painter p, int k)
    {
        p.viewDirty[k] = false;
        if (p.core == null) return;
        byte[] pal = new byte[p.N * 3];
        for (int i = 0; i < p.N; i++) { pal[3 * i] = (byte)B(p.cur[i].r); pal[3 * i + 1] = (byte)B(p.cur[i].g); pal[3 * i + 2] = (byte)B(p.cur[i].b); }
        byte[] b = p.viewBytes[k]; Color32[] px = p.viewPix[k];
        p.core.Colorize(k, b, pal);
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(b[4 * i], b[4 * i + 1], b[4 * i + 2], 255);
        p.viewTex[k].SetPixels32(px); p.viewTex[k].Apply();
    }

    void ScanShoes()
    {
        Painter p = shoes; p.found = 0;
        SkinnedMeshRenderer[] all = UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        List<SkinnedMeshRenderer> bodies = new List<SkinnedMeshRenderer>();
        for (int j = 0; j < all.Length; j++) { Mesh bm = all[j].sharedMesh; if (bm != null && bm.name.StartsWith("PolyMan")) bodies.Add(all[j]); }
        deckBody = bodies.Count > 0 ? bodies[0] : null;
        glassFeet.Clear(); glassBodies.Clear(); scarves.Clear();
        for (int k = 0; k < all.Length; k++)
        {
            SkinnedMeshRenderer r = all[k];
            Mesh m = r.sharedMesh;
            if (m == null) continue;
            string n = m.name;
            if (n.StartsWith("Scarf")) { scarves.Add(r); continue; }
            if (n != "FootLMesh" && n != "FootRMesh") continue;
            p.found++;
            int id = m.GetInstanceID();
            if (!p.meshes.ContainsKey(id)) Register(p, id, m, n, m.triangles, n == "FootRMesh");
            int rid = r.GetInstanceID();
            if (p.glass)
            {
                SkinnedMeshRenderer body = null;
                for (int j = 0; j < bodies.Count && body == null; j++) if (bodies[j].transform.parent == r.transform.parent) body = bodies[j];
                for (int j = 0; j < bodies.Count && body == null; j++) if (bodies[j].transform.root == r.transform.root) body = bodies[j];
                if (body != null && body.sharedMaterial != null)
                {
                    if (r.sharedMaterial != body.sharedMaterial)
                    {
                        if (!origMat.ContainsKey(rid)) origMat[rid] = r.sharedMaterial;
                        r.sharedMaterial = body.sharedMaterial;
                    }
                    glassFeet.Add(r); glassBodies.Add(body);
                    continue;
                }
            }
            Material om; bool hadGlass = false;
            if (origMat.TryGetValue(rid, out om)) { if (om != null) r.sharedMaterial = om; origMat.Remove(rid); hadGlass = true; }
            if (!p.custom)
            {   // the game look: remove our settings from this shoe
                if (hadGlass || blockSet.ContainsKey(rid)) { r.SetPropertyBlock(null); blockSet.Remove(rid); }
                continue;
            }
            if (hadGlass) mpb.Clear(); else r.GetPropertyBlock(mpb);
            blockSet[rid] = true;
            // make the shoe shader show the vertex color directly: R, G, B map to pure red, green, blue
            mpb.SetColor("_ColorR", new Color(1, 0, 0, 1));
            mpb.SetColor("_ColorG", new Color(0, 1, 0, 1));
            mpb.SetColor("_ColorB", new Color(0, 0, 1, 1));
            mpb.SetColor("_ColorA", new Color(0, 0, 0, 1));
            r.SetPropertyBlock(mpb);
        }
        int mode = (p.glass && glassFeet.Count > 0) ? 2 : (p.custom ? 1 : 0);
        if (mode != p.mode) { p.mode = mode; p.dirty = true; }
    }
    void ScanBoard()
    {
        Painter p = board;
        bool want = p.custom || p.glass;
        if (!want && decks.Count == 0 && !(open && tab == board)) return;      // nothing to do: do not search the scene
        p.found = 0;
        MeshFilter[] all = UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
        bool glassOn = p.glass && deckBody != null && deckBody.sharedMaterial != null;
        for (int k = 0; k < all.Length; k++)
        {
            MeshFilter mf = all[k]; Mesh m = mf.sharedMesh;
            if (m == null) continue;
            string n = m.name;
            if (!n.StartsWith("Deck")) continue;
            MeshRenderer r = mf.GetComponent<MeshRenderer>();
            if (r == null) continue;
            int rid = r.GetInstanceID();
            Material[] mats = r.sharedMaterials;
            DeckRend d;
            if (!decks.TryGetValue(rid, out d))
            {
                int idx = -1;
                for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].shader != null && mats[i].shader.name == "SkateDeckwGlitter") idx = i;
                if (idx < 0) continue;
                d = new DeckRend(); d.r = r; d.matIdx = idx; d.orig = mats[idx]; decks[rid] = d;
            }
            if (d.matIdx >= mats.Length) continue;
            Material now = mats[d.matIdx];
            if (now != d.orig && now != d.clone && !(deckBody != null && now == deckBody.sharedMaterial) && now != null && now.shader != null && now.shader.name == "SkateDeckwGlitter")
            {   // the game gave the deck a new material
                d.orig = now; if (d.clone != null) UnityEngine.Object.Destroy(d.clone); d.clone = null;
            }
            p.found++;
            int id = m.GetInstanceID();
            if (!p.meshes.ContainsKey(id)) Register(p, id, m, n, d.matIdx < m.subMeshCount ? m.GetTriangles(d.matIdx) : null, false);
            Material desired = d.orig;
            if (glassOn) desired = deckBody.sharedMaterial;
            else if (p.custom)
            {
                if (d.clone == null)
                {
                    d.clone = new Material(d.orig);
                    d.clone.SetColor("_ColorR", new Color(1, 0, 0, 1));
                    d.clone.SetColor("_ColorG", new Color(0, 1, 0, 1));
                    d.clone.SetColor("_ColorB", new Color(0, 0, 1, 1));
                    d.clone.SetColor("_ColorA", new Color(0, 0, 0, 0));
                }
                d.clone.SetTexture("_MainTex", p.grain ? d.orig.mainTexture : white);
                desired = d.clone;
            }
            if (now != desired && desired != null) { mats[d.matIdx] = desired; r.sharedMaterials = mats; }
        }
        List<int> dead = null;
        foreach (KeyValuePair<int, DeckRend> kv in decks) if (kv.Value.r == null) { if (dead == null) dead = new List<int>(); dead.Add(kv.Key); }
        if (dead != null) foreach (int id in dead) { if (decks[id].clone != null) UnityEngine.Object.Destroy(decks[id].clone); decks.Remove(id); }
        if (!want) { foreach (KeyValuePair<int, DeckRend> kv in decks) if (kv.Value.clone != null) UnityEngine.Object.Destroy(kv.Value.clone); decks.Clear(); }
        int mode = glassOn ? 2 : (p.custom ? 1 : 0);
        if (mode != p.mode) { p.mode = mode; p.dirty = true; }
    }

    void Update()
    {
        float now = Time.unscaledTime;
        screen.BootUpdate(now);
        screen.Update(now);
        if (screen.dirty) { screen.dirty = false; SaveState(); }
        if (camTools.dirty && drag == 0) { camTools.dirty = false; SaveState(); }
        moveTools.Update(now);
        try { moveTools.FrameSpin(); } catch (Exception) { }
        moveTools.VibCap();
        if (moveTools.dirty && drag == 0) { moveTools.dirty = false; SaveState(); }
        fxTools.Update(now);
        SceneLoads.Hook();
        warpTools.Update(now);
        if (warpTools.dirty) { warpTools.dirty = false; SaveState(); }
        replay.Update();
        MiniText.EndFrame();
        editor.Update();
        if (editor.dirty) { editor.dirty = false; SaveState(); }
        if (replay.dirty) { replay.dirty = false; SaveState(); }
        if (fxTools.dirty) { fxTools.dirty = false; SaveState(); }
        if (now >= nextScan)
        {
            nextScan = now + (open ? 0.5f : 1.5f);
            try { ScanShoes(); } catch (Exception e) { status = "Scan error: " + e.Message; }
        }
        if (now >= nextDeckScan)
        {
            nextDeckScan = now + (open && tab == board ? 1f : 2.5f);
            try { ScanBoard(); } catch (Exception e) { status = "Board scan error: " + e.Message; }
        }
        if (shoes.dirty) { try { ApplyColors(shoes); } catch (Exception e) { status = "Paint error: " + e.Message; shoes.dirty = false; } }
        if (board.dirty) { try { ApplyColors(board); } catch (Exception e) { status = "Paint error: " + e.Message; board.dirty = false; } }
        if (now >= nextAutoSave && drag == 0)
        {
            nextAutoSave = now + 5f;
            if (shoes.needSave) { Save(shoes); SaveState(); }
            if (board.needSave) { Save(board); SaveState(); }
        }
    }
    void FixedUpdate() { try { moveTools.Physics(); } catch (Exception) { } try { editor.Physics(); } catch (Exception) { } }
    void LateUpdate()
    {
        screen.LateUpdate();
        try { subTools.LateUpdate(); } catch (Exception) { }
        moveTools.VibCap();
        try { moveTools.LatePose(); } catch (Exception ex) { moveTools.status = "Flip pose error: " + ex.Message; }
        try { replay.LateApply(); } catch (Exception ex) { replay.status = "Replay error: " + ex.Message; }
        if (subTools.dirty) { subTools.dirty = false; SaveState(); }
        camTools.LateUpdate();
        looks.LateUpdate();
        if (looks.dirty) { looks.dirty = false; SaveState(); }
        fxTools.LateUpdate();
        bool free = open || replay.active || EditorTools.Editing;
        if (free) { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }
        if (cursorHelper != null && cursorHelper.activeSelf != free) cursorHelper.SetActive(free);
        // scarf: only the drawing is set to off. The game's own on and off control of the scarf stays as it is.
        for (int i = 0; i < scarves.Count; i++) { SkinnedMeshRenderer sc = scarves[i]; if (sc != null) sc.forceRenderingOff = hideScarf; }
        ScarfHidden = hideScarf;
        // glass mode: follow the live settings of the body material
        for (int i = 0; i < glassFeet.Count; i++)
        {
            SkinnedMeshRenderer f = glassFeet[i], b = glassBodies[i];
            if (f == null || b == null) continue;
            b.GetPropertyBlock(mpb); f.SetPropertyBlock(mpb);
            if (f.sharedMaterial != b.sharedMaterial && b.sharedMaterial != null) f.sharedMaterial = b.sharedMaterial;
        }
    }
    void SetOpen(bool o)
    {
        open = o; focusField = 0; drag = 0;
        if (cursorHelper != null) cursorHelper.SetActive(o);
        if (!o) { if (shoes.needSave) Save(shoes); if (board.needSave) Save(board); SaveState(); }
        nextScan = 0f; nextDeckScan = 0f;
    }
    void OnApplicationQuit() { if (shoes.needSave) Save(shoes); if (board.needSave) Save(board); SaveState(); }

    // ---------- menu ----------
    void OnGUI()
    {
        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F6) { SetOpen(!open); e.Use(); }
        try { looks.DrawOver(); } catch (Exception) { }
        try { editor.DrawOverlay(); } catch (Exception ex) { editor.status = "Editor screen error: " + ex.Message; }
        try { replay.DrawOverlay(); } catch (Exception ex) { replay.status = "Replay screen error: " + ex.Message; }
        if (!open) return;
        if (e.type == EventType.Repaint && tab.core != null && !screenTab && !camTab && !moveTab && !fxTab && !warpTab && !replayTab)
        {
            try
            {
                if (tab.viewDirty[tab.activeView]) UpdateView(tab, tab.activeView);
                if (drag == 0) for (int i = 0; i < tab.shownViews.Length; i++) if (tab.viewDirty[tab.shownViews[i]]) UpdateView(tab, tab.shownViews[i]);
            }
            catch (Exception ex) { status = "View error: " + ex.Message; }
        }
        float s = Mathf.Clamp(Mathf.Min(Screen.height / 1080f, Screen.width / 1040f), 0.6f, 3f);
        Matrix4x4 old = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        int total = tab.builtIn.Length + tab.designs.Count;
        win.width = 1010f; win.height = replayTab ? 560f : warpTab ? 700f : fxTab ? 640f : moveTab ? 700f : camTab ? 610f : screenTab ? 700f : (tab == shoes ? 690f : 600f) + (tab.listOpen ? Mathf.Min(196, total * 26 + 6) + 8f : 0f);
        win = GUI.Window(51515, win, DrawWindow, "In-Game Editor 1.2   (F6 to close)");
        GUI.matrix = old;
    }
    Rect Area(float w, float h) { return GUILayoutUtility.GetRect(w, h, GUI.skin.label, GUILayout.Width(w), GUILayout.Height(h)); }
    void Fill(Rect r, Color c) { Color o = GUI.color; GUI.color = new Color(c.r, c.g, c.b, 1f); GUI.DrawTexture(r, white); GUI.color = o; }
    void Swatch(Color c, float w, float h) { Fill(Area(w, h), c); }
    // Mouse press and drag on a rectangle. Gives the position as fractions (0 to 1). "down" is true only on the first press.
    bool Mouse(Rect r, int id, out float fx, out float fy, out bool down, out int button)
    {
        Event e = Event.current; fx = fy = 0f; down = false; button = e.button;
        if (e.type == EventType.MouseUp) { drag = 0; return false; }
        bool hit = false;
        if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { drag = id; down = true; hit = true; focusField = 0; }
        else if (e.type == EventType.MouseDrag && drag == id) hit = true;
        if (!hit) return false;
        fx = Mathf.Clamp01((e.mousePosition.x - r.x) / r.width); fy = Mathf.Clamp01((e.mousePosition.y - r.y) / r.height);
        e.Use();
        return true;
    }
    float Slider(string label, float v, float max, float width)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(16));
        Rect sr = Area(width, 20); sr.y += 5;
        float n = GUI.HorizontalSlider(sr, v, 0f, max);
        GUILayout.Label(((int)Mathf.Round(n)).ToString(), GUILayout.Width(34));
        GUILayout.EndHorizontal();
        return n;
    }
    void PushUndo(Painter p) { if (p.core == null) return; p.undo.Add((byte[])p.core.slot.Clone()); if (p.undo.Count > 30) p.undo.RemoveAt(0); }

    void MainView(Painter p)
    {
        ShoeCore core = p.core; int k = p.activeView;
        Rect r = Area(core.vw[k], core.vh[k]);
        Event e = Event.current;
        if (e.type == EventType.Repaint)
        {
            GUI.DrawTexture(r, p.viewTex[k]);
            if (r.Contains(e.mousePosition) && p.tool == 1)
            {   // brush cursor: a ring with the size of the brush
                float rad = p.brush + 0.5f; int n = Mathf.Clamp((int)(rad * 2.2f), 12, 72);
                for (int i = 0; i < n; i++)
                {
                    float a = i * 6.2831853f / n; float x = e.mousePosition.x + Mathf.Cos(a) * rad, y = e.mousePosition.y + Mathf.Sin(a) * rad;
                    if (x < r.x || y < r.y || x > r.xMax || y > r.yMax) continue;
                    Fill(new Rect(x - 1.5f, y - 1.5f, 3f, 3f), Color.black); Fill(new Rect(x - 0.5f, y - 0.5f, 1.5f, 1.5f), i % 2 == 0 ? Color.white : p.cur[p.sel]);
                }
            }
        }
        float fx, fy; bool down; int button;
        if (!Mouse(r, 10, out fx, out fy, out down, out button)) return;
        int px = Mathf.Min(core.vw[k] - 1, (int)(fx * core.vw[k])), py = Mathf.Min(core.vh[k] - 1, (int)((1f - fy) * core.vh[k]));
        int tri = core.TriAt(k, px, py);
        if (button == 1 || p.tool == 0) { if (down && tri >= 0) { p.sel = core.slot[tri]; SyncPicker(p); } return; }
        if (p.tool == 1)
        {
            if (down) { PushUndo(p); p.lastPx = px; p.lastPy = py; }
            int dx = px - p.lastPx, dy = py - p.lastPy; float len = Mathf.Sqrt(dx * dx + dy * dy);
            int steps = Mathf.Max(1, (int)Mathf.Ceil(len / Mathf.Max(1f, p.brush * 0.5f))); bool ch = false;
            for (int i = 1; i <= steps; i++) ch |= core.PaintAt(k, p.lastPx + dx * i / steps, p.lastPy + dy * i / steps, p.brush, (byte)p.sel, p.mirror);
            p.lastPx = px; p.lastPy = py;
            if (ch) { p.dirty = true; Touch(p); }
        }
        else if (p.tool == 2 && down && tri >= 0)
        {
            PushUndo(p);
            bool ch = core.Fill(tri, (byte)p.sel);
            if (p.mirror)
            {
                int m = core.NearestTri(core.minx + core.maxx - core.cen[3 * tri], core.cen[3 * tri + 1], core.cen[3 * tri + 2], 0.015f);
                if (m >= 0 && m != tri) ch |= core.Fill(m, (byte)p.sel);
            }
            if (ch) { p.dirty = true; Touch(p); }
        }
    }
    void DesignBar(Painter p)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Design:", GUILayout.Width(50));
        int total = p.builtIn.Length + p.designs.Count, curIdx = 0;
        for (int i = 0; i < total; i++) if (DesignName(p, i) == p.currentName && (i < p.builtIn.Length) == p.currentBuiltIn) curIdx = i;
        if (GUILayout.Button("<", GUILayout.Width(26))) { int i = (curIdx + total - 1) % total; Select(p, DesignName(p, i), i < p.builtIn.Length, true); }
        if (GUILayout.Button(p.currentName + (p.currentBuiltIn ? "  (built-in)" : "") + "    v", GUILayout.Width(250))) p.listOpen = !p.listOpen;
        if (GUILayout.Button(">", GUILayout.Width(26))) { int i = (curIdx + 1) % total; Select(p, DesignName(p, i), i < p.builtIn.Length, true); }
        if (GUILayout.Button("New", GUILayout.Width(50)))
        {
            if (p.needSave) Save(p);
            p.currentName = UniqueName(p, "Design " + (p.designs.Count + 1)); p.currentBuiltIn = false; p.designs.Add(p.currentName); p.nameField = p.currentName;
            Save(p); SaveState(); status = "New design: " + p.currentName + " (a copy of the one before)";
        }
        GUILayout.Space(12);
        p.nameField = MiniField(p == shoes ? 2 : 3, p.nameField, 24, false, 190);
        if (GUILayout.Button("Rename", GUILayout.Width(66)))
        {
            string nm = p.nameField.Trim();
            if (nm.Length > 0 && nm != p.currentName && !p.designs.Contains(nm) && !IsBuiltIn(p, nm))
            {
                string oldName = p.currentName; bool wasUser = !p.currentBuiltIn;
                if (wasUser) p.designs.Remove(oldName);
                p.currentName = nm; p.currentBuiltIn = false; p.designs.Add(nm); p.designs.Sort();
                Save(p); SaveState();
                if (wasUser) { try { File.Delete(FileOf(p, oldName)); } catch (Exception) { } }
                status = "Name is now: " + nm; focusField = 0;
            }
            else status = "Type a new name in the box, then click Rename.";
        }
        if (GUILayout.Button("Delete", GUILayout.Width(60)))
        {
            if (p.currentBuiltIn) status = "A built-in design cannot be deleted.";
            else
            {
                string dn = p.currentName; p.designs.Remove(dn);
                try { File.Delete(FileOf(p, dn)); } catch (Exception) { }
                p.needSave = false; Select(p, p.builtIn[0].Split(',')[0], true, false); SaveState(); status = "Deleted: " + dn;
            }
        }
        GUILayout.EndHorizontal();
        if (!p.listOpen) return;
        p.listScroll = GUILayout.BeginScrollView(p.listScroll, GUILayout.Height(Mathf.Min(196, total * 26 + 6)));
        for (int i = 0; i < total; i++)
        {
            bool bi = i < p.builtIn.Length; string nm = DesignName(p, i);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(i == curIdx, nm + (bi ? "  (built-in)" : ""), GUI.skin.button, GUILayout.Width(330)) && i != curIdx) { Select(p, nm, bi, true); }
            if (bi) { string[] f = p.builtIn[i].Split(','); for (int j = 2; j < f.Length && j <= 11; j++) { Color c; if (FromHex(f[j], out c)) Swatch(c, 12, 20); } }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
    }
    static string DesignName(Painter p, int i) { return i < p.builtIn.Length ? p.builtIn[i].Split(',')[0] : p.designs[i - p.builtIn.Length]; }

    void DrawWindow(int id)
    {
        GUILayout.BeginHorizontal();
        bool painterTab = !screenTab && !camTab && !moveTab && !fxTab && !warpTab && !replayTab;
        if (GUILayout.Toggle(tab == shoes && painterTab, "Shoes", GUI.skin.button, GUILayout.Width(95)) && !(tab == shoes && painterTab)) { tab = shoes; screenTab = camTab = moveTab = fxTab = warpTab = replayTab = false; focusField = 0; }
        if (GUILayout.Toggle(tab == board && painterTab, "Board", GUI.skin.button, GUILayout.Width(95)) && !(tab == board && painterTab)) { tab = board; screenTab = camTab = moveTab = fxTab = warpTab = replayTab = false; focusField = 0; nextDeckScan = 0f; }
        if (GUILayout.Toggle(screenTab, "Screen", GUI.skin.button, GUILayout.Width(95)) && !screenTab) { screenTab = true; camTab = moveTab = fxTab = warpTab = replayTab = false; focusField = 0; }
        if (GUILayout.Toggle(camTab, "Camera", GUI.skin.button, GUILayout.Width(95)) && !camTab) { camTab = true; screenTab = moveTab = fxTab = warpTab = replayTab = false; focusField = 0; }
        if (GUILayout.Toggle(moveTab, "Movement", GUI.skin.button, GUILayout.Width(95)) && !moveTab) { moveTab = true; screenTab = camTab = fxTab = warpTab = replayTab = false; focusField = 0; }
        if (GUILayout.Toggle(fxTab, "Effects", GUI.skin.button, GUILayout.Width(95)) && !fxTab) { fxTab = true; screenTab = camTab = moveTab = warpTab = replayTab = false; focusField = 0; }
        if (GUILayout.Toggle(warpTab, "Levels", GUI.skin.button, GUILayout.Width(95)) && !warpTab) { warpTab = true; screenTab = camTab = moveTab = fxTab = replayTab = false; focusField = 0; }
        if (GUILayout.Toggle(replayTab, "Replay", GUI.skin.button, GUILayout.Width(95)) && !replayTab) { replayTab = true; screenTab = camTab = moveTab = fxTab = warpTab = false; focusField = 0; }
        GUILayout.Space(16);
        bool sh = GUILayout.Toggle(!hideScarf, " Show scarf", GUILayout.Width(100));
        if (sh == hideScarf) { hideScarf = !sh; nextScan = 0f; SaveState(); }
        GUILayout.Label(status);
        GUILayout.EndHorizontal();
        if (screenTab) { screen.Draw(); subTools.Draw(); looks.Draw(); GUI.DragWindow(new Rect(0, 0, 10000, 22)); return; }
        if (camTab) { camTools.Draw(); GUI.DragWindow(new Rect(0, 0, 10000, 22)); return; }
        if (moveTab) { moveTools.Draw(); GUI.DragWindow(new Rect(0, 0, 10000, 22)); return; }
        if (replayTab) { replay.Draw(); GUI.DragWindow(new Rect(0, 0, 10000, 22)); return; }
        if (warpTab) { editor.DrawTab(); GUILayout.Space(6); warpTools.Draw(); GUI.DragWindow(new Rect(0, 0, 10000, 22)); return; }
        if (fxTab) { fxTools.Draw(); GUI.DragWindow(new Rect(0, 0, 10000, 22)); return; }
        Painter p = tab;
        DesignBar(p);

        GUILayout.BeginHorizontal();
        // ----- left: pictures and paint tools -----
        GUILayout.BeginVertical(GUILayout.Width(660));
        if (p.core == null)
        {
            if (p == shoes) GUILayout.Label(p.found == 0 ? "No shoes found in this scene yet." : (p.badMeshes > 0 ? "The shoe files are not the version for this editor. Install the mod files again." : "Waiting for the shoe mesh."));
            else GUILayout.Label(p.found == 0 ? "No board found in this scene yet. Go to a place where you ride the board." : (p.badMeshes > 0 ? "The board files are not the version for this editor. Install the mod files again." : "Waiting for the board mesh."));
            GUILayout.Label("You can set the colors now. The pictures come when the game shows the " + (p == shoes ? "shoes." : "board."));
        }
        else
        {
            GUILayout.Label("How to paint:  1. Click a part in the list on the right.  2. Drag on the large picture.  The area becomes that part and gets its color.");
            MainView(p);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < p.shownViews.Length && p.shownViews.Length > 1; i++)
            {
                int k = p.shownViews[i]; float tw = 150f, th = tw * p.core.vh[k] / p.core.vw[k];
                Rect tr = Area(tw + 4, th + 4);
                if (Event.current.type == EventType.Repaint) { Fill(tr, k == p.activeView ? Color.white : Color.black); GUI.DrawTexture(new Rect(tr.x + 2, tr.y + 2, tw, th), p.viewTex[k]); }
                if (Event.current.type == EventType.MouseDown && tr.Contains(Event.current.mousePosition)) { p.activeView = k; Event.current.Use(); }
            }
            if (p.shownViews.Length > 1) GUILayout.Label("  Click a small picture\n  to paint that side.");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(p.tool == 1, "Brush", GUI.skin.button, GUILayout.Width(60))) p.tool = 1;
            if (GUILayout.Toggle(p.tool == 2, "Fill", GUI.skin.button, GUILayout.Width(50))) p.tool = 2;
            if (GUILayout.Toggle(p.tool == 0, "Pick part", GUI.skin.button, GUILayout.Width(74))) p.tool = 0;
            GUILayout.Label("Brush size", GUILayout.Width(66));
            Rect br = Area(120, 20); br.y += 5; p.brush = (int)Mathf.Round(GUI.HorizontalSlider(br, p.brush, 1f, 40f));
            p.mirror = GUILayout.Toggle(p.mirror, " Both sides", GUILayout.Width(88));
            if (GUILayout.Button("Undo", GUILayout.Width(52)) && p.undo.Count > 0)
            {
                byte[] u = p.undo[p.undo.Count - 1]; p.undo.RemoveAt(p.undo.Count - 1);
                if (u.Length == p.core.slot.Length) { Array.Copy(u, p.core.slot, u.Length); p.dirty = true; Touch(p); }
            }
            if (GUILayout.Button("Reset paint", GUILayout.Width(86))) { PushUndo(p); p.core.ResetLayout(); p.dirty = true; Touch(p); }
            GUILayout.EndHorizontal();
            GUILayout.Label("Brush: drag to paint.   Fill: click an area.   Pick part or right-click: select the part under the pointer.");
        }
        GUILayout.EndVertical();

        // ----- right: options, parts, color -----
        GUILayout.BeginVertical(GUILayout.Width(318));
        if (p == shoes)
        {
            bool cs = GUILayout.Toggle(p.custom, " Custom shoe paint (off = the game look)");
            if (cs != p.custom) { p.custom = cs; Fork(p); nextScan = 0f; }
            bool g = GUILayout.Toggle(p.glass, " Glass shoes (use the body material)");
            if (g != p.glass) { p.glass = g; Fork(p); nextScan = 0f; }
            bool st = GUILayout.Toggle(p.stripes, " Three side stripes (off = the original side mark)");
            if (st != p.stripes) { p.stripes = st; Fork(p); RefreshBase(p); }
        }
        else
        {
            bool c = GUILayout.Toggle(p.custom, " Custom board paint (off = the game look)");
            if (c != p.custom) { p.custom = c; Fork(p); nextDeckScan = 0f; }
            bool g = GUILayout.Toggle(p.glass, " Glass board (use the body material)");
            if (g != p.glass) { p.glass = g; Fork(p); nextDeckScan = 0f; }
            bool gr = GUILayout.Toggle(p.grain, " Keep the grip grain texture (darker colors)");
            if (gr != p.grain) { p.grain = gr; Fork(p); nextDeckScan = 0f; }
        }
        if (p.mode == 2) GUILayout.Label("Glass is on, so the paint does not show now.");
        else if (!p.custom) GUILayout.Label("Custom paint is off. It goes on when you paint or change a color.");
        GUILayout.BeginHorizontal();
        GUILayout.Label("You paint with:", GUILayout.Width(96)); Swatch(p.cur[p.sel], 40, 20); GUILayout.Label(" " + p.names[p.sel]);
        GUILayout.EndHorizontal();
        int rows = (p.N + 1) / 2;
        for (int row = 0; row < rows; row++)
        {
            GUILayout.BeginHorizontal();
            for (int k = 0; k < 2; k++)
            {
                int i = row + k * rows; if (i >= p.N) break;
                Swatch(p.cur[i], 18, 20);
                if (GUILayout.Toggle(p.sel == i, p.names[i], GUI.skin.button, GUILayout.Width(120)) && p.sel != i) { p.sel = i; SyncPicker(p); focusField = 0; }
            }
            GUILayout.EndHorizontal();
        }
        Picker(p);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUI.DragWindow(new Rect(0, 0, 10000, 22));
    }
    void Picker(Painter p)
    {
        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        if ((Mathf.Abs(svHue - p.hue) > 0.001f || svOwner != p) && Event.current.type == EventType.Repaint)
        {
            svHue = p.hue; svOwner = p;
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) svTex.SetPixel(x, y, Color.HSVToRGB(p.hue, x / 63f, y / 63f));
            svTex.Apply();
        }
        Rect sv = Area(190, 130);
        if (Event.current.type == EventType.Repaint)
        {
            GUI.DrawTexture(sv, svTex);
            float mx = sv.x + p.sat * sv.width, my = sv.y + (1f - p.val) * sv.height;
            Fill(new Rect(mx - 5, my - 5, 10, 10), Color.black); Fill(new Rect(mx - 4, my - 4, 8, 8), Color.white); Fill(new Rect(mx - 3, my - 3, 6, 6), p.cur[p.sel]);
        }
        float fx, fy; bool down; int button;
        if (Mouse(sv, 1, out fx, out fy, out down, out button)) { p.sat = fx; p.val = 1f - fy; SetSel(p, Color.HSVToRGB(p.hue, p.sat, p.val), true); }
        GUILayout.Space(6);
        Rect hr = Area(24, 130);
        if (Event.current.type == EventType.Repaint)
        {
            GUI.DrawTexture(hr, hueTex);
            float hy = hr.y + (1f - p.hue) * hr.height;
            Fill(new Rect(hr.x - 2, hy - 2, hr.width + 4, 4), Color.black); Fill(new Rect(hr.x - 1, hy - 1, hr.width + 2, 2), Color.white);
        }
        if (Mouse(hr, 2, out fx, out fy, out down, out button)) { p.hue = Mathf.Clamp(1f - fy, 0f, 0.999f); SetSel(p, Color.HSVToRGB(p.hue, p.sat, p.val), true); }
        GUILayout.BeginVertical();
        GUILayout.Label("Hex");
        string t = MiniField(1, p.hexText, 6, true, 70);
        if (t != p.hexText) { p.hexText = t; Color hc; if (FromHex(t, out hc)) { p.cur[p.sel] = hc; p.dirty = true; Touch(p); Color.RGBToHSV(hc, out p.hue, out p.sat, out p.val); } }
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        Color c = p.cur[p.sel];
        float r = Slider("R", c.r * 255f, 255f, 230), gg = Slider("G", c.g * 255f, 255f, 230), b = Slider("B", c.b * 255f, 255f, 230);
        Color rgb = new Color(Mathf.Round(r) / 255f, Mathf.Round(gg) / 255f, Mathf.Round(b) / 255f, 1f);
        if (ToHex(rgb) != ToHex(c)) SetSel(p, rgb, false);
        for (int row = 0; row < 2; row++)
        {
            GUILayout.BeginHorizontal();
            for (int i = row * 10; i < row * 10 + 10 && i < Quick.Length; i++)
            {
                Color qc; FromHex(Quick[i], out qc);
                Rect qr = Area(24, 18);
                if (Event.current.type == EventType.Repaint) { Fill(qr, Color.black); Fill(new Rect(qr.x + 1, qr.y + 1, qr.width - 2, qr.height - 2), qc); }
                if (Event.current.type == EventType.MouseDown && qr.Contains(Event.current.mousePosition)) { SetSel(p, qc, false); Event.current.Use(); }
            }
            GUILayout.EndHorizontal();
        }
    }
    // The game's GUI library has no text field, so this is a small one: click it, then type.
    string MiniField(int fid, string text, int maxLen, bool hexOnly, float width)
    {
        bool focused = focusField == fid;
        if (GUILayout.Toggle(focused, focused ? text + "_" : text, GUI.skin.button, GUILayout.Width(width)) != focused) focusField = focused ? 0 : fid;
        Event e = Event.current;
        if (focusField == fid && e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Backspace) { if (text.Length > 0) text = text.Substring(0, text.Length - 1); e.Use(); }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Escape) { focusField = 0; e.Use(); }
            else
            {
                char ch = e.character;
                bool ok = hexOnly ? ((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F'))
                                  : (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_');
                if (ok && text.Length < maxLen) text += hexOnly ? char.ToUpperInvariant(ch) : ch;
                if (ch != 0) e.Use();
            }
        }
        return text;
    }
}

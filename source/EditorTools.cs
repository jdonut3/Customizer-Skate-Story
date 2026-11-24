// Level editor: a large flat grid world, with building blocks (quarter pipe, half pipe, spine, kicker, bank, box,
// rail, stairs, a start point) to place, move, turn, size, save and load, and then skate on.
//  The world is made in the level you are in: the level's drawn objects and its colliders (also its trigger zones,
//  so nothing in the level can send you back) are turned off, and the grid floor is laid where you stand. The level's
//  sky, light and fog stay.
//  The blocks are made in code. They use the layer, the ground sound and the material of the ground you stood on, so
//  the board rolls and sounds as on the real ground. The game grinds on edges of colliders on that layer, so every
//  edge (box edges, the lip of a ramp, the rail bar) can be ground.
//  Edit mode: the game stops (the same pause value as the replay), the camera flies free.
//  Play mode: the game goes on, and you skate from the start point. P switches between the two.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class EditorTools
{
    public static bool Active, Editing;      // the editor world is on / the edit mode (the game is stopped)
    public string status = "";
    public bool dirty;

    // ---------- the block types ----------
    public class Kind
    {
        public string id, name, help; public string[] pn; public float[] pd, pmin, pmax;
        public Kind(string id, string name, string help, string[] pn, float[] pd, float[] pmin, float[] pmax) { this.id = id; this.name = name; this.help = help; this.pn = pn; this.pd = pd; this.pmin = pmin; this.pmax = pmax; }
    }
    static readonly Kind[] kinds = {
        new Kind("qp", "Quarter pipe", "A curve that turns all the way up (vertical at the top), then a deck. Vert = the straight-up part.",
            new[] { "Width", "Height", "Vert", "Deck" }, new[] { 4f, 2f, 0.25f, 1.2f }, new[] { 1f, 0.5f, 0f, 0f }, new[] { 200f, 15f, 5f, 20f }),
        new Kind("hp", "Half pipe", "Two quarter pipes face to face, with a flat part between them. Vertical at the top.",
            new[] { "Width", "Height", "Vert", "Flat", "Deck" }, new[] { 5f, 2.6f, 0.3f, 4f, 1.2f }, new[] { 1f, 0.5f, 0f, 0f, 0f }, new[] { 200f, 15f, 5f, 100f, 20f }),
        new Kind("spine", "Spine", "Two quarter pipes back to back, with a sharp top.",
            new[] { "Width", "Height", "Curve radius" }, new[] { 4f, 1.6f, 2f }, new[] { 1f, 0.3f, 0.5f }, new[] { 200f, 10f, 20f }),
        new Kind("kicker", "Kicker", "A short curved launch ramp.",
            new[] { "Width", "Height", "Length", "Curve" }, new[] { 2f, 0.8f, 2.2f, 0.6f }, new[] { 0.5f, 0.1f, 0.3f, 0f }, new[] { 60f, 8f, 30f, 1f }),
        new Kind("bank", "Bank", "A flat slope.",
            new[] { "Width", "Height", "Length" }, new[] { 3f, 1f, 3f }, new[] { 0.5f, 0.1f, 0.3f }, new[] { 100f, 15f, 60f }),
        new Kind("box", "Box / ledge", "A box. Low and long = a ledge; big = a platform. All its edges can be ground.",
            new[] { "Length", "Width", "Height" }, new[] { 3f, 1f, 0.45f }, new[] { 0.2f, 0.2f, 0.05f }, new[] { 200f, 200f, 30f }),
        new Kind("rail", "Rail", "A flat bar on posts.",
            new[] { "Length", "Height", "Bar width" }, new[] { 5f, 0.4f, 0.06f }, new[] { 0.5f, 0.1f, 0.03f }, new[] { 200f, 5f, 1f }),
        new Kind("stairs", "Stairs", "Steps up to a landing, with a handrail on one side.",
            new[] { "Steps", "Width", "Step height", "Step depth", "Landing", "Handrail" }, new[] { 5f, 3f, 0.18f, 0.35f, 1.5f, 1f },
            new[] { 1f, 0.6f, 0.08f, 0.2f, 0f, 0f }, new[] { 60f, 50f, 0.6f, 1.5f, 30f, 1f }),
        new Kind("start", "Start point", "Where you start when you press Play. The arrow shows the direction.",
            new string[0], new float[0], new float[0], new float[0]),
    };
    static Kind KindOf(string id) { foreach (Kind k in kinds) if (k.id == id) return k; return null; }

    class Block { public Kind kind; public float[] p; public GameObject go; public MeshFilter mf; public MeshCollider mc; public Mesh colMesh; public List<BoxCollider> boxes = new List<BoxCollider>(); public string look0 = "", look1 = ""; public List<BoxCollider> decks = new List<BoxCollider>(); }
    List<Block> blocks = new List<Block>();

    // ---------- world ----------
    GameObject root, floor;
    Vector3 origin; int layer; FloorMaterial groundFm;
    Material blockMat, railMat, floorMat, startMat;
    List<Renderer> offR = new List<Renderer>(); List<Collider> offC = new List<Collider>(); List<Behaviour> offB = new List<Behaviour>();
    Vector3 backPos; Quaternion backRot; bool haveBack;
    const float FloorSize = 600f;
    const float Lift = 0.004f;          // blocks sit this much above what is under them, so their faces do not flicker

    // ---------- edit state ----------
    Vector3 camPos; float camYaw, camPitch = 25f, camSpeed = 10f, lookSpeed = 3f;
    Block sel, ghost; bool placing, keepPlacing;
    float snap = 0.5f, turnStep = 15f;
    bool dragging, dragUp; Vector3 dragOffset; float dragPlaneY; Vector2 dragLast;
    string layoutName = "My park";
    float oldPause = 1f;
    float nextFall;

    string Dir { get { return Path.Combine(Path.Combine(Application.persistentDataPath, "ShoeMod"), "parks"); } }

    // ===================================================================================== start and end
    public void Open()
    {
        if (Active) return;
        if (ReplayTools.Active) { status = "Close the replay first."; return; }
        ThePlayer p = ThePlayer.instance;
        SkatePlayer sp = null; try { sp = ThePlayer.SkatePlayer; } catch (Exception) { }
        if (p == null || sp == null || Camera.main == null) { status = "The skater is not loaded. Go into a level first."; return; }
        string stage = "start";
        try
        {
            backPos = sp.transform.position; backRot = sp.transform.rotation; haveBack = true;
            stage = "ground"; Probe(p, sp);
            levelMode = false; planeY = origin.y;
            stage = "hide the level"; HideLevel(p, sp);
            stage = "floor";
            root = new GameObject("ShoeModPark");
            root.transform.position = origin;
            MakeFloor();
            FindLooks();
            stage = "camera";
            Active = true; sessionOpen = true; nextFall = Time.unscaledTime + 1f;
            // the camera starts where the game camera is
            Transform ct = Camera.main.transform;
            camPos = ct.position; Vector3 e = ct.rotation.eulerAngles; camYaw = e.y; camPitch = e.x > 180f ? e.x - 360f : e.x;
            stage = "start point";
            if (blocks.Count == 0) AddBlock(KindOf("start"), origin + Flat(ct.forward) * 3f + Vector3.up * Lift, camYaw, false);
            stage = "edit mode";
            SetEditing(true);
            status = "Park editor on. Pick a block on the left, then click in the world to place it.";
        }
        catch (Exception ex) { string msg = "Could not open the editor (" + stage + "): " + ex.Message; Close(); status = msg; Log(ex, stage); }
    }
    // the ground under the skater: its layer, its ground sound, its material (the blocks copy them)
    void Probe(ThePlayer p, SkatePlayer sp)
    {
        {
            RaycastHit hit; layer = 0; groundFm = null; Material gm = null;
            origin = sp.transform.position;
            RaycastHit[] hits = new RaycastHit[16];
            int mask = ~0;
            try { mask = sp.physicsLayers.value; } catch (Exception) { }
            if (mask == 0) mask = ~0;
            int n = UnityEngine.Physics.RaycastNonAlloc(sp.transform.position + Vector3.up * 1f, Vector3.down, hits, 20f, mask, QueryTriggerInteraction.Ignore);
            float best = 1e30f; hit = new RaycastHit(); bool found = false;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(p.transform.root) || hits[i].collider.transform.IsChildOf(sp.transform.root)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; hit = hits[i]; found = true; }
            }
            if (found)
            {
                origin = hit.point; layer = hit.collider.gameObject.layer;
                hit.collider.TryGetComponent<FloorMaterial>(out groundFm);
                Renderer gr = hit.collider.GetComponent<Renderer>(); if (gr == null) gr = hit.collider.GetComponentInParent<Renderer>();
                if (gr != null) gm = gr.sharedMaterial;
            }
            // the layer must be one that the skater stands on
            if ((mask & (1 << layer)) == 0 || !found)
            {
                int geo = mask; try { geo = mask & SkateSingleton.main.geometryLayerMask.value; } catch (Exception) { }
                if (geo == 0) geo = mask;
                for (int i = 0; i < 32; i++) if ((geo & (1 << i)) != 0) { layer = i; break; }
            }
            MakeMaterials(gm);
        }
    }

    // ===================================================================================== blocks in a real level
    // The same blocks, placed in the level you are in (nothing is hidden, there is no grid floor). They are saved for
    // this level (ShoeMod/levels/<level>.txt) and come back by themselves when you come to this level again.
    public bool levelMode, sessionOpen, autoLoad = true;
    float planeY; string levelScene = "";
    bool pendingAuto = true; float autoAt;
    string LevelDir { get { return Path.Combine(Path.Combine(Application.persistentDataPath, "ShoeMod"), "levels"); } }
    string LevelFile() { return Path.Combine(LevelDir, SceneName() + ".txt"); }
    static string SceneName() { return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
    public void OpenLevel(bool edit)
    {
        if (ReplayTools.Active) { status = "Close the replay first."; return; }
        if (Active && !levelMode) { status = "Close the park editor first."; return; }
        ThePlayer p = ThePlayer.instance;
        SkatePlayer sp = null; try { sp = ThePlayer.SkatePlayer; } catch (Exception) { }
        if (p == null || sp == null || Camera.main == null) { status = "The skater is not loaded. Go into a level first."; return; }
        try
        {
            if (!Active)
            {
                Probe(p, sp);
                levelMode = true; planeY = origin.y; origin = Vector3.zero;   // places in a level are kept in world space
                root = new GameObject("ShoeModLevelBlocks");
                floor = null; offR.Clear(); offC.Clear(); offB.Clear();
                FindLooks();
                Active = true; levelScene = SceneName();
                if (File.Exists(LevelFile())) Load(LevelFile());
            }
            if (edit)
            {
                Transform ct = Camera.main.transform;
                camPos = ct.position; Vector3 e = ct.rotation.eulerAngles; camYaw = e.y; camPitch = e.x > 180f ? e.x - 360f : e.x;
                backPos = sp.transform.position; backRot = sp.transform.rotation; haveBack = true;
                sessionOpen = true; SetEditing(true);
                status = "Editing " + levelScene + ". Place blocks, then Save (they come back each time you come here).";
            }
        }
        catch (Exception ex) { status = "Could not edit the level: " + ex.Message; Log(ex, "level"); }
    }
    // take the blocks out of this level (the file stays, unless you delete it)
    public void RemoveLevelBlocks()
    {
        if (!levelMode) return;
        if (Editing) SetEditing(false);
        if (deckOffBlock != null) deckOffBlock = null;
        if (root != null) UnityEngine.Object.Destroy(root);
        root = null; blocks.Clear(); sel = null; ghost = null; placing = false;
        Active = false; levelMode = false; sessionOpen = false;
    }
    static void Log(Exception ex, string stage)
    {
        try { File.WriteAllText(Path.Combine(Path.Combine(Application.persistentDataPath, "ShoeMod"), "editor_error.txt"), DateTime.Now + "  step: " + stage + "\n" + ex.ToString()); } catch (Exception) { }
    }
    public void Close()
    {
        if (levelMode)
        {   // done editing: the blocks stay in the level
            if (Editing) SetEditing(false);
            CancelPlace(); sessionOpen = false;
            status = "Done. The blocks stay in this level" + (File.Exists(LevelFile()) ? "." : " (not saved yet: Edit this level, then Save).");
            return;
        }
        if (Editing) SetEditing(false);
        sessionOpen = false;
        foreach (Renderer r in offR) if (r != null) r.enabled = true;
        foreach (Collider c in offC) if (c != null) c.enabled = true;
        foreach (Behaviour b in offB) if (b != null) b.enabled = true;
        offR.Clear(); offC.Clear(); offB.Clear();
        if (root != null) UnityEngine.Object.Destroy(root);
        root = null; floor = null; blocks.Clear(); sel = null; ghost = null; placing = false;
        bool was = Active; Active = false;
        if (was && haveBack) Teleport(backPos, backRot);
        haveBack = false;
        if (was) status = "Level editor closed. The level is back.";
    }
    // a new level load ends the editor (its world was in the old level)
    int seenLoads = -1;

    void HideLevel(ThePlayer p, SkatePlayer sp)
    {
        offR.Clear(); offC.Clear(); offB.Clear();
        HashSet<Transform> keep = new HashSet<Transform>();
        keep.Add(p.transform.root); keep.Add(sp.transform.root);
        if (Camera.main != null) keep.Add(Camera.main.transform.root);
        try { if (SkateSingleton.main != null) keep.Add(SkateSingleton.main.transform.root); } catch (Exception) { }
        UnityEngine.SceneManagement.Scene keepScene = SkateSingleton.main != null ? SkateSingleton.main.gameObject.scene : p.gameObject.scene;
        for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
        {
            UnityEngine.SceneManagement.Scene s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(si);
            if (!s.isLoaded || s == keepScene) continue;
            foreach (GameObject g in s.GetRootGameObjects())
            {
                if (g == null || keep.Contains(g.transform)) continue;
                // (a component with a missing script shows as an empty entry in these lists: skip those)
                foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true)) if (r != null && r.enabled) { r.enabled = false; offR.Add(r); }
                foreach (Collider c in g.GetComponentsInChildren<Collider>(true)) if (c != null && c.enabled) { c.enabled = false; offC.Add(c); }
                foreach (Behaviour b in g.GetComponentsInChildren<Behaviour>(true))
                {
                    if (b == null || !b.enabled) continue;
                    Light li = b as Light;
                    if (b.GetType().Name == "Terrain" || (li != null && li.type != LightType.Directional)) { b.enabled = false; offB.Add(b); }
                }
            }
        }
    }

    // ---------- materials ----------
    void MakeMaterials(Material ground)
    {
        Shader lit = null;
        foreach (string n in new[] { "Standard", "Legacy Shaders/Diffuse", "Mobile/Diffuse" }) { lit = Shader.Find(n); if (lit != null) break; }
        // the blocks: the ground material of this level (concrete in most levels), else a plain gray
        if (ground != null) blockMat = ground;
        else if (lit != null) { blockMat = new Material(lit); blockMat.color = new Color(0.62f, 0.62f, 0.6f); }
        // rails: a metal material from the level if there is one
        railMat = null;
        foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
        {
            if (r == null || r.sharedMaterial == null) continue;
            string mn = r.sharedMaterial.name.ToLower();
            if (mn.Contains("metal") || mn.Contains("rail") || mn.Contains("steel") || mn.Contains("iron")) { railMat = r.sharedMaterial; break; }
        }
        if (railMat == null && lit != null) { railMat = new Material(lit); railMat.color = new Color(0.35f, 0.37f, 0.4f); }
        if (blockMat == null) blockMat = railMat;
        if (blockMat == null) { Shader any = Shader.Find("Hidden/Internal-Colored"); if (any != null) { blockMat = new Material(any); blockMat.color = new Color(0.62f, 0.62f, 0.6f); } }
        if (railMat == null) railMat = blockMat;
        // the floor: a gray grid (1 m lines, a stronger line each 10 m)
        Texture2D grid = GridTexture();
        if (lit != null) { floorMat = new Material(lit); floorMat.mainTexture = grid; floorMat.color = Color.white; }
        else
        {
            Shader un = Shader.Find("Unlit/Texture");
            floorMat = un != null ? new Material(un) : blockMat != null ? new Material(blockMat) : null;
            if (floorMat == null) { floorMat = blockMat; return; }
            floorMat.mainTexture = grid;
            if (un == null) status = "No plain shader found: the floor uses the level's ground look.";
        }
        if (lit != null) { startMat = new Material(lit); startMat.color = new Color(0.2f, 0.9f, 0.4f); } else startMat = railMat;
    }
    static Texture2D GridTexture()
    {
        const int S = 512;   // the texture is 10 m: 1 m = 51.2 pixels
        Texture2D t = new Texture2D(S, S, TextureFormat.RGBA32, true);
        Color32[] px = new Color32[S * S];
        Color32 bg = new Color32(150, 152, 156, 255), minor = new Color32(118, 120, 125, 255), major = new Color32(70, 72, 78, 255);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                Color32 c = bg;
                float fx = x * 10f / S, fy = y * 10f / S;
                float dx = Mathf.Abs(fx - Mathf.Round(fx)), dy = Mathf.Abs(fy - Mathf.Round(fy));
                if (dx < 0.025f || dy < 0.025f) c = minor;
                if (x < 3 || y < 3 || x >= S - 2 || y >= S - 2) c = major;
                px[y * S + x] = c;
            }
        t.SetPixels32(px); t.Apply(true);
        t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Trilinear; t.anisoLevel = 8;
        return t;
    }

    void MakeFloor()
    {
        floor = new GameObject("Floor");
        floor.transform.SetParent(root.transform, false);
        floor.layer = layer;
        MeshBuilder mb = new MeshBuilder();
        float h = FloorSize * 0.5f;
        mb.Quad(new Vector3(-h, 0, -h), new Vector3(-h, 0, h), new Vector3(h, 0, h), new Vector3(h, 0, -h), 0.1f);
        floor.AddComponent<MeshFilter>().sharedMesh = mb.Make();
        floor.AddComponent<MeshRenderer>().sharedMaterial = floorMat;
        BoxCollider bc = floor.AddComponent<BoxCollider>();
        bc.center = new Vector3(0, -2f, 0); bc.size = new Vector3(FloorSize, 4f, FloorSize);
        CopyGround(floor);
    }
    void CopyGround(GameObject g)
    {
        if (groundFm == null) return;
        try
        {
            // every setting of the real ground (its sound, its slide, and its crack settings: left at their start values,
            // the crack settings made the ground crack and shake the controller all the time)
            FloorMaterial f = g.AddComponent<FloorMaterial>();
            foreach (System.Reflection.FieldInfo fi in typeof(FloorMaterial).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                if (typeof(Delegate).IsAssignableFrom(fi.FieldType)) continue;
                if (fi.Name == "onRideEnterEvent" || fi.Name == "onLandTrick") continue;     // level events: not on the blocks
                try { fi.SetValue(f, fi.GetValue(groundFm)); } catch (Exception) { }
            }
            f.killPlayer = false; f.killPlayerOnLand = false; f.killPlayerOnRoll = false; f.heal = 0f; f.disableOnLand = false; f.isSpecial = false;
        }
        catch (Exception) { }
    }

    // ===================================================================================== blocks
    Block AddBlock(Kind k, Vector3 pos, float yaw, bool select)
    {
        Block b = new Block { kind = k, p = (float[])k.pd.Clone() };
        b.go = new GameObject(k.name);
        b.go.transform.SetParent(root.transform, true);
        b.go.layer = layer;
        b.go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        b.mf = b.go.AddComponent<MeshFilter>();
        MeshRenderer mr = b.go.AddComponent<MeshRenderer>();
        if (k.id == "start") mr.sharedMaterials = new[] { startMat };
        else ApplyLook(b, mr);
        // solid: boxes and rails get box colliders (the surest kind); ramps and stairs a closed mesh collider
        if (k.id != "start") { if (k.id != "box" && k.id != "rail") b.mc = b.go.AddComponent<MeshCollider>(); CopyGround(b.go); }
        Rebuild(b);
        blocks.Add(b);
        if (select) sel = b;
        return b;
    }
    void Rebuild(Block b)
    {
        // the drawn mesh has no bottom faces (they would flicker against the floor); the collider mesh is closed
        MeshBuilder mb = new MeshBuilder(); mb.skipBottom = true;
        Build(b.kind.id, b.p, mb);
        Mesh m = mb.Make();
        if (b.mf.sharedMesh != null) UnityEngine.Object.Destroy(b.mf.sharedMesh);
        b.mf.sharedMesh = m;
        if (b.mc != null)
        {
            MeshBuilder cb = new MeshBuilder();
            // pipes: the deck is a box collider of its own (so that it can be turned off for a moment, see the vert ride)
            float[] cp = (float[])b.p.Clone();
            if (b.kind.id == "qp") cp[3] = 0f; else if (b.kind.id == "hp") cp[4] = 0f;
            Build(b.kind.id, cp, cb);
            if (b.colMesh != null) UnityEngine.Object.Destroy(b.colMesh);
            b.colMesh = cb.Make();
            b.mc.sharedMesh = null; b.mc.sharedMesh = b.colMesh;
        }
        foreach (BoxCollider dc in b.decks) if (dc != null) UnityEngine.Object.Destroy(dc);
        b.decks.Clear();
        if (b.kind.id == "qp" || b.kind.id == "hp")
        {
            float w = b.p[0], h = b.p[1], r = PipeR(b.p[1], b.p[2]);
            float deck = b.kind.id == "qp" ? b.p[3] : b.p[4];
            if (deck > 0.01f)
            {
                int sides = b.kind.id == "hp" ? 2 : 1;
                for (int s = 0; s < sides; s++)
                {
                    float side = s == 0 ? 1f : -1f, z0 = b.kind.id == "hp" ? b.p[3] * 0.5f + r : r;
                    BoxCollider dc = b.go.AddComponent<BoxCollider>();
                    dc.center = new Vector3(0f, h * 0.5f, side * (z0 + deck * 0.5f)); dc.size = new Vector3(w, h, deck);
                    dc.enabled = b != ghost; b.decks.Add(dc);
                }
            }
        }
        if (b.kind.id == "box" || b.kind.id == "rail")
        {
            foreach (BoxCollider bc in b.boxes) if (bc != null) UnityEngine.Object.Destroy(bc);
            b.boxes.Clear();
            bool on = b != ghost;
            foreach (Bounds bb in mb.boxes) { BoxCollider bc = b.go.AddComponent<BoxCollider>(); bc.center = bb.center; bc.size = bb.size; bc.enabled = on; b.boxes.Add(bc); }
        }
    }
    static void Cols(Block b, bool on)
    {
        if (b == null) return;
        if (b.mc != null) b.mc.enabled = on;
        foreach (BoxCollider bc in b.boxes) if (bc != null) bc.enabled = on;
        foreach (BoxCollider bc in b.decks) if (bc != null) bc.enabled = on;
    }
    void Remove(Block b)
    {
        if (b == null) return;
        if (b.mf != null && b.mf.sharedMesh != null) UnityEngine.Object.Destroy(b.mf.sharedMesh);
        if (b.colMesh != null) UnityEngine.Object.Destroy(b.colMesh);
        if (b.go != null) UnityEngine.Object.Destroy(b.go);
        blocks.Remove(b);
        if (sel == b) sel = null;
    }
    Block Duplicate(Block b)
    {
        Vector3 off = b.go.transform.right * Mathf.Max(1f, Size(b).x + 0.5f);
        Block n = AddBlock(b.kind, b.go.transform.position + off, b.go.transform.eulerAngles.y, true);
        n.p = (float[])b.p.Clone(); n.look0 = b.look0; n.look1 = b.look1; Rebuild(n); ApplyLook(n, null);
        return n;
    }
    Vector3 Size(Block b) { return b.mf != null && b.mf.sharedMesh != null ? b.mf.sharedMesh.bounds.size : Vector3.one; }

    // ---------- the shapes (local space: x = across, y = up, z = forward; the ground is at y = 0) ----------
    static void Build(string id, float[] p, MeshBuilder mb)
    {
        switch (id)
        {
            case "qp": QuarterPipe(mb, p[0], p[1], PipeR(p[1], p[2]), p[3], 0f, false); break;
            case "hp":
                {
                    float w = p[0], h = p[1], r = PipeR(p[1], p[2]), flat = p[3], deck = p[4];
                    QuarterPipe(mb, w, h, r, deck, flat * 0.5f, false);
                    QuarterPipe(mb, w, h, r, deck, flat * 0.5f, true);
                    break;
                }
            case "spine":
                {
                    float w = p[0], h = p[1], r = Mathf.Max(p[2], h);
                    float zt = Mathf.Sqrt(Mathf.Max(0f, r * r - (r - h) * (r - h)));      // the length of one curve
                    QuarterPipe(mb, w, h, r, 0f, -zt, false);
                    QuarterPipe(mb, w, h, r, 0f, -zt, true);
                    break;
                }
            case "kicker": Ramp(mb, p[0], p[1], p[2], p[3]); break;
            case "bank": Ramp(mb, p[0], p[1], p[2], 0f); break;
            case "box": mb.Box(new Vector3(-p[1] * 0.5f, 0f, -p[0] * 0.5f), new Vector3(p[1] * 0.5f, p[2], p[0] * 0.5f), 0); break;
            case "rail":
                {
                    float len = p[0], h = p[1], bw = p[2];
                    mb.Box(new Vector3(-bw * 0.5f, h - bw, -len * 0.5f), new Vector3(bw * 0.5f, h, len * 0.5f), 1);
                    int posts = Mathf.Max(2, Mathf.CeilToInt(len / 2.5f) + 1);
                    for (int i = 0; i < posts; i++)
                    {
                        float z = Mathf.Lerp(-len * 0.5f + 0.15f, len * 0.5f - 0.15f, i / (float)(posts - 1));
                        mb.Box(new Vector3(-0.025f, 0f, z - 0.025f), new Vector3(0.025f, h - bw, z + 0.025f), 1);
                    }
                    break;
                }
            case "stairs":
                {
                    int n = Mathf.Max(1, Mathf.RoundToInt(p[0])); float w = p[1], sh = p[2], sd = p[3], land = p[4];
                    float total = n * sd;
                    for (int i = 0; i < n; i++)
                        mb.Box(new Vector3(-w * 0.5f, 0f, i * sd), new Vector3(w * 0.5f, (i + 1) * sh, total + land), 0);
                    if (p[5] > 0.5f)
                    {   // the handrail: a bar along the slope, 0.8 m over the step noses, on the right side
                        float x = w * 0.5f - 0.1f, rh = 0.8f;
                        Vector3 a = new Vector3(x, sh + rh, 0.05f), b = new Vector3(x, n * sh + rh, total - 0.05f);
                        mb.Bar(a, b, 0.06f, 1);
                        mb.Box(new Vector3(x - 0.025f, sh, 0.02f), new Vector3(x + 0.025f, sh + rh, 0.07f), 1);
                        mb.Box(new Vector3(x - 0.025f, n * sh, total - 0.08f), new Vector3(x + 0.025f, n * sh + rh, total - 0.03f), 1);
                    }
                    break;
                }
            case "start":
                {   // a flat arrow on the ground
                    mb.Box(new Vector3(-0.35f, 0f, -0.6f), new Vector3(0.35f, 0.03f, 0.3f), 0);
                    mb.Box(new Vector3(-0.12f, 0f, 0.3f), new Vector3(0.12f, 0.03f, 0.9f), 0);
                    break;
                }
        }
    }
    // the curve radius of a pipe: the height without the straight-up part (so the curve always ends vertical)
    static float PipeR(float h, float vert) { return Mathf.Max(0.3f, h - Mathf.Clamp(vert, 0f, h - 0.3f)); }
    // a quarter pipe that rises toward +z (or -z when back), starting at z = start
    static void QuarterPipe(MeshBuilder mb, float w, float h, float r, float deck, float start, bool back)
    {
        List<Vector2> pr = new List<Vector2>();      // (z, y) along the riding surface
        float thMax = h <= r ? Mathf.Acos(1f - h / r) : 1.5707963f;
        int seg = Mathf.Clamp(Mathf.CeilToInt(thMax * 57.29578f / 4f), 4, 40);
        for (int i = 0; i <= seg; i++)
        {
            float th = thMax * i / seg;
            pr.Add(new Vector2(r * Mathf.Sin(th), r - r * Mathf.Cos(th)));
        }
        float zTop = pr[pr.Count - 1].x;
        if (h > r + 0.001f) pr.Add(new Vector2(zTop, h));          // straight up (vert) above the curve
        if (deck > 0.001f) pr.Add(new Vector2(zTop + deck, h));
        float zEnd = pr[pr.Count - 1].x;
        float s = back ? -1f : 1f;
        Func<Vector2, float, Vector3> P = (v, x) => new Vector3(x, v.y, s * (start + v.x));
        mb.Extrude(pr, -w * 0.5f, w * 0.5f, P, back, new Vector2(zEnd, 0f));
    }
    static void Ramp(MeshBuilder mb, float w, float h, float len, float curve)
    {
        List<Vector2> pr = new List<Vector2>();
        int seg = curve > 0.01f ? 16 : 1;
        for (int i = 0; i <= seg; i++)
        {
            float t = i / (float)seg;
            float y = Mathf.Lerp(t, t * t, curve) * h;           // curve 0 = straight, 1 = a curve that gets steeper
            pr.Add(new Vector2(t * len - len * 0.5f, y));
        }
        Func<Vector2, float, Vector3> P = (v, x) => new Vector3(x, v.y, v.x);
        mb.Extrude(pr, -w * 0.5f, w * 0.5f, P, false, new Vector2(len * 0.5f, 0f));
    }

    // ===================================================================================== play and edit
    void SetEditing(bool on)
    {
        TimeMan tm = null; try { tm = TimeMan.main; } catch (Exception) { }
        if (on)
        {
            if (tm != null) { oldPause = tm.uiPauseTimeScale; tm.uiPauseTimeScale = 0f; }
            Editing = true;
            if (sel == null && ghost == null) placing = false;
        }
        else
        {
            CancelPlace();
            Editing = false;
            if (tm != null) tm.uiPauseTimeScale = 1f;
        }
    }
    void Play()
    {
        if (!Active) return;
        Block st = blocks.Find(x => x.kind.id == "start");
        Vector3 pos; float yaw;
        if (st != null) { pos = st.go.transform.position; yaw = st.go.transform.eulerAngles.y; }
        else if (levelMode) { SetEditing(false); status = "Play: skate! P = back to the editor."; return; }
        else { pos = origin; yaw = camYaw; }
        SetEditing(false);
        Teleport(pos + Vector3.up * 0.3f, Quaternion.Euler(0f, yaw, 0f));
        status = "Play: skate! P = back to the editor.";
    }
    void Teleport(Vector3 pos, Quaternion rot)
    {
        try { ThePlayer.instance.SetPositionTeleport(pos, rot, true); }
        catch (Exception e) { status = "Could not move the skater: " + e.Message; }
    }
    void BackToEdit()
    {
        SetEditing(true);
        status = "Edit mode. The game is stopped.";
    }

    // ===================================================================================== pipes: the guide line
    // Each pipe wall (a quarter pipe, or each side of a half pipe) has a guide line: a vertical plane a little inside
    // its lip. In the air near the wall, the skater is held to the inside of that plane like on a string: a soft spring
    // takes away any speed that would carry the skater past it (toward the lip edge and the deck), and brings the
    // skater back if they are past it. Speed along the wall and speed into the pipe stay. It works for any take-off:
    // up the vertical part, an ollie just before the lip, or a hop on the curve, and at any speed.
    // If the speed alone cannot keep the skater inside (a hard hit), the position is moved back too.
    // A jump that starts on the deck (the skater wants to drop in) is not held.
    public bool vertAssist = true;
    public float guideIn = 0.25f;        // the line is this far inside the lip (m)
    public float guidePull = 8f;        // how hard the spring pulls (1/s)
    bool wasAir, holding; string guideInfo = "";
    bool launchOnDeck, gndOnDeck; int airSteps;
    Vector3 rideUp;                     // the board's up at take-off: kept in the air (spins around it stay free)
    public float airMax = 3f;           // the highest a pipe air goes over the lip (m)
    // the nearest pipe wall to a point: how far out past the line (+) or inside it (-), and the flat direction into the pipe
    string why = "";
    Block guideBlock;
    bool Guide(Vector3 pos, out float d, out Vector3 inward, out float aboveLip)
    {
        d = 0f; inward = Vector3.forward; aboveLip = 0f; bool any = false; float best = 1e30f; guideBlock = null; why = blocks.Exists(x => x.kind.id == "qp" || x.kind.id == "hp") ? "?" : "no pipes";
        foreach (Block b in blocks)
        {
            if (b.go == null || (b.kind.id != "qp" && b.kind.id != "hp")) continue;
            Transform t = b.go.transform;
            Vector3 p = t.InverseTransformPoint(pos);
            float w = b.p[0], h = b.p[1], r = PipeR(b.p[1], b.p[2]);
            if (Mathf.Abs(p.x) > w * 0.5f + 1.5f) { why = "beside the pipe"; continue; }
            int sides = b.kind.id == "hp" ? 2 : 1;
            for (int s = 0; s < sides; s++)
            {
                float side = s == 0 ? 1f : -1f;
                float zLip = b.kind.id == "hp" ? side * (b.p[3] * 0.5f + r) : r;
                float dd = (p.z - zLip) * side + guideIn;      // + = past the line (toward the deck)
                float deck = b.kind.id == "hp" ? b.p[4] : b.p[3];
                if (dd < -4f) { why = "far inside the pipe"; continue; }
                if (dd > deck + 3f) { why = "far over the deck"; continue; }
                if (p.y < h * 0.2f) { why = "low on the curve"; continue; }
                float score = Mathf.Abs(dd);
                if (score < best) { best = score; d = dd; inward = Flat(t.TransformDirection(new Vector3(0f, 0f, -side))); aboveLip = p.y - h; any = true; guideBlock = b; }
            }
        }
        return any;
    }
    static System.Reflection.FieldInfo smoothPosF, rbField;
    // a test log of the pipe guide (ShoeMod/pipe_log.txt): each physics step in the air, and a short time after
    public bool logOn = true;
    StringBuilder pipeLog = new StringBuilder(); int logLines; float lastAirT = -10f, nextFlush;
    static string V3(Vector3 v) { return v.x.ToString("0.00") + "," + v.y.ToString("0.00") + "," + v.z.ToString("0.00"); }
    void LogLine(SkatePlayer sp, Rigidbody rb, bool air, bool near, float d, float above)
    {
        if (air) lastAirT = Time.time;
        if (Time.time - lastAirT > 0.5f || logLines > 20000) return;
        Block nb = null; float nd = 1e30f;
        foreach (Block b in blocks) if (b.go != null && (b.kind.id == "qp" || b.kind.id == "hp")) { float dist = (b.go.transform.position - sp.transform.position).sqrMagnitude; if (dist < nd) { nd = dist; nb = b; } }
        Vector3 lp = nb != null ? nb.go.transform.InverseTransformPoint(sp.transform.position) : Vector3.zero;
        pipeLog.Append(Time.time.ToString("0.000")).Append(air ? " AIR " : " GND ").Append(riding ? "RIDE " : "").Append(near ? "near d=" + d.ToString("0.00") + " up=" + above.ToString("0.00") : "free(" + why + ")")
            .Append(" sp=").Append(V3(sp.transform.position)).Append(" board=").Append(sp.board != null ? V3(sp.board.transform.position) : "-")
            .Append(" rb=").Append(rb != null ? V3(rb.position) + (rb.isKinematic ? " kin" : " dyn") + " rbv=" + V3(rb.velocity) : "-")
            .Append(" vel=").Append(V3(sp.skPhysics.velocity))
            .Append(" pipe=").Append(nb != null ? nb.kind.id + " local=" + V3(lp) + " w=" + nb.p[0].ToString("0.0") + " h=" + nb.p[1].ToString("0.0") + " r=" + PipeR(nb.p[1], nb.p[2]).ToString("0.00") : "-")
            .Append('\n');
        logLines++;
        if (Time.unscaledTime >= nextFlush)
        {
            nextFlush = Time.unscaledTime + 1f;
            try { File.WriteAllText(Path.Combine(Path.Combine(Application.persistentDataPath, "ShoeMod"), "pipe_log.txt"), pipeLog.ToString()); } catch (Exception) { }
        }
    }
    void Nudge(SkatePlayer sp, Vector3 delta)
    {   // move the skater: the body, the saved physics pose (local to the parent) and the smoothed body point
        sp.transform.position += delta;
        SkateInterpolatedTransform s = sp.GetComponent<SkateInterpolatedTransform>();
        if (s != null)
        {
            Transform par = sp.transform.parent;
            Vector3 ld = par != null ? par.InverseTransformVector(delta) : delta;
            TransformState c = s.current; c.pos = (Vector3)c.pos + ld; s.current = c;
            TransformState l = s.last; l.pos = (Vector3)l.pos + ld; s.last = l;
        }
        try
        {
            if (smoothPosF == null) smoothPosF = typeof(SkatePlayer).GetField("skaterBodyParentSmoothPos", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (smoothPosF != null) smoothPosF.SetValue(sp, (Vector3)smoothPosF.GetValue(sp) + delta);
        }
        catch (Exception) { }
    }
    // ---------- the vert ride ----------
    // The log showed why holding the speed did not work: in the air the game moves the skater by itself (its pop off
    // the wall pushes inward, and as you come down it pulls you toward the deck), and speed changes from the mod were
    // lost. So in a pipe air the mod now takes over the flight itself, like a string: the game's physics step is off,
    // and the mod moves the skater on a clean arc. The speed along the wall and up stays as it was at take-off, the
    // distance from the wall stays fixed (it eases to the line just inside the lip if you were past it), and the pull
    // down is the game's gravity. Below the lip, on the way down, the game gets the skater back, with the same speed,
    // just inside the wall, so the board comes down onto the curve.
    public float airGravity = 1f;       // the pull down in a pipe air, times the game's gravity
    bool riding; float rideT0, rideVa, rideVy0, rideG, rideD0, rideD1, rideLipY, rideTop;
    Vector3 rideP0, rideA, rideIn;
    void RideStart(SkatePlayer sp, Vector3 pos, float d, Vector3 inward, float above)
    {
        Vector3 v = sp.skPhysics.velocity;
        rideIn = inward; rideA = Vector3.Cross(Vector3.up, inward).normalized;
        rideVa = Vector3.Dot(v, rideA); rideVy0 = v.y;
        float g = 9.81f; try { g = sp.skPhysics.gravity.magnitude; } catch (Exception) { }
        if (g < 1f) g = 9.81f;
        rideG = g * airGravity;
        // not higher than the air limit over the lip (the pop off the wall adds speed each time, and it grew without end)
        float room = Mathf.Max(0.1f, (pos.y - above) + airMax - pos.y);
        rideVy0 = Mathf.Min(rideVy0, Mathf.Sqrt(2f * rideG * room));
        rideP0 = pos; rideT0 = Time.time;
        rideD0 = d; rideD1 = Mathf.Min(d, 0f);          // d is from the line (+ = past it): never past the line
        rideLipY = pos.y - above;
        rideTop = pos.y + rideVy0 * rideVy0 / (2f * rideG);
        rideUp = sp.transform.up;
        riding = true;
        try { sp.disablePhysics = true; } catch (Exception) { }
    }
    void RideEnd(SkatePlayer sp, Vector3 vel)
    {
        riding = false; rideEndT = Time.time;
        try { sp.disablePhysics = false; sp.skPhysics.velocity = vel; } catch (Exception) { }
    }
    Collider[] ov = new Collider[16];
    bool InBlock(Vector3 pos)
    {
        int n = UnityEngine.Physics.OverlapSphereNonAlloc(pos, 0.14f, ov, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) if (ov[i] != null && ov[i].enabled && ov[i].transform.IsChildOf(root.transform) && ov[i].gameObject != floor) return true;
        return false;
    }
    // each frame and each physics step while riding: the place on the arc now
    void RideApply(SkatePlayer sp)
    {
        float t = Time.time - rideT0;
        float blend = Mathf.Clamp01(t / 0.25f); blend = blend * blend * (3f - 2f * blend);
        float dNow = Mathf.Lerp(rideD0, rideD1, blend);
        float dRate = t < 0.25f ? (rideD1 - rideD0) * 6f * (t / 0.25f) * (1f - t / 0.25f) / 0.25f : 0f;
        Vector3 pos = rideP0 + rideA * (rideVa * t) + Vector3.up * (rideVy0 * t - 0.5f * rideG * t * t) - rideIn * (dNow - rideD0);
        Vector3 vel = rideA * rideVa + Vector3.up * (rideVy0 - rideG * t) - rideIn * dRate;
        bool down = vel.y < 0f;
        if ((down && pos.y < rideLipY - 0.5f) || t > 8f) { RideEnd(sp, vel); guideInfo = "back to the game"; return; }
        if (InBlock(pos)) { RideEnd(sp, vel); guideInfo = "touched a block: back to the game"; return; }
        // the board keeps the lean it had at take-off (in the air the game tips it over toward the deck, and then it
        // caught the lip); a turn around that line (a spin) stays as the game makes it
        Quaternion fix = Quaternion.FromToRotation(sp.transform.up, rideUp);
        Quaternion rot = fix * sp.transform.rotation;
        sp.transform.SetPositionAndRotation(pos, rot);
        SkateInterpolatedTransform s = sp.GetComponent<SkateInterpolatedTransform>();
        if (s != null)
        {
            Transform par = sp.transform.parent;
            Vector3 lp = par != null ? par.InverseTransformPoint(pos) : pos;
            Quaternion lr = par != null ? Quaternion.Inverse(par.rotation) * rot : rot;
            TransformState c = s.current; c.pos = lp; c.rot = lr; s.current = c;
            TransformState l = s.last; l.pos = lp; l.rot = lr; s.last = l;
        }
        sp.skPhysics.velocity = vel;
        guideInfo = "on the line  " + (pos.y - rideLipY).ToString("0.0") + " m over the lip";
    }
    public void Physics()
    {
        SkatePlayer sp = null; try { sp = ThePlayer.SkatePlayer; } catch (Exception) { }
        if (sp == null) return;
        if (!Active || Editing || !vertAssist || root == null)
        {
            MoveTools.VibCapNow = 1f;
            if (deckOffBlock != null) { foreach (BoxCollider bc in deckOffBlock.decks) if (bc != null) bc.enabled = true; deckOffBlock = null; }
            if (riding) RideEnd(sp, sp.skPhysics.velocity);
            holding = false; guideInfo = ""; return;
        }
        Rigidbody rb = null;
        bool air = false; try { air = ThePlayer.IS_AIRBORNE; } catch (Exception) { }
        Vector3 pos = sp.transform.position;
        float d, above; Vector3 inward;
        bool near = Guide(pos, out d, out inward, out above);
        if (logOn) LogLine(sp, rb, air, near, d, above);
        DeckControl(sp, air, near, d, above, inward);
        MoveTools.VibCapNow = (riding || Time.time - rideEndT < 0.4f) ? 0.15f : 1f;
        if (riding) { RideApply(sp); wasAir = air; return; }
        if (!air) { airSteps = 0; gndOnDeck = near && above > -0.3f && sp.transform.up.y > 0.7f; }   // on the deck now (the board is flat)
        else airSteps++;
        if (air && !wasAir) launchOnDeck = gndOnDeck;                                // the jump starts on the deck
        wasAir = air;
        if (!air || !near || launchOnDeck) { guideInfo = air ? (launchOnDeck ? "from the deck: free" : "free (" + why + ")") : "on the ground"; return; }
        // a pipe air: going up, near a wall, not far inside the pipe
        if (airSteps >= 2 && sp.skPhysics.velocity.y > 0.5f && d > -2.5f && above > -3f) { RideStart(sp, pos, d, inward, above); RideApply(sp); }
    }
    // Grinds on the lip: the game grinds on any edge near the board, and in a pipe air the board is near the lip edge all
    // the time. So near the lip, from the wall side, the deck of that pipe is off (no edge, nothing to catch) unless the
    // board is set up for a lip grind: about flat (wheels down), along the lip (at most 30 degrees off), and at the top.
    // It is never off when you are on the deck or out over it.
    Block deckOffBlock; float rideEndT = -10f;
    void DeckControl(SkatePlayer sp, bool air, bool near, float d, float above, Vector3 inward)
    {
        bool off = false;
        if (near && guideBlock != null && d < 0.2f && above > -1.5f)
        {
            Vector3 up = sp.transform.up, fw = Flat(sp.transform.forward);
            Vector3 lipDir = Vector3.Cross(Vector3.up, inward).normalized;
            bool grindPose = up.y > 0.5f && Mathf.Abs(Vector3.Dot(fw, lipDir)) > 0.87f && above > -0.25f;
            bool vertNow = riding || Time.time - rideEndT < 0.5f || (air && d < 0.2f) || (!air && up.y < 0.4f && above > -1f);
            off = vertNow && !grindPose;
        }
        Block want = off ? guideBlock : null;
        if (want != deckOffBlock)
        {
            if (deckOffBlock != null) foreach (BoxCollider bc in deckOffBlock.decks) if (bc != null) bc.enabled = true;
            if (want != null) foreach (BoxCollider bc in want.decks) if (bc != null) bc.enabled = false;
            deckOffBlock = want;
        }
    }

    // every frame too, so that the arc is smooth at any frame rate
    void RideFrame()
    {
        if (!riding) return;
        SkatePlayer sp = null; try { sp = ThePlayer.SkatePlayer; } catch (Exception) { }
        if (sp == null) { riding = false; return; }
        RideApply(sp);
    }

    // ===================================================================================== each frame
    bool hooked;
    public void Update()
    {
        try
        {
            if (!hooked) { hooked = true; Camera.onPreCull += OnPreCull; Camera.onPostRender += OnPostRender; }
            if (SceneLoads.count != seenLoads)
            {
                bool first = seenLoads < 0; seenLoads = SceneLoads.count;
                if (!first && Active)
                {
                    if (levelMode) RemoveLevelBlocks();
                    else { haveBack = false; Close(); status = "The level changed, so the editor closed."; }
                }
                pendingAuto = true; autoAt = Time.unscaledTime + 2.5f;
            }
            if (pendingAuto && Time.unscaledTime >= autoAt && !MultiScene.isLoading && ThePlayer.instance != null)
            {   // the blocks you saved for this level come back by themselves
                pendingAuto = false;
                bool sp0 = false; try { sp0 = ThePlayer.SkatePlayer != null; } catch (Exception) { }
                if (autoLoad && !Active && sp0 && File.Exists(LevelFile())) { OpenLevel(false); status = "Your blocks for this level are back (" + blocks.Count + ")."; }
            }
            if (!Active) return;
            RideFrame();
            if (sessionOpen && Input.GetKeyDown(KeyCode.P) && !typing && !MiniText.AnyFocus) { if (Editing) Play(); else BackToEdit(); }
            if (Editing)
            {
                TimeMan tm = null; try { tm = TimeMan.main; } catch (Exception) { }
                if (tm != null) tm.uiPauseTimeScale = 0f;
                EditControls();
            }
            else if (!levelMode && Time.unscaledTime >= nextFall)
            {   // fell off the floor, or the game sent the skater back to the hidden level: go to the start point
                nextFall = Time.unscaledTime + 0.5f;
                SkatePlayer sp = null; try { sp = ThePlayer.SkatePlayer; } catch (Exception) { }
                if (sp != null)
                {
                    Vector3 d = sp.transform.position - origin;
                    if (d.y < -25f || Mathf.Abs(d.x) > FloorSize * 0.5f + 50f || Mathf.Abs(d.z) > FloorSize * 0.5f + 50f) Play();
                }
            }
        }
        catch (Exception e) { status = "Editor error: " + e.Message; }
    }

    void EditControls()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        if (typing) return;
        // the camera: hold the right mouse button to look; WASD to move, Q/E down and up, Shift fast, Ctrl slow
        if (Input.GetMouseButton(1)) { camYaw += Input.GetAxisRaw("Mouse X") * lookSpeed; camPitch = Mathf.Clamp(camPitch - Input.GetAxisRaw("Mouse Y") * lookSpeed, -89f, 89f); }
        Quaternion r = Quaternion.Euler(camPitch, camYaw, 0f);
        Vector3 m = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) m += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) m += Vector3.back;
        if (Input.GetKey(KeyCode.A)) m += Vector3.left;
        if (Input.GetKey(KeyCode.D)) m += Vector3.right;
        Vector3 v = r * m;
        if (Input.GetKey(KeyCode.E)) v += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) v += Vector3.down;
        camPos += v * camSpeed * dt * (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f) * (Input.GetKey(KeyCode.LeftControl) ? 0.25f : 1f);
        float wh = Input.mouseScrollDelta.y;
        if (wh != 0f && !overUI) camSpeed = Mathf.Clamp(camSpeed * (wh > 0 ? 1.2f : 0.83f), 0.5f, 80f);
        // keys for the selected block or the block being placed
        Block b = placing ? ghost : sel;
        if (b != null)
        {
            if (Input.GetKeyDown(KeyCode.R)) Turn(b, Input.GetKey(KeyCode.LeftShift) ? -turnStep : turnStep);
            if (!placing)
            {
                if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace)) { Remove(b); return; }
                if (Input.GetKeyDown(KeyCode.D) && Input.GetKey(KeyCode.LeftControl)) sel = Duplicate(b);
                float st = Mathf.Max(0.05f, snap);
                Vector3 f = Flat(r * Vector3.forward), rt = Flat(r * Vector3.right);
                if (Input.GetKeyDown(KeyCode.UpArrow)) Move(b, f * st);
                if (Input.GetKeyDown(KeyCode.DownArrow)) Move(b, -f * st);
                if (Input.GetKeyDown(KeyCode.RightArrow)) Move(b, rt * st);
                if (Input.GetKeyDown(KeyCode.LeftArrow)) Move(b, -rt * st);
                if (Input.GetKeyDown(KeyCode.PageUp)) Move(b, Vector3.up * st);
                if (Input.GetKeyDown(KeyCode.PageDown)) Move(b, Vector3.down * st);
            }
        }
    }
    static Vector3 Flat(Vector3 v) { v.y = 0f; if (v.sqrMagnitude < 1e-6f) return Vector3.forward; v.Normalize(); return v; }
    void Turn(Block b, float d) { Vector3 e = b.go.transform.eulerAngles; b.go.transform.rotation = Quaternion.Euler(0f, Mathf.Round((e.y + d) / turnStep) * turnStep, 0f); }
    void Move(Block b, Vector3 d) { Vector3 p = b.go.transform.position + d; if (!levelMode) p.y = Mathf.Max(origin.y - 20f, p.y); b.go.transform.position = p; }
    float Snap(float v) { return snap > 0.001f ? Mathf.Round(v / snap) * snap : v; }
    Vector3 SnapXZ(Vector3 p) { Vector3 l = p - origin; l.x = Snap(l.x); l.z = Snap(l.z); return origin + l; }

    // ---------- the camera: set just before the frame is drawn, put back after ----------
    bool camSet; Vector3 keepPos; Quaternion keepRot; Camera keepCam;
    Vector3 viewPos; Quaternion viewRot = Quaternion.identity; float viewFov = 60f, viewAspect = 1.78f;
    void OnPreCull(Camera c)
    {
        if (!Active || !Editing || c == null || c != Camera.main || camSet) return;
        keepCam = c; keepPos = c.transform.position; keepRot = c.transform.rotation;
        Quaternion q = Quaternion.Euler(camPitch, camYaw, 0f);
        c.transform.SetPositionAndRotation(camPos, q);
        viewPos = camPos; viewRot = q; viewFov = c.fieldOfView; viewAspect = c.aspect;
        camSet = true;
    }
    void OnPostRender(Camera c)
    {
        if (!camSet || c != keepCam) return;
        c.transform.SetPositionAndRotation(keepPos, keepRot);
        camSet = false;
    }

    // ---------- the mouse in the world ----------
    Ray MouseRay(Vector2 mp, float W, float H)
    {
        float t = Mathf.Tan(viewFov * 0.5f * 0.01745329f);
        Vector3 d = new Vector3((mp.x / W * 2f - 1f) * t * viewAspect, (1f - mp.y / H * 2f) * t, 1f);
        return new Ray(viewPos, viewRot * d.normalized);
    }
    RaycastHit[] rh = new RaycastHit[32];
    // the nearest hit on the floor or a block (not the block that is ignored)
    bool Pick(Ray ray, Block ignore, out RaycastHit hit, out Block hb)
    {
        hit = new RaycastHit(); hb = null;
        if (root == null) return false;
        int n = UnityEngine.Physics.RaycastNonAlloc(ray, rh, 2000f, ~0, QueryTriggerInteraction.Ignore);
        float best = 1e30f; bool any = false;
        for (int i = 0; i < n; i++)
        {
            Collider c = rh[i].collider;
            if (c == null) continue;
            bool mine = c.transform.IsChildOf(root.transform);
            if (!mine && (!levelMode || IsSkater(c))) continue;
            if (ignore != null && c.transform.IsChildOf(ignore.go.transform)) continue;
            if (rh[i].distance < best) { best = rh[i].distance; hit = rh[i]; any = true; }
        }
        if (any) { Transform ht = hit.collider.transform; if (ht.IsChildOf(root.transform)) hb = blocks.Find(x => x.go != null && ht.IsChildOf(x.go.transform)); }
        if (!any)
        {   // no collider (for example the start point): the floor height
            Plane pl = new Plane(Vector3.up, new Vector3(0f, planeY, 0f)); float d;
            if (pl.Raycast(ray, out d)) { hit.point = ray.GetPoint(d); hit.normal = Vector3.up; return true; }
        }
        return any;
    }
    static bool IsSkater(Collider c)
    {
        Transform t = c.transform;
        try { if (ThePlayer.instance != null && t.IsChildOf(ThePlayer.instance.transform.root)) return true; } catch (Exception) { }
        try { if (ThePlayer.SkatePlayer != null && t.IsChildOf(ThePlayer.SkatePlayer.transform.root)) return true; } catch (Exception) { }
        return false;
    }
    // the start point has no collider: find it by its spot on the screen
    Block PickStart(Vector2 mp)
    {
        foreach (Block b in blocks)
        {
            if (b.kind.id != "start") continue;
            Vector3 s; if (Proj(b.go.transform.position, out s) && Vector2.Distance(mp, new Vector2(s.x, s.y)) < 30f) return b;
        }
        return null;
    }

    void StartPlace(Kind k)
    {
        CancelPlace();
        if (k.id == "start") { Block st = blocks.Find(x => x.kind.id == "start"); if (st != null) { sel = st; status = "There is one start point. Drag it to move it."; return; } }
        ghost = AddBlock(k, origin, camYaw, false);
        if (sel != null && sel.kind == k) { ghost.p = (float[])sel.p.Clone(); ghost.look0 = sel.look0; ghost.look1 = sel.look1; Rebuild(ghost); ApplyLook(ghost, null); }       // the same sizes as the selected one
        Cols(ghost, false);
        placing = true; sel = null;
        status = "Click to place the " + k.name.ToLower() + ". R = turn. Shift + click = place more. Right click or Cancel = stop.";
    }
    void CancelPlace()
    {
        if (ghost != null) Remove(ghost);
        ghost = null; placing = false;
    }
    void PlaceGhost()
    {
        if (ghost == null) return;
        Cols(ghost, true);
        Block placed = ghost; ghost = null; placing = false;
        sel = placed;
        if (keepPlacing) { Kind k = placed.kind; float yaw = placed.go.transform.eulerAngles.y; StartPlace(k); sel = placed; ghost.go.transform.rotation = Quaternion.Euler(0f, yaw, 0f); ghost.p = (float[])placed.p.Clone(); Rebuild(ghost); }
        else status = "Placed. Drag it to move it, R to turn it, or change its size on the right.";
    }

    // ===================================================================================== the screen
    Texture2D white; Material lineMat;
    void Fill(Rect r, Color c) { if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); } Color o = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = o; }
    float gW, gH; bool overUI, typing, nameFocus; int loadPage; bool loadOpen;
    List<Rect> uiRects = new List<Rect>();
    bool OverUI(Vector2 mp) { foreach (Rect r in uiRects) if (r.Contains(mp)) return true; return false; }

    public void DrawOverlay()
    {
        if (!Active || !sessionOpen) return;
        Event e = Event.current;
        float s = Mathf.Clamp(Screen.height / 1080f, 0.6f, 3f);
        Matrix4x4 old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = Screen.height / s; gW = W; gH = H;
        Vector2 mp = e.mousePosition;
        uiRects.Clear();
        if (!Editing)
        {
            Rect pr = new Rect(10, 10, 520, 30); Fill(pr, new Color(0, 0, 0, 0.6f));
            GUI.Label(new Rect(18, 15, 510, 22), "LEVEL EDITOR: playing.   P = back to editing.   " + (status.StartsWith("Play") ? "" : status));
            if (vertAssist && guideInfo.Length > 0) { Fill(new Rect(10, 42, 520, 26), new Color(0, 0, 0, 0.5f)); GUI.Label(new Rect(18, 45, 510, 22), "Vert assist: " + guideInfo); }
            GUI.matrix = old; return;
        }
        // the panels (their rects first, so that clicks on them do not go into the world)
        Rect top = new Rect(0, 0, W, 44), bottom = new Rect(0, H - 32, W, 32), side = new Rect(W - 330, 54, 320, 480);
        Rect palette = new Rect(0, 176, 150, 30 + kinds.Length * 40 + 44);
        uiRects.Add(palette);
        uiRects.Add(top); uiRects.Add(bottom);
        Block info = placing ? ghost : sel;
        if (info != null) uiRects.Add(side);
        Rect lookRect = new Rect(W - 760, 54, 420, 470);
        if (lookOpen && info != null) uiRects.Add(lookRect); else lookOpen = false;
        Rect floorBtn = new Rect(W - 330, H - 132, 320, 34);
        uiRects.Add(floorBtn);
        Rect loadRect = new Rect(380, 46, 360, 300);
        if (loadOpen) uiRects.Add(loadRect);
        Rect feel = new Rect(10, 80, 360, 88);
        if (vertAssist) uiRects.Add(feel);
        overUI = OverUI(mp);

        // the world: the outlines, and the mouse
        DrawOutlines(e);
        WorldMouse(e, mp, W, H);

        // top bar
        Fill(top, new Color(0f, 0f, 0f, 0.72f));
        float x = 10;
        GUI.Label(new Rect(x, 12, 110, 22), levelMode ? "LEVEL BLOCKS" : "PARK EDITOR"); x += 110;
        if (GUI.Button(new Rect(x, 7, 90, 30), "Play (P)")) Play(); x += 100;
        typing = nameFocus || lookFocus || editFocus;
        if (levelMode)
        {   // a real level: one file for this level
            GUI.Label(new Rect(x, 12, 208, 22), "Level: " + levelScene); x += 212;
            if (GUI.Button(new Rect(x, 7, 60, 30), "Save")) Save(); x += 64;
            if (GUI.Button(new Rect(x, 7, 70, 30), "Revert")) { if (File.Exists(LevelFile())) Load(LevelFile()); else ClearBlocks(); status = "Back to the saved blocks."; } x += 74;
            if (GUI.Button(new Rect(x, 7, 60, 30), "Clear")) { ClearBlocks(); status = "All blocks taken away (Save to keep it so)."; } x += 74;
        }
        else
        {
            GUI.Label(new Rect(x, 12, 40, 22), "Name"); x += 42;
            layoutName = MiniText.Box(new Rect(x, 7, 160, 30), ref nameFocus, layoutName, 40); x += 166;
            if (GUI.Button(new Rect(x, 7, 60, 30), "Save")) Save(); x += 64;
            if (GUI.Toggle(new Rect(x, 7, 70, 30), loadOpen, new GUIContent("Load"), GUI.skin.button) != loadOpen) { loadOpen = !loadOpen; if (loadOpen) ScanParks(); } x += 74;
            if (GUI.Button(new Rect(x, 7, 60, 30), "New")) { ClearBlocks(); AddBlock(KindOf("start"), origin, camYaw, true); status = "A new, empty park."; } x += 74;
        }
        GUI.Label(new Rect(x, 12, 40, 22), "Grid"); x += 40;
        float[] snaps = { 0f, 0.25f, 0.5f, 1f };
        foreach (float sv in snaps) { if (GUI.Toggle(new Rect(x, 7, 48, 30), Mathf.Abs(snap - sv) < 0.001f, new GUIContent(sv == 0f ? "Off" : sv + ""), GUI.skin.button)) snap = sv; x += 50; }
        x += 8;
        GUI.Label(new Rect(x, 12, 40, 22), "Turn"); x += 40;
        float[] turns = { 15f, 45f, 90f };
        foreach (float tv in turns) { if (GUI.Toggle(new Rect(x, 7, 46, 30), Mathf.Abs(turnStep - tv) < 0.001f, new GUIContent(tv + "°"), GUI.skin.button)) turnStep = tv; x += 48; }
        x += 10;
        vertAssist = GUI.Toggle(new Rect(x, 12, 130, 22), vertAssist, new GUIContent(" Vert assist"), GUI.skin.toggle);
        if (new Rect(x, 7, 130, 30).Contains(mp)) { Fill(new Rect(x, 46, 520, 26), new Color(0, 0, 0, 0.8f)); GUI.Label(new Rect(x + 6, 49, 510, 22), "Vert assist: a pipe air flies on a line just inside the lip, straight up and down, back onto the wall."); }
        if (GUI.Button(new Rect(W - 130, 7, 120, 30), levelMode ? "Done" : "Close editor")) { Close(); GUI.matrix = old; return; }
        if (status.Length > 0) { Fill(new Rect(10, 48, 360, 26), new Color(0, 0, 0, 0.5f)); GUI.Label(new Rect(16, 51, 354, 22), status); }

        if (loadOpen) DrawLoad(loadRect);
        if (vertAssist && !loadOpen)
        {   // the pipe guide line: how far inside the lip, and how hard it holds
            Fill(feel, new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(new Rect(feel.x + 8, feel.y + 6, 150, 22), "Pipe line inside lip");
            guideIn = Mathf.Round(GUI.HorizontalSlider(new Rect(feel.x + 160, feel.y + 12, 130, 16), guideIn, 0f, 1f) * 100f) / 100f;
            GUI.Label(new Rect(feel.x + 296, feel.y + 6, 60, 22), guideIn.ToString("0.00") + " m");
            GUI.Label(new Rect(feel.x + 8, feel.y + 32, 150, 22), "Pipe air gravity");
            airGravity = Mathf.Round(GUI.HorizontalSlider(new Rect(feel.x + 160, feel.y + 38, 130, 16), airGravity, 0.4f, 1.5f) * 20f) / 20f;
            GUI.Label(new Rect(feel.x + 296, feel.y + 32, 60, 22), (airGravity * 100f).ToString("0") + " %");
            GUI.Label(new Rect(feel.x + 8, feel.y + 58, 150, 22), "Pipe air height");
            airMax = Mathf.Round(GUI.HorizontalSlider(new Rect(feel.x + 160, feel.y + 64, 130, 16), airMax, 0.5f, 15f) * 4f) / 4f;
            GUI.Label(new Rect(feel.x + 296, feel.y + 58, 60, 22), airMax.ToString("0.00") + " m");
        }

        // the blocks, on the left
        Fill(palette, new Color(0f, 0f, 0f, 0.72f));
        float bx = palette.x + 10, by = palette.y + 6;
        GUI.Label(new Rect(bx, by, 140, 22), "BLOCKS");
        by += 24;
        string hover = ""; float hoverY = 0f;
        foreach (Kind k in kinds)
        {
            bool on = placing && ghost != null && ghost.kind == k;
            Rect br = new Rect(bx, by, 130, 36);
            if (br.Contains(mp)) { hover = k.name + ": " + k.help; hoverY = by; }
            if (GUI.Toggle(br, on, new GUIContent(k.name), GUI.skin.button) != on) { if (on) CancelPlace(); else StartPlace(k); }
            by += 40;
        }
        if (placing) { if (GUI.Button(new Rect(bx, by + 2, 130, 32), "Cancel")) CancelPlace(); }
        else GUI.Label(new Rect(bx, by + 2, 140, 40), "Click one, then\nclick in the world.");
        Fill(bottom, new Color(0f, 0f, 0f, 0.72f));
        GUI.Label(new Rect(10, H - 26, W - 20, 22), "Camera: hold right mouse to look, WASD move, Q/E down and up, Shift fast, Ctrl slow, wheel = speed.   Block: drag = move, Alt + drag = up and down, R = turn, arrows / PgUp / PgDn = step, Ctrl + D = copy, Del = remove.");
        if (hover.Length > 0) { Fill(new Rect(palette.xMax + 6, hoverY + 4, 640, 26), new Color(0, 0, 0, 0.85f)); GUI.Label(new Rect(palette.xMax + 12, hoverY + 7, 630, 22), hover); }

        // the selected block
        if (info != null) DrawSide(side, info);
        if (lookOpen && info != null) DrawLooks(lookRect, info);
        if (!levelMode && GUI.Button(floorBtn, "Floor look: " + (floorLook.Length > 0 ? floorLook : "grid"))) { floorPick = !floorPick; }
        if (floorPick) { Rect fr = new Rect(W - 760, H - 610, 420, 470); uiRects.Add(fr); DrawLooks(fr, null); }
        GUI.matrix = old;
    }

    void WorldMouse(Event e, Vector2 mp, float W, float H)
    {
        if (overUI && !dragging) return;
        Ray ray = MouseRay(mp, W, H);
        if (placing && ghost != null)
        {
            RaycastHit hit; Block hb;
            if (Pick(ray, ghost, out hit, out hb))
            {
                Vector3 p = SnapXZ(hit.point);
                p.y = (hit.normal.y > 0.7f ? hit.point.y : planeY) + Lift;     // just above the top of a block, or of the ground
                ghost.go.transform.position = p;
            }
            if (e.type == EventType.MouseDown && e.button == 0) { keepPlacing = e.shift; PlaceGhost(); e.Use(); }
            if (e.type == EventType.MouseUp && e.button == 1 && !rightDragged) { CancelPlace(); e.Use(); }
        }
        else
        {
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                nameFocus = false;
                RaycastHit hit; Block hb;
                Block st = PickStart(mp);
                if (st != null) { hb = st; hit = new RaycastHit(); hit.point = st.go.transform.position; }
                else Pick(ray, null, out hit, out hb);
                sel = hb;
                if (hb != null)
                {
                    dragging = true; dragUp = e.alt; dragLast = mp;
                    dragPlaneY = hb.go.transform.position.y;
                    Plane pl = new Plane(Vector3.up, new Vector3(0, dragPlaneY, 0)); float d;
                    dragOffset = pl.Raycast(ray, out d) ? hb.go.transform.position - ray.GetPoint(d) : Vector3.zero;
                }
                e.Use();
            }
            if (e.type == EventType.MouseDrag && dragging && sel != null)
            {
                if (dragUp)
                {
                    float dy = (dragLast.y - mp.y) * 0.02f; dragLast = mp;
                    Vector3 p = sel.go.transform.position; p.y = levelMode ? p.y + dy : Mathf.Max(origin.y - 20f, p.y + dy); dragPlaneY = p.y;
                    sel.go.transform.position = p;
                }
                else
                {
                    Plane pl = new Plane(Vector3.up, new Vector3(0, dragPlaneY, 0)); float d;
                    if (pl.Raycast(ray, out d)) { Vector3 p = SnapXZ(ray.GetPoint(d) + dragOffset); p.y = dragPlaneY; sel.go.transform.position = p; }
                }
                e.Use();
            }
            if (e.type == EventType.MouseUp && e.button == 0) dragging = false;
        }
        if (e.type == EventType.MouseDown && e.button == 1) rightDragged = false;
        if (e.type == EventType.MouseDrag && e.button == 1) rightDragged = true;
    }
    bool rightDragged;

    // ---------- looks (materials) from this level ----------
    Dictionary<string, Material> looks = new Dictionary<string, Material>();
    List<string> lookNames = new List<string>();
    bool lookOpen, floorPick, lookFocus; int lookSlot, lookPage, missingLooks; string lookFilter = "", floorLook = "";
    void FindLooks()
    {
        looks.Clear(); lookNames.Clear();
        Action<string, Material> add = (n, m) => { if (m != null && !looks.ContainsKey(n)) { looks[n] = m; lookNames.Add(n); } };
        // the materials of the level (from its hidden objects, or in a level from all of them)
        List<Renderer> src = offR;
        if (levelMode || src.Count == 0)
        {
            src = new List<Renderer>();
            foreach (Renderer r0 in UnityEngine.Object.FindObjectsOfType<Renderer>()) if (r0 != null && (root == null || !r0.transform.IsChildOf(root.transform))) src.Add(r0);
        }
        foreach (Renderer r in src)
        {
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            foreach (Material m in r.sharedMaterials)
            {
                if (m == null || m.shader == null) continue;
                string sn = m.shader.name.ToLower();
                if (sn.Contains("particle") || sn.Contains("skybox") || sn.Contains("/ui") || sn.StartsWith("ui") || sn.Contains("sprite") || sn.StartsWith("hidden")) continue;
                string n = m.name.Replace(" (Instance)", "");
                if (n.Length == 0) continue;
                add(n, m);
            }
        }
        lookNames.Sort(StringComparer.OrdinalIgnoreCase);
        // the editor's own looks first
        List<string> own = new List<string>();
        Action<string, Material> addOwn = (n, m) => { if (m != null && !looks.ContainsKey(n)) { looks[n] = m; own.Add(n); } };
        if (floorMat != null)
        {   // the grid on a block: the floor's grid, at the same size (1 m squares)
            Material g = new Material(floorMat); g.mainTextureScale = new Vector2(0.1f, 0.1f); addOwn("Editor: grid", g);
        }
        addOwn("Editor: start green", startMat);
        lookNames.InsertRange(0, own);
    }
    Material LookMat(string n, Material def) { Material m; return n.Length > 0 && looks.TryGetValue(n, out m) && m != null ? m : def; }
    void ApplyLook(Block b, MeshRenderer mr)
    {
        if (b == null || b.go == null || b.kind.id == "start") return;
        if (mr == null) mr = b.go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterials = new[] { LookMat(b.look0, blockMat), LookMat(b.look1, railMat) };
    }
    void SetFloorLook(string n)
    {
        floorLook = n;
        if (floor == null) return;
        Material m = n.Length > 0 ? LookMat(n, floorMat) : floorMat;
        if (m != floorMat && n.Length > 0 && n != "Editor: grid")
        {   // a level material on the floor: its texture at 1 m, as on the blocks (the floor's UVs are at 10 m)
            m = new Material(m); m.mainTextureScale = new Vector2(m.mainTextureScale.x * 10f, m.mainTextureScale.y * 10f);
        }
        if (n == "Editor: grid") m = floorMat;
        floor.GetComponent<MeshRenderer>().sharedMaterial = m;
    }
    void DrawLooks(Rect a, Block b)
    {
        Fill(a, new Color(0f, 0f, 0f, 0.88f));
        float x = a.x + 10, y = a.y + 8;
        string what = b == null ? "THE FLOOR" : (lookSlot == 0 ? "THE SURFACE" : "THE METAL PARTS") + " OF THE " + b.kind.name.ToUpper();
        GUI.Label(new Rect(x, y, a.width - 50, 22), "LOOK FOR " + what);
        if (GUI.Button(new Rect(a.xMax - 34, y, 26, 22), "x")) { if (b == null) floorPick = false; else lookOpen = false; }
        y += 26;
        GUI.Label(new Rect(x, y + 3, 50, 22), "Search");
        string nf = MiniText.Box(new Rect(x + 52, y, 220, 26), ref lookFocus, lookFilter, 30);
        if (nf != lookFilter) { lookFilter = nf; lookPage = 0; }
        typing = typing || lookFocus;
        if (GUI.Button(new Rect(x + 280, y, 120, 26), "Default look"))
        {
            if (b == null) SetFloorLook(""); else { if (lookSlot == 0) b.look0 = ""; else b.look1 = ""; ApplyLook(b, null); }
        }
        y += 32;
        List<string> list = new List<string>();
        string q = lookFilter.Trim().ToLower();
        foreach (string n in lookNames) if (q.Length == 0 || n.ToLower().Contains(q)) list.Add(n);
        const int per = 12;
        int pages = Mathf.Max(1, (list.Count + per - 1) / per);
        lookPage = Mathf.Clamp(lookPage, 0, pages - 1);
        string cur = b == null ? floorLook : (lookSlot == 0 ? b.look0 : b.look1);
        for (int i = lookPage * per; i < Mathf.Min(list.Count, (lookPage + 1) * per); i++)
        {
            Material m = looks[list[i]];
            Texture tex = null; try { if (m.HasProperty("_MainTex")) tex = m.mainTexture; } catch (Exception) { }
            Color col = Color.gray; try { if (m.HasProperty("_Color")) col = m.color; } catch (Exception) { }
            Rect sw = new Rect(x, y + 2, 26, 26);
            if (tex != null) GUI.DrawTexture(sw, tex); else Fill(sw, col);
            if (GUI.Toggle(new Rect(x + 32, y, a.width - 52, 28), cur == list[i], new GUIContent(list[i]), GUI.skin.button) && cur != list[i])
            {
                if (b == null) SetFloorLook(list[i]);
                else { if (lookSlot == 0) b.look0 = list[i]; else b.look1 = list[i]; ApplyLook(b, null); }
            }
            y += 30;
        }
        float by = a.yMax - 32;
        GUI.Label(new Rect(x, by + 3, 200, 22), list.Count + " looks" + (pages > 1 ? ",  page " + (lookPage + 1) + " of " + pages : ""));
        if (pages > 1)
        {
            if (GUI.Button(new Rect(a.xMax - 140, by, 60, 26), "<")) lookPage--;
            if (GUI.Button(new Rect(a.xMax - 74, by, 60, 26), ">")) lookPage++;
        }
    }

    int editParam = -1; bool editFocus; string editText = "";
    // put a block down on what is below it (the floor, a block, or the level's ground)
    void DropDown(Block b)
    {
        Vector3 p = b.go.transform.position;
        Cols(b, false);
        RaycastHit hit; Block hb;
        bool ok = Pick(new Ray(p + Vector3.up * 0.5f, Vector3.down), b, out hit, out hb);
        Cols(b, true);
        p.y = (ok ? hit.point.y : (levelMode ? planeY : origin.y)) + Lift;
        b.go.transform.position = p;
    }
    void DrawSide(Rect a, Block b)
    {
        Fill(a, new Color(0f, 0f, 0f, 0.78f));
        float x = a.x + 10, y = a.y + 8;
        GUI.Label(new Rect(x, y, a.width - 20, 22), (placing ? "NEW: " : "SELECTED: ") + b.kind.name.ToUpper()); y += 22;
        GUI.Label(new Rect(x, y, a.width - 20, 40), b.kind.help); y += 40;
        bool changed = false;
        for (int i = 0; i < b.kind.pn.Length; i++)
        {
            Kind k = b.kind;
            bool whole = k.pn[i] == "Steps", onoff = k.pn[i] == "Handrail";
            if (onoff)
            {
                bool v = b.p[i] > 0.5f;
                bool nv = GUI.Toggle(new Rect(x, y, 200, 22), v, new GUIContent(" " + k.pn[i]), GUI.skin.toggle);
                if (nv != v) { b.p[i] = nv ? 1f : 0f; changed = true; }
                y += 26; continue;
            }
            GUI.Label(new Rect(x, y, 100, 22), k.pn[i]);
            float top = Mathf.Max(k.pmax[i], b.p[i]);
            float nvv = GUI.HorizontalSlider(new Rect(x + 100, y + 6, 120, 16), b.p[i], k.pmin[i], top);
            nvv = whole ? Mathf.Round(nvv) : Mathf.Round(nvv * 100f) / 100f;
            if (Mathf.Abs(nvv - b.p[i]) > 0.0001f && !(editParam == i && editFocus)) { b.p[i] = nvv; changed = true; }
            // the value: click it to type any size (up to 10 times the slider's end)
            bool f = editParam == i && editFocus;
            string shown = f ? editText : (whole ? b.p[i].ToString("0") : b.p[i].ToString("0.00"));
            bool was = f;
            string nt = MiniText.Box(new Rect(x + 226, y, 82, 24), ref f, shown, 8);
            if (f && !was) { editParam = i; editText = whole ? b.p[i].ToString("0") : b.p[i].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); editFocus = true; }
            else if (f) { editText = nt; editFocus = true; }
            else if (was)
            {   // typing ended: use the value
                editFocus = false;
                float tv; if (float.TryParse(editText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out tv))
                { tv = Mathf.Clamp(tv, k.pmin[i], k.pmax[i] * 10f); if (whole) tv = Mathf.Round(tv); b.p[i] = tv; changed = true; }
            }
            y += 26;
        }
        if (changed) Rebuild(b);
        y += 6;
        Vector3 lp = b.go.transform.position - origin;
        GUI.Label(new Rect(x, y, a.width - 20, 22), "Place: x " + lp.x.ToString("0.00") + "  y " + lp.y.ToString("0.00") + "  z " + lp.z.ToString("0.00") + "   turn " + b.go.transform.eulerAngles.y.ToString("0") + "°"); y += 26;
        if (GUI.Button(new Rect(x, y, 70, 26), "Turn -")) Turn(b, -turnStep);
        if (GUI.Button(new Rect(x + 74, y, 70, 26), "Turn +")) Turn(b, turnStep);
        if (GUI.Button(new Rect(x + 148, y, 70, 26), "Up")) Move(b, Vector3.up * Mathf.Max(0.05f, snap));
        if (GUI.Button(new Rect(x + 222, y, 70, 26), "Down")) Move(b, Vector3.down * Mathf.Max(0.05f, snap));
        y += 30;
        if (GUI.Button(new Rect(x, y, 90, 26), levelMode ? "Ground" : "Floor")) DropDown(b);
        if (GUI.Button(new Rect(x + 94, y, 120, 26), "Reset sizes")) { b.p = (float[])b.kind.pd.Clone(); Rebuild(b); }
        y += 30;
        if (b.kind.id != "start")
        {
            GUI.Label(new Rect(x, y + 3, 60, 22), "Look");
            if (GUI.Button(new Rect(x + 50, y, 250, 26), "Surface: " + (b.look0.Length > 0 ? b.look0 : "default"))) { lookOpen = true; lookSlot = 0; lookFilter = ""; lookPage = 0; }
            y += 30;
            if (b.kind.id == "rail" || b.kind.id == "stairs")
            {
                if (GUI.Button(new Rect(x + 50, y, 250, 26), "Metal: " + (b.look1.Length > 0 ? b.look1 : "default"))) { lookOpen = true; lookSlot = 1; lookFilter = ""; lookPage = 0; }
                y += 30;
            }
        }
        if (!placing)
        {
            if (GUI.Button(new Rect(x, y, 110, 26), "Copy (Ctrl+D)")) sel = Duplicate(b);
            if (b.kind.id != "start" && GUI.Button(new Rect(x + 114, y, 110, 26), "Remove (Del)")) Remove(b);
        }
    }

    // ---------- outlines of the selected block and the new block ----------
    bool Proj(Vector3 w, out Vector3 s)
    {
        Vector3 v = Quaternion.Inverse(viewRot) * (w - viewPos);
        s = Vector3.zero;
        if (v.z < 0.05f) return false;
        float t = Mathf.Tan(viewFov * 0.5f * 0.01745329f);
        s = new Vector3((v.x / (v.z * t * viewAspect) * 0.5f + 0.5f) * gW, (0.5f - v.y / (v.z * t) * 0.5f) * gH, v.z);
        return true;
    }
    void DrawOutlines(Event e)
    {
        if (e.type != EventType.Repaint) return;
        if (lineMat == null)
        {
            Shader sh = Shader.Find("Hidden/Internal-Colored"); if (sh == null) return;
            lineMat = new Material(sh); lineMat.hideFlags = HideFlags.HideAndDontSave;
            lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lineMat.SetInt("_Cull", 0); lineMat.SetInt("_ZWrite", 0); lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }
        GL.PushMatrix(); GL.LoadPixelMatrix(0f, gW, gH, 0f); lineMat.SetPass(0);
        GL.Begin(1);
        if (sel != null && sel.go != null) BoxLines(sel, new Color(1f, 0.85f, 0.2f, 1f));
        if (ghost != null && ghost.go != null) BoxLines(ghost, new Color(0.3f, 0.9f, 1f, 1f));
        // the start point always shows, with its direction
        foreach (Block b in blocks) if (b.kind.id == "start" && b != sel) BoxLines(b, new Color(0.3f, 1f, 0.5f, 0.9f));
        GL.End(); GL.PopMatrix();
    }
    void BoxLines(Block b, Color c)
    {
        Bounds bb = b.mf.sharedMesh != null ? b.mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
        Vector3 mn = bb.min, mx = bb.max;
        Vector3[] k = new Vector3[8];
        for (int i = 0; i < 8; i++) k[i] = b.go.transform.TransformPoint(new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z));
        int[] ed = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };
        GL.Color(c);
        for (int i = 0; i < ed.Length; i += 2)
        {
            Vector3 a, z;
            if (!Proj(k[ed[i]], out a) || !Proj(k[ed[i + 1]], out z)) continue;
            GL.Vertex3(a.x, a.y, 0f); GL.Vertex3(z.x, z.y, 0f);
            GL.Vertex3(a.x + 1, a.y, 0f); GL.Vertex3(z.x + 1, z.y, 0f);
        }
    }

    // ===================================================================================== files
    void ClearBlocks() { foreach (Block b in blocks.ToArray()) Remove(b); sel = null; ghost = null; placing = false; }
    static string F(float v) { return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }
    static float P(string s) { float v; float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v); return v; }
    string ParkText()
    {
        StringBuilder sb = new StringBuilder("SSPARK1\n");
        if (floorLook.Length > 0) sb.Append("floor|").Append(floorLook).Append('\n');
        foreach (Block b in blocks)
        {
            if (b == ghost || b.go == null) continue;
            Vector3 lp = b.go.transform.position - origin;
            sb.Append(b.kind.id).Append(' ').Append(F(lp.x)).Append(' ').Append(F(lp.y)).Append(' ').Append(F(lp.z)).Append(' ').Append(F(b.go.transform.eulerAngles.y));
            foreach (float v in b.p) sb.Append(' ').Append(F(v));
            sb.Append('|').Append(b.look0).Append('|').Append(b.look1);
            sb.Append('\n');
        }
        return sb.ToString();
    }
    void SaveText(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text); }
    void SaveTo(string path) { SaveText(path, ParkText()); }
    void Save()
    {
        if (levelMode) { SaveTo(LevelFile()); status = "Saved " + blocks.Count + " blocks for " + levelScene + ". They come back when you come here."; return; }
        string name = layoutName.Trim(); if (name.Length == 0) name = "My park";
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        SaveTo(Path.Combine(Dir, name + ".txt"));
        status = "Saved \"" + name + "\" (" + (blocks.Count - (ghost != null ? 1 : 0)) + " blocks).";
        ScanParks();
    }
    public void Load(string file)
    {
        try
        {
            string[] lines = File.ReadAllLines(file);
            if (lines.Length == 0 || lines[0].Trim() != "SSPARK1") { status = "Not a park file."; return; }
            ClearBlocks(); missingLooks = 0; SetFloorLook("");
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split('|');
                if (parts[0] == "floor" && parts.Length > 1) { SetFloorLook(looks.ContainsKey(parts[1]) ? parts[1] : ""); continue; }
                string[] f = parts[0].Trim().Split(' ');
                if (f.Length < 5) continue;
                Kind k = KindOf(f[0]); if (k == null) continue;
                Block b = AddBlock(k, origin + new Vector3(P(f[1]), P(f[2]), P(f[3])), P(f[4]), false);
                for (int j = 0; j < b.p.Length && 5 + j < f.Length; j++) b.p[j] = Mathf.Clamp(P(f[5 + j]), k.pmin[j], k.pmax[j]);
                if (parts.Length > 2) { b.look0 = parts[1]; b.look1 = parts[2]; }
                Rebuild(b); ApplyLook(b, null);
                if ((b.look0.Length > 0 && !looks.ContainsKey(b.look0)) || (b.look1.Length > 0 && !looks.ContainsKey(b.look1))) missingLooks++;
            }
            layoutName = Path.GetFileNameWithoutExtension(file);
            status = "Loaded \"" + layoutName + "\"." + (missingLooks > 0 ? " " + missingLooks + " blocks use a look from another level: they show the default look here." : "");
            loadOpen = false;
        }
        catch (Exception e) { status = "Could not load: " + e.Message; }
    }
    List<string> parks = new List<string>();
    public List<string> Parks() { ScanParks(); return parks; }
    void ScanParks()
    {
        parks.Clear();
        if (Directory.Exists(Dir)) parks.AddRange(Directory.GetFiles(Dir, "*.txt"));
        parks.Sort(StringComparer.OrdinalIgnoreCase);
    }
    void DrawLoad(Rect a)
    {
        Fill(a, new Color(0f, 0f, 0f, 0.85f));
        GUI.Label(new Rect(a.x + 10, a.y + 6, a.width - 20, 22), parks.Count == 0 ? "No saved parks yet." : "Saved parks (the current one is replaced):");
        float y = a.y + 32;
        const int per = 7;
        int pages = Mathf.Max(1, (parks.Count + per - 1) / per);
        loadPage = Mathf.Clamp(loadPage, 0, pages - 1);
        for (int i = loadPage * per; i < Mathf.Min(parks.Count, (loadPage + 1) * per); i++)
        {
            if (GUI.Button(new Rect(a.x + 10, y, a.width - 100, 26), Path.GetFileNameWithoutExtension(parks[i]))) { Load(parks[i]); return; }
            if (GUI.Button(new Rect(a.xMax - 84, y, 74, 26), "Delete")) { try { File.Delete(parks[i]); } catch (Exception) { } ScanParks(); return; }
            y += 30;
        }
        if (pages > 1)
        {
            if (GUI.Button(new Rect(a.x + 10, a.yMax - 32, 60, 26), "<")) loadPage--;
            GUI.Label(new Rect(a.x + 80, a.yMax - 29, 120, 22), "Page " + (loadPage + 1) + " of " + pages);
            if (GUI.Button(new Rect(a.x + 200, a.yMax - 32, 60, 26), ">")) loadPage++;
        }
    }

    // ---------- settings file ----------
    static float PF(string s, float d) { float v; return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : d; }
    public void LoadSettings(string line)
    {
        line = line.Trim();
        if (!line.StartsWith("editor=")) return;
        string[] f = line.Substring(7).Split(',');
        if (f.Length < 5) return;
        autoLoad = f[0] == "1"; vertAssist = f[1] == "1";
        guideIn = Mathf.Clamp(PF(f[2], 0.25f), 0f, 1f); airGravity = Mathf.Clamp(PF(f[3], 1f), 0.4f, 1.5f); airMax = Mathf.Clamp(PF(f[4], 3f), 0.5f, 15f);
    }
    public void WriteSettings(StringBuilder sb)
    {
        sb.Append("editor=").Append(autoLoad ? "1" : "0").Append(',').Append(vertAssist ? "1" : "0").Append(',').Append(F(guideIn)).Append(',').Append(F(airGravity)).Append(',').Append(F(airMax)).Append('\n');
    }

    // ---------- the Levels tab part ----------
    public void DrawTab()
    {
        GUILayout.Label("LEVEL EDITOR     Build with ramps, rails and boxes: in an empty grid world, or right in this level.");
        GUILayout.BeginHorizontal();
        if (!Active)
        {
            if (GUILayout.Button("Open the park editor (empty grid)", GUILayout.Width(250))) Open();
            if (GUILayout.Button("Edit this level (add blocks here)", GUILayout.Width(250))) OpenLevel(true);
        }
        else if (levelMode)
        {
            if (!sessionOpen && GUILayout.Button("Edit this level's blocks", GUILayout.Width(250))) OpenLevel(true);
            if (sessionOpen && GUILayout.Button("Done editing", GUILayout.Width(250))) Close();
            if (GUILayout.Button("Take the blocks out (for now)", GUILayout.Width(250))) { RemoveLevelBlocks(); status = "Blocks taken out of this level. Your saved file stays."; }
        }
        else if (GUILayout.Button("Close the park editor", GUILayout.Width(250))) Close();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        bool al = GUILayout.Toggle(autoLoad, " Put my saved blocks back in each level when I come there", GUILayout.Width(520));
        if (al != autoLoad) { autoLoad = al; dirty = true; }
        if (File.Exists(LevelFile()) && GUILayout.Button("Delete this level's saved blocks", GUILayout.Width(240))) { try { File.Delete(LevelFile()); } catch (Exception) { } status = "The saved blocks of this level are deleted."; }
        GUILayout.EndHorizontal();
        if (status.Length > 0) GUILayout.Label(status);
    }

}

// Builds a mesh from boxes, quads and extruded side shapes. UVs: 1 unit = 1 m, from the side the face looks to.
public class MeshBuilder
{
    List<Vector3> v = new List<Vector3>(); List<Vector2> uv = new List<Vector2>();
    List<int>[] tri = { new List<int>(), new List<int>() };     // submesh 0 = the block material, 1 = the rail material
    public bool skipBottom;                                        // no faces that look down at the ground (y = 0)
    public List<Bounds> boxes = new List<Bounds>();
    static Vector2 UV(Vector3 p, Vector3 n, float scale)
    {
        Vector3 a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
        if (a.y >= a.x && a.y >= a.z) return new Vector2(p.x, p.z) * scale;
        if (a.x >= a.z) return new Vector2(p.z, p.y) * scale;
        return new Vector2(p.x, p.y) * scale;
    }
    // a flat quad (corners in order around it); its front is the side from where the order is clockwise
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uvScale = 1f, int sub = 0)
    {
        Vector3 n = Vector3.Cross(b - a, c - a).normalized;
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        uv.Add(UV(a, n, uvScale)); uv.Add(UV(b, n, uvScale)); uv.Add(UV(c, n, uvScale)); uv.Add(UV(d, n, uvScale));
        tri[sub].AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
    }
    public void Box(Vector3 mn, Vector3 mx, int sub)
    {
        Vector3 a = new Vector3(mn.x, mn.y, mn.z), b = new Vector3(mx.x, mn.y, mn.z), c = new Vector3(mx.x, mn.y, mx.z), d = new Vector3(mn.x, mn.y, mx.z);
        Vector3 e = new Vector3(mn.x, mx.y, mn.z), f = new Vector3(mx.x, mx.y, mn.z), g = new Vector3(mx.x, mx.y, mx.z), h = new Vector3(mn.x, mx.y, mx.z);
        boxes.Add(new Bounds((mn + mx) * 0.5f, mx - mn));
        Quad(e, h, g, f, 1f, sub);   // top
        if (!(skipBottom && mn.y < 0.001f)) Quad(a, b, c, d, 1f, sub);   // bottom
        Quad(a, e, f, b, 1f, sub);   // -z
        Quad(c, g, h, d, 1f, sub);   // +z
        Quad(d, h, e, a, 1f, sub);   // -x
        Quad(b, f, g, c, 1f, sub);   // +x
    }
    // a square bar from a to b
    public void Bar(Vector3 a, Vector3 b, float size, int sub)
    {
        Vector3 f = (b - a).normalized, s = Vector3.Cross(Vector3.up, f).normalized; if (s.sqrMagnitude < 0.01f) s = Vector3.right;
        Vector3 u = Vector3.Cross(f, s).normalized; float hs = size * 0.5f;
        Vector3[] ca = { a - s * hs - u * hs, a + s * hs - u * hs, a + s * hs + u * hs, a - s * hs + u * hs };
        Vector3[] cb = { b - s * hs - u * hs, b + s * hs - u * hs, b + s * hs + u * hs, b - s * hs + u * hs };
        for (int i = 0; i < 4; i++) { int j = (i + 1) % 4; Quad(ca[i], ca[j], cb[j], cb[i], 1f, sub); }
        Quad(ca[3], ca[2], ca[1], ca[0], 1f, sub); Quad(cb[0], cb[1], cb[2], cb[3], 1f, sub);
    }
    // a side shape (points in (z, y), the riding surface from front to back) made solid across x from x0 to x1:
    // the riding surface (smooth), the two sides (filled from the bottom back corner), the back and the bottom
    public void Extrude(List<Vector2> pr, float x0, float x1, Func<Vector2, float, Vector3> P, bool flip, Vector2 backCorner)
    {
        int n = pr.Count;
        // the riding surface: one strip, shared points between the parts, so the curve is smooth
        int start = v.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = P(pr[i], x0), b = P(pr[i], x1);
            v.Add(a); v.Add(b);
            float along = 0f; for (int k = 1; k <= i; k++) along += (pr[k] - pr[k - 1]).magnitude;
            uv.Add(new Vector2(x0, along)); uv.Add(new Vector2(x1, along));
        }
        for (int i = 0; i < n - 1; i++)
        {
            int a = start + i * 2, b = a + 1, c = a + 3, d = a + 2;
            if (!flip) tri[0].AddRange(new[] { a, d, c, a, c, b }); else tri[0].AddRange(new[] { a, b, c, a, c, d });
        }
        // the sides
        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? x0 : x1;
            Vector3 corner = P(backCorner, x);
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = P(pr[i], x), p1 = P(pr[i + 1], x);
                bool f = (side == 0) != flip;
                Tri(corner, f ? p1 : p0, f ? p0 : p1);
            }
            Vector3 front = P(new Vector2(pr[0].x, 0f), x);
            if ((pr[0] - new Vector2(pr[0].x, 0f)).sqrMagnitude > 1e-6f) { bool f = (side == 0) != flip; Tri(corner, f ? P(pr[0], x) : front, f ? front : P(pr[0], x)); }
        }
        // the back (from the last point down to the ground) and the bottom
        Vector3 t0 = P(pr[n - 1], x0), t1 = P(pr[n - 1], x1), g0 = P(backCorner, x0), g1 = P(backCorner, x1);
        if ((t0 - g0).sqrMagnitude > 1e-6f) { if (!flip) Quad(g1, t1, t0, g0); else Quad(g0, t0, t1, g1); }
        Vector3 f0 = P(new Vector2(pr[0].x, 0f), x0), f1 = P(new Vector2(pr[0].x, 0f), x1);
        if (!skipBottom) { if (!flip) Quad(f0, f1, g1, g0); else Quad(g0, g1, f1, f0); }
    }
    void Tri(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 n = Vector3.Cross(b - a, c - a).normalized; int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c); uv.Add(UV(a, n, 1f)); uv.Add(UV(b, n, 1f)); uv.Add(UV(c, n, 1f));
        tri[0].AddRange(new[] { i, i + 1, i + 2 });
    }
    public Mesh Make()
    {
        Mesh m = new Mesh();
        if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.vertices = v.ToArray(); m.uv = uv.ToArray();
        m.subMeshCount = 2;
        m.SetTriangles(tri[0].ToArray(), 0); m.SetTriangles(tri[1].ToArray(), 1);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }
}

// The game's GUI library has no text field: a small one. Click it, type, Enter or a click elsewhere ends it.
public static class MiniText
{
    public static bool AnyFocus;
    static int lastFrame;
    static string Keys(ref bool focus, string text, int maxLen)
    {
        Event e = Event.current;
        if (focus) { AnyFocus = true; lastFrame = Time.frameCount; }
        if (focus && e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Backspace) { if (text.Length > 0) text = text.Substring(0, text.Length - 1); e.Use(); }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape) { focus = false; e.Use(); }
            else
            {
                char ch = e.character;
                bool ok = char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_' || ch == '\'' || ch == '.';
                if (ok && text.Length < maxLen) text += ch;
                if (ch != 0 || e.keyCode != KeyCode.None) e.Use();
            }
        }
        if (focus && e.type == EventType.MouseDown) { }    // the button below sees its own clicks
        return text;
    }
    // with a fixed place
    public static string Box(Rect r, ref bool focus, string text, int maxLen)
    {
        Event e = Event.current;
        if (focus && e.type == EventType.MouseDown && !r.Contains(e.mousePosition)) focus = false;
        bool n = GUI.Toggle(r, focus, new GUIContent(focus ? text + "_" : (text.Length > 0 ? text : "(click to type)")), GUI.skin.button);
        if (n != focus) focus = n;
        return Keys(ref focus, text, maxLen);
    }
    // in a GUILayout row
    public static string Layout(ref bool focus, string text, int maxLen, float width)
    {
        Event e = Event.current;
        Rect r = GUILayoutUtility.GetRect(width, 24f, GUI.skin.button, GUILayout.Width(width));
        if (focus && e.type == EventType.MouseDown && !r.Contains(e.mousePosition)) focus = false;
        bool n = GUI.Toggle(r, focus, new GUIContent(focus ? text + "_" : (text.Length > 0 ? text : "(click to type)")), GUI.skin.button);
        if (n != focus) focus = n;
        return Keys(ref focus, text, maxLen);
    }
    // once a frame: no box has focus unless one said so this frame
    public static void EndFrame() { if (Time.frameCount - lastFrame > 1) AnyFocus = false; }
}

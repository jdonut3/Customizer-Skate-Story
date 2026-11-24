// Replay: records the last seconds of skating, and plays them back with a timeline.
//  Recording: just before each frame is drawn (after the game, the camera tools and the drawn flips), the mod saves the
//  pose of the drawn skater (all objects of the final skater mesh and its skeleton), the drawn board (with trucks and
//  wheels), and the camera. At most 60 frames a second, in a ring buffer (the newest seconds).
//  Replay (F8): the game is frozen with a pause value of the game's time manager that the game itself does not use
//  (uiPauseTimeScale). Just before each frame is drawn, the recorded poses are put on the skater, the board and the camera
//  (between two recorded frames they are blended), and just after the frame they go back. So the game state does not
//  change, and F8 again goes on from the same moment.
//  Clips: the part between the in and out markers can be saved to a file and played again later, in the same level.
//  Only the skater, the board and the camera are recorded: the rest of the level stays as it is at the pause, and there is no sound.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public partial class ReplayTools
{
    public bool dirty;
    public string status = "";

    // ---------- settings ----------
    public bool recordOn = true;
    public float bufferSeconds = 30f;
    public KeyCode replayKey = KeyCode.F8;

    // ---------- what is recorded ----------
    Transform[] parts;                 // the drawn skater and board, parents before children
    string partsKey = "";              // the names, to check that a clip fits the skater
    SkatePlayer partsOwner; float nextPartsCheck;

    class Buffer
    {
        public int cap, n, count, head;   // ring: head = the oldest frame
        public float[] time; public Vector3[] camPos; public Quaternion[] camRot; public float[] fov;
        public Vector3[] pos; public Quaternion[] rot;
        public Buffer(int cap, int n)
        {
            this.cap = cap; this.n = n;
            time = new float[cap]; camPos = new Vector3[cap]; camRot = new Quaternion[cap]; fov = new float[cap];
            pos = new Vector3[cap * n]; rot = new Quaternion[cap * n];
        }
        public int Index(int i) { return (head + i) % cap; }     // i = 0 for the oldest
    }
    Buffer live;                       // the ring buffer of the live recording
    float lastRec = -1f, lastRaw = -1f, gap;
    int recFrame = -1;

    // ---------- replay ----------
    public bool active;
    Buffer src;                        // what plays: a copy of the live buffer, or a loaded clip
    string srcName = "";
    float t0, t1;                      // first and last time of src
    float playT, inT, outT;
    bool playing, loop = true;
    float speed = 1f;
    static readonly float[] speeds = { 0.1f, 0.25f, 0.5f, 1f, 2f };
    int camMode;                       // 0 = as recorded, 1 = free, 2 = orbit
    Vector3 freePos; float freeYaw, freePitch; float freeSpeed = 6f, lookSpeed = 3f;
    float orbitYaw, orbitPitch = 15f, orbitDist = 4f;
    bool uiHidden;
    float oldPause = 1f;
    bool applied; int appliedFrame = -1;
    Vector3[] keepPos; Quaternion[] keepRot; Vector3 keepCamPos; Quaternion keepCamRot; float keepFov; Camera keepCam;
    Vector3 skaterCenter;

    // ---------- clips ----------
    class ClipInfo { public string file, name, scene; public float length; }
    List<ClipInfo> clips = new List<ClipInfo>(); float nextClipScan;
    string Dir { get { return Path.Combine(Path.Combine(Application.persistentDataPath, "ShoeMod"), "clips"); } }

    public static bool Active;         // for the other tools: a replay is on (they do not change the camera or the skater then)
    bool hooked;
    // The camera hooks are added at the first update, after the flip tools added theirs, so that a recording has the flips.
    void Hook()
    {
        if (hooked) return; hooked = true;
        Camera.onPreCull += OnPreCull;
        Camera.onPostRender += OnPostRender;
    }

    // ---------- settings file ----------
    static string F(float v) { return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }
    static float P(string s, float d) { float v; return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : d; }
    public void Load(string line)
    {
        line = line.Trim();
        if (!line.StartsWith("replay=")) return;
        string[] f = line.Substring(7).Split(',');
        if (f.Length < 2) return;
        recordOn = f[0] == "1"; bufferSeconds = Mathf.Clamp(P(f[1], 30f), 10f, 120f);
        if (f.Length >= 4) { freeSpeed = Mathf.Clamp(P(f[2], 6f), 0.2f, 60f); lookSpeed = Mathf.Clamp(P(f[3], 3f), 0.3f, 10f); }
    }
    public void Write(StringBuilder sb) { sb.Append("replay=").Append(recordOn ? "1" : "0").Append(',').Append(F(bufferSeconds)).Append(',').Append(F(freeSpeed)).Append(',').Append(F(lookSpeed)).Append('\n'); }

    // ---------- the parts to record ----------
    void FindParts()
    {
        SkatePlayer sp = null; ThePlayer p = null;
        try { sp = ThePlayer.SkatePlayer; p = ThePlayer.instance; } catch (Exception) { }
        if (sp == null || p == null || p.finalSkaterMesh == null || sp.board == null) { parts = null; partsOwner = null; return; }
        if (sp == partsOwner && parts != null) return;
        List<Transform> l = new List<Transform>();
        HashSet<Transform> seen = new HashSet<Transform>();
        Action<Transform> add = root => { if (root == null) return; foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (seen.Add(t)) l.Add(t); };
        add(p.finalSkaterMesh.transform);
        if (p.finalSkaterMesh.skaterMeshRenderer != null && p.finalSkaterMesh.skaterMeshRenderer.rootBone != null) add(p.finalSkaterMesh.skaterMeshRenderer.rootBone);
        add(sp.board.transform);
        // the drawn board of the final skater (the game blends it from its board copies) and the other copies
        FinalSkateboardMesh fb = p.finalSkaterMesh.finalSkateboardMesh;
        if (fb != null)
        {
            if (fb.final_board != null) add(fb.final_board.transform);
            if (fb.skate_board != null) add(fb.skate_board.transform);
            if (fb.anim_board != null) add(fb.anim_board.transform);
        }
        // all bones of the final skeleton (some can be outside the root bone)
        if (p.finalSkaterMesh.finalRig != null && p.finalSkaterMesh.finalRig.children != null)
            foreach (Transform t in p.finalSkaterMesh.finalRig.children) if (t != null && seen.Add(t)) l.Add(t);
        if (p.finalSkaterMesh.skaterMeshRenderer != null && p.finalSkaterMesh.skaterMeshRenderer.bones != null)
            foreach (Transform t in p.finalSkaterMesh.skaterMeshRenderer.bones) if (t != null && seen.Add(t)) l.Add(t);
        // parents before children, so that each one is set after its parent
        l.Sort((x, y) => Depth(x).CompareTo(Depth(y)));
        parts = l.ToArray(); partsOwner = sp;
        StringBuilder k = new StringBuilder(); foreach (Transform t in parts) k.Append(t.name).Append('|');
        partsKey = parts.Length + ":" + k.ToString().GetHashCode();
        live = null;                   // a new skater: start a new recording
        keepPos = new Vector3[parts.Length]; keepRot = new Quaternion[parts.Length];
    }
    static int Depth(Transform t) { int d = 0; while (t != null) { d++; t = t.parent; } return d; }
    void NewLive()
    {
        int cap = Mathf.Clamp((int)(bufferSeconds * 60f) + 2, 120, 7400);
        live = new Buffer(cap, parts.Length); lastRec = -1f; lastRaw = -1f; gap = 0f;
    }

    // ---------- each frame ----------
    public void Update()
    {
        try
        {
            Hook();
            AfterDraw();                                   // safety: put back, if no camera drew last frame
            float now = Time.unscaledTime;
            if (now >= nextPartsCheck || partsOwner == null) { nextPartsCheck = now + 1f; if (!active) FindParts(); }
            if (Input.GetKeyDown(replayKey)) { if (active) Exit(); else Enter(); }
            if (active) Controls();
            if (active) KeepFrozen();
        }
        catch (Exception e) { status = "Replay error: " + e.Message; }
    }
    void KeepFrozen()
    {
        TimeMan tm = null; try { tm = TimeMan.main; } catch (Exception) { }
        if (tm != null) tm.uiPauseTimeScale = 0f;
    }

    void OnPreCull(Camera c)
    {
        if (c == null || c != Camera.main) return;
        try
        {
            if (active) { BeforeDraw(c); return; }
            Record(c);
        }
        catch (Exception e) { status = "Replay error: " + e.Message; }
    }
    void OnPostRender(Camera c)
    {
        if (c == null || c != Camera.main) return;
        AfterDraw();
    }

    void Record(Camera c)
    {
        if (!recordOn || parts == null || recFrame == Time.frameCount) return;
        float raw = Time.unscaledTime;
        if (lastRaw >= 0f && raw - lastRaw < 1f / 61f) return;    // at most about 60 frames a second
        // a gap (a pause menu, a hitch): the recorded time goes on as if the gap was one frame long
        if (lastRaw >= 0f && raw - lastRaw > 0.25f) gap += raw - lastRaw - 1f / 60f;
        lastRaw = raw;
        float t = raw - gap;
        for (int i = 0; i < parts.Length; i++) if (parts[i] == null) { partsOwner = null; parts = null; return; }
        if (live == null || live.n != parts.Length) NewLive();
        recFrame = Time.frameCount;
        lastRec = t;
        int slot;
        if (live.count < live.cap) { slot = (live.head + live.count) % live.cap; live.count++; }
        else { slot = live.head; live.head = (live.head + 1) % live.cap; }
        live.time[slot] = t;
        live.camPos[slot] = c.transform.position; live.camRot[slot] = c.transform.rotation; live.fov[slot] = c.fieldOfView;
        int b = slot * live.n;
        for (int i = 0; i < parts.Length; i++) { live.pos[b + i] = parts[i].position; live.rot[b + i] = parts[i].rotation; }
    }

    // ---------- enter and exit ----------
    void Enter()
    {
        if (EditorTools.Editing) { status = "Press P to play first: the replay works in play mode."; return; }
        if (parts == null || live == null || live.count < 10) { status = "Nothing recorded yet. Skate for a moment, then press " + replayKey + "."; return; }
        // a copy of the live buffer, in order from old to new
        src = new Buffer(live.count, live.n);
        for (int i = 0; i < live.count; i++) CopyFrame(live, live.Index(i), src, i);
        path.Clear(); pathSel = -1;
        src.count = live.count; srcName = "Last " + (live.time[live.Index(live.count - 1)] - live.time[live.head]).ToString("0") + " seconds";
        Start();
    }
    // a report for finding problems: what is drawn, what is recorded, and how much the recorded parts move
    void Report()
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Replay report ").Append(DateTime.Now.ToString("HH:mm:ss")).Append("  parts=").Append(parts.Length).Append("  frames=").Append(src.count).Append('\n');
            HashSet<Transform> mine = new HashSet<Transform>(parts);
            ThePlayer p = ThePlayer.instance;
            sb.Append("DRAWN MODELS (enabled and in an active object):\n");
            if (p != null)
                foreach (Renderer rd in p.GetComponentsInChildren<Renderer>(false))
                {
                    if (!rd.enabled || rd is ParticleSystemRenderer || rd is TrailRenderer) continue;
                    SkinnedMeshRenderer sm = rd as SkinnedMeshRenderer;
                    string path = TPath(rd.transform);
                    if (sm != null)
                    {
                        int inSet = 0; foreach (Transform b0 in sm.bones) if (b0 != null && mine.Contains(b0)) inSet++;
                        sb.Append("  SKIN ").Append(path).Append("  root=").Append(sm.rootBone != null ? TPath(sm.rootBone) : "-")
                          .Append("  bones=").Append(sm.bones.Length).Append("  recorded=").Append(inSet).Append('\n');
                    }
                    else if (rd.transform.name.ToLower().Contains("deck") || rd.transform.name.ToLower().Contains("wheel") || rd.transform.name.ToLower().Contains("truck") || rd.transform.name.ToLower().Contains("board"))
                        sb.Append("  MESH ").Append(path).Append("  recorded=").Append(mine.Contains(rd.transform) ? "yes" : "no").Append('\n');
                }
            sb.Append("RECORDED PARTS THAT MOVE MOST (angle change over the recording):\n");
            List<KeyValuePair<float, int>> mv = new List<KeyValuePair<float, int>>();
            for (int k = 0; k < src.n; k++)
            {
                float most = 0f;
                for (int f = 1; f < src.count; f += 3) most = Mathf.Max(most, Quaternion.Angle(src.rot[k], src.rot[f * src.n + k]));
                mv.Add(new KeyValuePair<float, int>(most, k));
            }
            mv.Sort((x, y) => y.Key.CompareTo(x.Key));
            for (int i = 0; i < Mathf.Min(25, mv.Count); i++) sb.Append("  ").Append(mv[i].Key.ToString("0")).Append(" deg  ").Append(parts[mv[i].Value] != null ? TPath(parts[mv[i].Value]) : "-").Append('\n');
            int still = 0; foreach (var kv in mv) if (kv.Key < 1f) still++;
            sb.Append("  parts that do not turn at all: ").Append(still).Append(" of ").Append(mv.Count).Append('\n');
            File.WriteAllText(System.IO.Path.Combine(System.IO.Path.Combine(Application.persistentDataPath, "ShoeMod"), "replay_report.txt"), sb.ToString());
        }
        catch (Exception e) { status = "Report error: " + e.Message; }
    }
    static string TPath(Transform t) { string s = t.name; int d = 0; while (t.parent != null && d < 6) { t = t.parent; s = t.name + "/" + s; d++; } return s; }

    void Start()
    {
        Report();
        rootIdx = -2; haveView = false; dragPt = -1;
        t0 = src.time[0]; t1 = src.time[src.count - 1];
        inT = t0; outT = t1; playT = Mathf.Max(t0, t1 - 5f); playing = false; speed = 1f; camMode = 0; uiHidden = false;
        TimeMan tm = null; try { tm = TimeMan.main; } catch (Exception) { }
        if (tm != null) { oldPause = tm.uiPauseTimeScale; tm.uiPauseTimeScale = 0f; }
        active = true; Active = true;
        try { FinalSkaterMesh fm = ThePlayer.instance.finalSkaterMesh; if (fm != null && fm.enabled) { fm.enabled = false; finalOff = fm; } } catch (Exception) { }
        TrailsOff();
        // the free and orbit cameras start from the recorded view
        int fi = Find(playT);
        freePos = src.camPos[fi]; Vector3 e = src.camRot[fi].eulerAngles; freeYaw = e.y; freePitch = e.x > 180f ? e.x - 360f : e.x;
        status = "";
    }
    public void Exit()
    {
        if (!active) return;
        AfterDraw();
        TimeMan tm = null; try { tm = TimeMan.main; } catch (Exception) { }
        if (tm != null) tm.uiPauseTimeScale = oldPause > 0f ? oldPause : 1f;
        active = false; Active = false; src = null;
        if (finalOff != null) { finalOff.enabled = true; finalOff = null; }
        TrailsOn();
        LensOff();
        lastRaw = -1f;                 // the recording goes on, without the time of the replay
    }
    static void CopyFrame(Buffer a, int ai, Buffer b, int bi)
    {
        b.time[bi] = a.time[ai]; b.camPos[bi] = a.camPos[ai]; b.camRot[bi] = a.camRot[ai]; b.fov[bi] = a.fov[ai];
        Array.Copy(a.pos, ai * a.n, b.pos, bi * b.n, a.n);
        Array.Copy(a.rot, ai * a.n, b.rot, bi * b.n, a.n);
    }

    // ---------- play ----------
    int Find(float t)
    {   // the last frame at or before t
        int lo = 0, hi = src.count - 1;
        if (t <= src.time[0]) return 0;
        if (t >= src.time[hi]) return hi;
        while (hi - lo > 1) { int m = (lo + hi) / 2; if (src.time[m] <= t) lo = m; else hi = m; }
        return lo;
    }
    void Controls()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        if (playing)
        {
            playT += dt * speed;
            if (playT >= outT) { if (loop) playT = inT; else { playT = outT; playing = false; } }
        }
        // keys
        if (Input.GetKeyDown(KeyCode.Space)) TogglePlay();
        if (Input.GetKeyDown(KeyCode.H)) uiHidden = !uiHidden;
        if (Input.GetKeyDown(KeyCode.I)) SetIn();
        if (Input.GetKeyDown(KeyCode.O)) SetOut();
        if (Input.GetKeyDown(KeyCode.C)) camMode = (camMode + 1) % 4;
        if (Input.GetKeyDown(KeyCode.T)) GoToSkater();
        if (Input.GetKeyDown(KeyCode.K)) AddPoint();
        if (Input.GetKeyDown(KeyCode.V)) showPath = !showPath;
        if (pathSel >= 0 && pathSel < path.Count)
        {
            if (Input.GetKeyDown(KeyCode.LeftBracket)) MovePointTime(-0.1f);
            if (Input.GetKeyDown(KeyCode.RightBracket)) MovePointTime(0.1f);
            if (Input.GetKeyDown(KeyCode.Delete)) RemovePoint();
        }
        if (Input.GetKeyDown(KeyCode.Home)) playT = inT;
        if (Input.GetKeyDown(KeyCode.End)) playT = outT;
        if (Input.GetKeyDown(KeyCode.UpArrow)) SpeedStep(1);
        if (Input.GetKeyDown(KeyCode.DownArrow)) SpeedStep(-1);
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.Comma)) Step(-1);
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Period)) Step(1);
        if (Input.GetKey(KeyCode.LeftArrow) && !playing && Input.GetKey(KeyCode.LeftShift)) playT -= dt * 0.5f;
        if (Input.GetKey(KeyCode.RightArrow) && !playing && Input.GetKey(KeyCode.LeftShift)) playT += dt * 0.5f;
        playT = Mathf.Clamp(playT, t0, t1);
        CameraControls(dt);
    }
    void TogglePlay() { playing = !playing; if (playing && playT >= outT - 0.01f) playT = inT; }
    void SetIn() { inT = Mathf.Min(playT, outT - 0.1f); }
    void SetOut() { outT = Mathf.Max(playT, inT + 0.1f); }
    void SpeedStep(int d)
    {
        int i = 0; for (int k = 0; k < speeds.Length; k++) if (Mathf.Abs(speeds[k] - speed) < 0.001f) i = k;
        speed = speeds[Mathf.Clamp(i + d, 0, speeds.Length - 1)];
    }
    void Step(int d)
    {
        playing = false;
        int i = Find(playT);
        if (d > 0 && Mathf.Abs(src.time[i] - playT) < 0.0001f) i++;
        else if (d < 0 && Mathf.Abs(src.time[i] - playT) < 0.0001f) i--;
        else if (d > 0) i++;
        i = Mathf.Clamp(i, 0, src.count - 1);
        playT = src.time[i];
    }
    void CameraControls(float dt)
    {
        if (camMode == 1)
        {   // free: hold the right mouse button to look; WASD to move, Q/E down/up, Shift faster, the wheel sets the speed
            if (Input.GetMouseButton(1)) { freeYaw += Input.GetAxisRaw("Mouse X") * lookSpeed; freePitch = Mathf.Clamp(freePitch - Input.GetAxisRaw("Mouse Y") * lookSpeed, -89f, 89f); }
            float w = Input.mouseScrollDelta.y;
            if (w != 0f && !MouseOverUI()) { freeSpeed = Mathf.Clamp(freeSpeed * (w > 0 ? 1.25f : 0.8f), 0.2f, 60f); speedChanged = true; }
            Quaternion r = Quaternion.Euler(freePitch, freeYaw, 0f);
            Vector3 m = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) m += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) m += Vector3.back;
            if (Input.GetKey(KeyCode.A)) m += Vector3.left;
            if (Input.GetKey(KeyCode.D)) m += Vector3.right;
            Vector3 v = r * m;
            if (Input.GetKey(KeyCode.E)) v += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) v += Vector3.down;
            freePos += v * freeSpeed * dt * (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f) * (Input.GetKey(KeyCode.LeftControl) ? 0.25f : 1f);   // Ctrl = slow
        }
        else if (camMode == 2)
        {   // orbit: hold the right mouse button to turn around the skater, the wheel to go closer or farther
            if (Input.GetMouseButton(1)) { orbitYaw += Input.GetAxisRaw("Mouse X") * 3f; orbitPitch = Mathf.Clamp(orbitPitch - Input.GetAxisRaw("Mouse Y") * 3f, -80f, 85f); }
            float w = Input.mouseScrollDelta.y;
            if (w != 0f && !MouseOverUI()) orbitDist = Mathf.Clamp(orbitDist * (w > 0 ? 0.9f : 1.1f), 0.8f, 40f);
        }
    }
    bool MouseOverUI() { float s = Mathf.Clamp(Screen.height / 1080f, 0.6f, 3f); return !uiHidden && (Input.mousePosition.y < 200f * s || (looksOpen && Input.mousePosition.y < 360f * s && Input.mousePosition.x > Screen.width - 530f * s)); }

    // The recorded poses go on in the late update: Unity works out the shape of the skinned body from the bones after the
    // late update and before the cameras draw, so a bone set later only moves the finished shape as one piece.
    // (The game's own final skeleton step is off during a replay, so it does not write its frozen pose over them.)
    float blendA; int blendI, blendJ;
    public void LateApply()
    {
        LatePoses();
        try { ApplyLens(); } catch (Exception e) { status = "Lens error: " + e.Message; }
    }
    void LatePoses()
    {
        if (!active || posesOn || src == null || parts == null || src.n != parts.Length) return;
        int i = Find(playT); int j = Mathf.Min(i + 1, src.count - 1);
        float span = src.time[j] - src.time[i];
        float a = span > 0.0001f ? Mathf.Clamp01((playT - src.time[i]) / span) : 0f;
        blendI = i; blendJ = j; blendA = a;
        int bi = i * src.n, bj = j * src.n;
        for (int k = 0; k < parts.Length; k++)
        {
            Transform t = parts[k]; if (t == null) continue;
            keepPos[k] = t.position; keepRot[k] = t.rotation;
        }
        for (int k = 0; k < parts.Length; k++)
        {   // parents come first in the list, so each object is set after its parent
            Transform t = parts[k]; if (t == null) continue;
            t.SetPositionAndRotation(Vector3.Lerp(src.pos[bi + k], src.pos[bj + k], a), Quaternion.Slerp(src.rot[bi + k], src.rot[bj + k], a));
        }
        Transform rb = null; try { rb = ThePlayer.instance.finalSkaterMesh.skaterMeshRenderer.rootBone; } catch (Exception) { }
        skaterCenter = rb != null ? rb.position : Vector3.Lerp(src.pos[bi], src.pos[bj], a) + Vector3.up * 0.9f;
        posesOn = true;
    }
    bool posesOn, camOn;
    FinalSkaterMesh finalOff;

    // Trails on the skater and the board: during a replay the parts jump between the paused place and the recorded
    // place each frame, and time is stopped, so a trail would draw lines along that path that never fade.
    // They are hidden for the replay and start clean after it.
    List<TrailRenderer> trailsOff = new List<TrailRenderer>();
    List<bool> trailsEmit = new List<bool>();
    void TrailsOff()
    {
        trailsOff.Clear(); trailsEmit.Clear();
        HashSet<Transform> roots = new HashSet<Transform>();
        try { roots.Add(ThePlayer.instance.transform.root); } catch (Exception) { }
        try { roots.Add(ThePlayer.SkatePlayer.transform.root); } catch (Exception) { }
        foreach (Transform root in roots)
        {
            if (root == null) continue;
            foreach (TrailRenderer tr in root.GetComponentsInChildren<TrailRenderer>(true))
            {
                if (tr == null || !tr.enabled) continue;
                trailsOff.Add(tr); trailsEmit.Add(tr.emitting);
                tr.emitting = false; tr.Clear(); tr.enabled = false;
            }
        }
    }
    void TrailsOn()
    {
        for (int k = 0; k < trailsOff.Count; k++)
        {
            TrailRenderer tr = trailsOff[k]; if (tr == null) continue;
            tr.Clear(); tr.enabled = true; tr.emitting = trailsEmit[k];
        }
        trailsOff.Clear(); trailsEmit.Clear();
    }

    // the camera: just before the frame is drawn (after the game's camera code)
    void BeforeDraw(Camera c)
    {
        if (camOn || appliedFrame == Time.frameCount || src == null) return;
        if (!posesOn) { blendI = Find(playT); blendJ = Mathf.Min(blendI + 1, src.count - 1); float sp = src.time[blendJ] - src.time[blendI]; blendA = sp > 0.0001f ? Mathf.Clamp01((playT - src.time[blendI]) / sp) : 0f; }
        int i = blendI, j = blendJ; float a = blendA;
        // set the poses again: plain models (the board, trucks and wheels) take their place when they are drawn,
        // and game scripts can move them after the late update. Skinned parts are not changed by this.
        if (posesOn && parts != null && src.n == parts.Length)
        {
            int bi = i * src.n, bj = j * src.n;
            for (int k = 0; k < parts.Length; k++)
            {
                Transform t = parts[k]; if (t == null) continue;
                t.SetPositionAndRotation(Vector3.Lerp(src.pos[bi + k], src.pos[bj + k], a), Quaternion.Slerp(src.rot[bi + k], src.rot[bj + k], a));
            }
        }
        keepCam = c; keepCamPos = c.transform.position; keepCamRot = c.transform.rotation; keepFov = c.fieldOfView;
        Vector3 vp; Quaternion vr; float vf;
        View(camMode, out vp, out vr, out vf);
        c.transform.SetPositionAndRotation(vp, vr);
        c.fieldOfView = vf;
        viewPos = vp; viewRot = vr; viewFov = vf; viewAspect = c.aspect; viewNear = Mathf.Max(0.05f, c.nearClipPlane); haveView = true;
        camOn = true; appliedFrame = Time.frameCount;
    }
    // the view of a camera mode at the time playT
    void View(int mode, out Vector3 pos, out Quaternion rot, out float fov)
    {
        int i = blendI, j = blendJ; float a = blendA;
        fov = Zoomed(Mathf.Lerp(src.fov[i], src.fov[j], a), baseLens.zoom);
        if (mode == 1) { pos = freePos; rot = Quaternion.Euler(freePitch, freeYaw, 0f); return; }
        if (mode == 2)
        {
            rot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            pos = skaterCenter - rot * Vector3.forward * orbitDist; return;
        }
        if (mode == 3 && path.Count > 0) { PathView(playT, out pos, out rot, out fov); return; }
        pos = Vector3.Lerp(src.camPos[i], src.camPos[j], a); rot = Quaternion.Slerp(src.camRot[i], src.camRot[j], a);
    }

    // ---------- camera path: points on the timeline, the camera glides between them ----------
    class PathPt { public float t; public Vector3 pos; public Quaternion rot; public float fov; public Lens lens; }
    List<PathPt> path = new List<PathPt>();
    bool pathLook;                     // the path camera turns to the skater
    int pathSel = -1;
    void AddPoint()
    {
        if (src == null) return;
        Vector3 p; Quaternion q; float f;
        int from = camMode;                           // the new point is the view you see now
        if (camMode == 3 && path.Count > 0) PathView(playT, out p, out q, out f); else View(from, out p, out q, out f);
        for (int k = 0; k < path.Count; k++)
            if (Mathf.Abs(path[k].t - playT) < 0.05f) { path[k].pos = p; path[k].rot = q; path[k].fov = f; pathSel = k; status = "Point " + (k + 1) + " changed."; return; }
        path.Add(new PathPt { t = playT, pos = p, rot = q, fov = f, lens = LensAt(playT).Copy() });
        path.Sort((x, y) => x.t.CompareTo(y.t));
        pathSel = path.FindIndex(x => Mathf.Abs(x.t - playT) < 0.0001f);
        status = "Point " + (pathSel + 1) + " of " + path.Count + " added." + (path.Count == 1 ? " Add one more at another time, then choose Path." : "");
    }
    void RemovePoint()
    {
        int k = pathSel;
        if (k < 0 || k >= path.Count) { k = -1; float best = 0.25f; for (int q = 0; q < path.Count; q++) { float d = Mathf.Abs(path[q].t - playT); if (d < best) { best = d; k = q; } } }
        if (k < 0) { status = "Select a point first: click its mark on the timeline, or move the time to it."; return; }
        path.RemoveAt(k); pathSel = -1; status = "Point removed. " + path.Count + " left.";
    }
    void PathView(float t, out Vector3 pos, out Quaternion rot, out float fov)
    {
        int n = path.Count;
        if (n == 1 || t <= path[0].t) { Pt(path[0], out pos, out rot, out fov); }
        else if (t >= path[n - 1].t) { Pt(path[n - 1], out pos, out rot, out fov); }
        else
        {
            int k = 0; while (k < n - 2 && t >= path[k + 1].t) k++;
            PathPt p1 = path[k], p2 = path[k + 1];
            PathPt p0 = k > 0 ? path[k - 1] : null, p3 = k + 2 < n ? path[k + 2] : null;
            float u = (t - p1.t) / Mathf.Max(0.0001f, p2.t - p1.t);
            if (pathEase) u = u * u * (3f - 2f * u);
            pos = Spline(p0 != null ? p0.pos : 2f * p1.pos - p2.pos, p1.pos, p2.pos, p3 != null ? p3.pos : 2f * p2.pos - p1.pos, u);
            Vector3 f1 = p1.rot * Vector3.forward, f2 = p2.rot * Vector3.forward, u1 = p1.rot * Vector3.up, u2 = p2.rot * Vector3.up;
            Vector3 f0 = p0 != null ? p0.rot * Vector3.forward : f1, f3 = p3 != null ? p3.rot * Vector3.forward : f2;
            Vector3 u0 = p0 != null ? p0.rot * Vector3.up : u1, u3 = p3 != null ? p3.rot * Vector3.up : u2;
            Vector3 fw = Spline(f0, f1, f2, f3, u), up = Spline(u0, u1, u2, u3, u);
            rot = fw.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(fw.normalized, up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up) : Quaternion.Slerp(p1.rot, p2.rot, u);
            fov = Mathf.Lerp(p1.fov, p2.fov, u * u * (3f - 2f * u));
        }
        if (pathLook) { Vector3 d = skaterCenter - pos; if (d.sqrMagnitude > 0.01f) rot = Quaternion.LookRotation(d, Vector3.up); }
    }
    static void Pt(PathPt p, out Vector3 pos, out Quaternion rot, out float fov) { pos = p.pos; rot = p.rot; fov = p.fov; }
    static Vector3 Spline(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
    {   // Catmull-Rom: goes through each point, with no sharp corner
        float u2 = u * u, u3 = u2 * u;
        return 0.5f * (2f * p1 + (p2 - p0) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (3f * p1 - p0 - 3f * p2 + p3) * u3);
    }

    // ---------- the path view: the path drawn in the level, with each point as a small camera (V) ----------
    bool showPath = true, pathEase;
    bool haveView; Vector3 viewPos; Quaternion viewRot; float viewFov = 60f, viewAspect = 1.78f, viewNear = 0.1f;
    float gW, gH;                      // the size of the replay screen, in its own units
    int dragPt = -1; Vector2 dragLast;
    Material lineMat;
    List<Vector3> lineA = new List<Vector3>(), lineB = new List<Vector3>(); List<Color> lineC = new List<Color>();

    // world point to replay-screen point (top left is 0,0); z = distance in front of the camera
    bool Proj(Vector3 w, out Vector3 s)
    {
        Vector3 v = Quaternion.Inverse(viewRot) * (w - viewPos);
        s = Vector3.zero;
        if (v.z < viewNear) return false;
        float t = Mathf.Tan(viewFov * 0.5f * 0.01745329f);
        s = new Vector3((v.x / (v.z * t * viewAspect) * 0.5f + 0.5f) * gW, (0.5f - v.y / (v.z * t) * 0.5f) * gH, v.z);
        return true;
    }
    void Line(Vector3 a, Vector3 b, Color c) { lineA.Add(a); lineB.Add(b); lineC.Add(c); }
    // a small camera: a box, and lines to the frame of its view
    void CamShape(Vector3 p, Quaternion q, float fov, Color c, float size)
    {
        float h = Mathf.Tan(fov * 0.5f * 0.01745329f) * size, w = h * viewAspect;
        Vector3 f = q * Vector3.forward * size, r = q * Vector3.right, u = q * Vector3.up;
        Vector3 c1 = p + f + r * w + u * h, c2 = p + f - r * w + u * h, c3 = p + f - r * w - u * h, c4 = p + f + r * w - u * h;
        Line(p, c1, c); Line(p, c2, c); Line(p, c3, c); Line(p, c4, c);
        Line(c1, c2, c); Line(c2, c3, c); Line(c3, c4, c); Line(c4, c1, c);
        Line(c1 + u * h * 0.3f, c2 + u * h * 0.3f, c);               // the top of the view
    }
    void DrawPathView(Event e, float panelTop, Rect[] busy)
    {
        if (!showPath || !haveView || path.Count == 0) return;
        Color yel = new Color(1f, 0.82f, 0.2f, 0.95f), cyan = new Color(0.3f, 0.9f, 1f, 0.95f);
        lineA.Clear(); lineB.Clear(); lineC.Clear();
        // the curve
        if (path.Count > 1)
        {
            float ta = path[0].t, tb = path[path.Count - 1].t;
            int steps = Mathf.Clamp((int)((tb - ta) * 40f), 20, 2000);
            Vector3 prev = Vector3.zero;
            for (int k = 0; k <= steps; k++)
            {
                Vector3 p; Quaternion q; float f;
                bool keep = pathLook; pathLook = false;
                PathView(ta + (tb - ta) * k / steps, out p, out q, out f);
                pathLook = keep;
                if (k > 0) Line(prev, p, yel);
                prev = p;
            }
        }
        // the points
        for (int k = 0; k < path.Count; k++) CamShape(path[k].pos, path[k].rot, path[k].fov, k == pathSel ? Color.white : yel, 0.45f);
        // where the path camera is now (when you do not look through it)
        if (camMode != 3)
        {
            Vector3 p; Quaternion q; float f; PathView(playT, out p, out q, out f);
            CamShape(p, q, f, cyan, 0.3f);
        }
        if (e.type == EventType.Repaint)
        {
            if (lineMat == null)
            {
                Shader sh = Shader.Find("Hidden/Internal-Colored");
                if (sh != null)
                {
                    lineMat = new Material(sh); lineMat.hideFlags = HideFlags.HideAndDontSave;
                    lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    lineMat.SetInt("_Cull", 0); lineMat.SetInt("_ZWrite", 0);
                    lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                }
            }
            if (lineMat != null)
            {
                GL.PushMatrix();
                GL.LoadPixelMatrix(0f, gW, gH, 0f);
                lineMat.SetPass(0);
                GL.Begin(1);                // lines
                for (int k = 0; k < lineA.Count; k++)
                {
                    Vector3 a, b;
                    if (!Clip(lineA[k], lineB[k], out a, out b)) continue;
                    GL.Color(lineC[k]);
                    // three lines side by side, so that the line is easy to see
                    GL.Vertex3(a.x, a.y, 0f); GL.Vertex3(b.x, b.y, 0f);
                    GL.Vertex3(a.x + 1f, a.y, 0f); GL.Vertex3(b.x + 1f, b.y, 0f);
                    GL.Vertex3(a.x, a.y + 1f, 0f); GL.Vertex3(b.x, b.y + 1f, 0f);
                }
                GL.End();
                GL.PopMatrix();
            }
            // the point numbers
            for (int k = 0; k < path.Count; k++)
            {
                Vector3 s;
                if (Proj(path[k].pos, out s)) GUI.Label(new Rect(s.x + 8, s.y - 22, 120, 22), (k + 1).ToString() + "  " + (path[k].t - t0).ToString("0.0") + " s");
            }
        }
        // click a point to select it; drag it to move it (Shift + drag up and down: nearer and farther)
        Vector2 mp = e.mousePosition;
        bool free = mp.y < panelTop; foreach (Rect b0 in busy) if (b0.Contains(mp)) free = false;
        if (e.type == EventType.MouseDown && e.button == 0 && free)
        {
            int best = -1; float bd = 20f;
            for (int k = 0; k < path.Count; k++) { Vector3 s; if (Proj(path[k].pos, out s)) { float d = Vector2.Distance(mp, new Vector2(s.x, s.y)); if (d < bd) { bd = d; best = k; } } }
            if (best >= 0) { pathSel = best; dragPt = best; dragLast = mp; playing = false; e.Use(); }
        }
        if (e.type == EventType.MouseDrag && dragPt >= 0 && dragPt < path.Count)
        {
            Vector2 d = mp - dragLast; dragLast = mp;
            PathPt p = path[dragPt];
            Vector3 v = Quaternion.Inverse(viewRot) * (p.pos - viewPos);
            float z = Mathf.Max(0.5f, v.z), perPx = 2f * z * Mathf.Tan(viewFov * 0.5f * 0.01745329f) / gH;
            if (e.shift) p.pos += viewRot * Vector3.forward * (-d.y * perPx * 2f);
            else p.pos += viewRot * Vector3.right * (d.x * perPx) + viewRot * Vector3.up * (-d.y * perPx);
            e.Use();
        }
        if (e.type == EventType.MouseUp && e.button == 0) dragPt = -1;
    }
    // a 3D line in screen points, cut at the front of the camera
    bool Clip(Vector3 wa, Vector3 wb, out Vector3 a, out Vector3 b)
    {
        a = b = Vector3.zero;
        Quaternion inv = Quaternion.Inverse(viewRot);
        Vector3 va = inv * (wa - viewPos), vb = inv * (wb - viewPos);
        if (va.z < viewNear && vb.z < viewNear) return false;
        if (va.z < viewNear) va = Vector3.Lerp(va, vb, (viewNear - va.z) / (vb.z - va.z));
        else if (vb.z < viewNear) vb = Vector3.Lerp(vb, va, (viewNear - vb.z) / (va.z - vb.z));
        float t = Mathf.Tan(viewFov * 0.5f * 0.01745329f);
        a = new Vector3((va.x / (va.z * t * viewAspect) * 0.5f + 0.5f) * gW, (0.5f - va.y / (va.z * t) * 0.5f) * gH, 0f);
        b = new Vector3((vb.x / (vb.z * t * viewAspect) * 0.5f + 0.5f) * gW, (0.5f - vb.y / (vb.z * t) * 0.5f) * gH, 0f);
        return true;
    }

    // ---------- point editing ----------
    void MovePointTime(float d)
    {
        if (pathSel < 0 || pathSel >= path.Count) return;
        PathPt p = path[pathSel];
        float lo = pathSel > 0 ? path[pathSel - 1].t + 0.05f : t0, hi = pathSel < path.Count - 1 ? path[pathSel + 1].t - 0.05f : t1;
        p.t = Mathf.Clamp(p.t + d, lo, Mathf.Max(lo, hi));
        playT = p.t; playing = false;
    }
    int rootIdx = -2;
    Vector3 SkaterAt(float t)
    {
        if (rootIdx == -2)
        {
            rootIdx = -1;
            try { Transform rb = ThePlayer.instance.finalSkaterMesh.skaterMeshRenderer.rootBone; if (parts != null) rootIdx = Array.IndexOf(parts, rb); } catch (Exception) { }
        }
        int i = Find(t);
        if (rootIdx < 0 || src == null) return skaterCenter;
        return src.pos[i * src.n + rootIdx];
    }
    void DrawPointPanel(Rect a)
    {
        PathPt p = path[pathSel];
        Fill(a, new Color(0f, 0f, 0f, 0.8f));
        float x = a.x + 10, y = a.y + 8;
        GUI.Label(new Rect(x, y, 400, 22), "POINT " + (pathSel + 1) + " OF " + path.Count + "     time " + (p.t - t0).ToString("0.00") + " s");
        if (GUI.Button(new Rect(a.xMax - 34, y, 26, 22), "x")) pathSel = -1;
        y += 26;
        if (GUI.Button(new Rect(x, y, 90, 26), "< 0.1 s  [")) MovePointTime(-0.1f);
        if (GUI.Button(new Rect(x + 94, y, 90, 26), "]  0.1 s >")) MovePointTime(0.1f);
        if (GUI.Button(new Rect(x + 194, y, 140, 26), "Move to the time")) { float lo = pathSel > 0 ? path[pathSel - 1].t + 0.05f : t0, hi = pathSel < path.Count - 1 ? path[pathSel + 1].t - 0.05f : t1; p.t = Mathf.Clamp(playT, lo, Mathf.Max(lo, hi)); }
        if (GUI.Button(new Rect(x + 338, y, 130, 26), "Delete (Del)")) { RemovePoint(); return; }
        y += 30;
        if (GUI.Button(new Rect(x, y, 140, 26), "Set to this view")) { Vector3 vp; Quaternion vr; float vf; View(camMode == 3 ? 1 : camMode, out vp, out vr, out vf); p.pos = vp; p.rot = vr; status = "Point " + (pathSel + 1) + " set to the " + (camMode == 2 ? "orbit" : camMode == 0 ? "recorded" : "free") + " view."; }
        if (GUI.Button(new Rect(x + 144, y, 140, 26), "Look from point")) { freePos = p.pos; Vector3 e = p.rot.eulerAngles; freeYaw = e.y; freePitch = e.x > 180f ? e.x - 360f : e.x; camMode = 1; playT = p.t; playing = false; }
        if (GUI.Button(new Rect(x + 288, y, 140, 26), "Aim at skater")) { Vector3 d = SkaterAt(p.t) + Vector3.up * 0.3f - p.pos; if (d.sqrMagnitude > 0.01f) p.rot = Quaternion.LookRotation(d, Vector3.up); }
        y += 32;
        GUI.Label(new Rect(x, y, 60, 22), "Zoom");
        p.fov = GUI.HorizontalSlider(new Rect(x + 50, y + 6, 260, 16), p.fov, 15f, 110f);
        GUI.Label(new Rect(x + 320, y, 140, 22), p.fov.ToString("0") + " deg view");
        y += 24;
        GUI.Label(new Rect(x, y, a.width - 20, 22), "Drag the point in the view to move it. Shift + drag up and down: nearer and farther.");
    }

    // free camera: go to a place near the skater, looking at him
    void GoToSkater()
    {
        Quaternion look = Quaternion.Euler(0f, freeYaw, 0f);
        freePos = skaterCenter - look * Vector3.forward * 3.5f + Vector3.up * 1.2f;
        Vector3 d = skaterCenter - freePos;
        Vector3 e = Quaternion.LookRotation(d, Vector3.up).eulerAngles;
        freeYaw = e.y; freePitch = e.x > 180f ? e.x - 360f : e.x;
        camMode = 1;
    }
    void AfterDraw()
    {
        if (posesOn && parts != null) for (int k = 0; k < parts.Length; k++) if (parts[k] != null) parts[k].SetPositionAndRotation(keepPos[k], keepRot[k]);
        posesOn = false;
        if (camOn && keepCam != null) { keepCam.transform.SetPositionAndRotation(keepCamPos, keepCamRot); keepCam.fieldOfView = keepFov; }
        camOn = false;
    }

    // ---------- clips ----------
    static string SceneName() { return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
    void SaveClip()
    {
        int a = Find(inT), b = Find(outT);
        if (b - a < 5) { status = "The part between the markers is too short."; return; }
        Directory.CreateDirectory(Dir);
        int no = 1; while (File.Exists(Path.Combine(Dir, "Clip " + no + ".ssr"))) no++;
        string name = "Clip " + no;
        string file = Path.Combine(Dir, name + ".ssr");
        using (BinaryWriter w = new BinaryWriter(File.Open(file, FileMode.Create)))
        {
            w.Write("SSR1"); w.Write(name); w.Write(SceneName()); w.Write(partsKey); w.Write(src.n); w.Write(b - a + 1);
            for (int f = a; f <= b; f++)
            {
                w.Write(src.time[f] - src.time[a]);
                V(w, src.camPos[f]); Q(w, src.camRot[f]); w.Write(src.fov[f]);
                for (int k = 0; k < src.n; k++) { V(w, src.pos[f * src.n + k]); Q(w, src.rot[f * src.n + k]); }
            }
            w.Write("PATH"); w.Write(path.Count); w.Write(pathLook);
            foreach (PathPt p in path) { w.Write(p.t - src.time[a]); V(w, p.pos); Q(w, p.rot); w.Write(p.fov); }
            WriteLens(w);
        }
        status = "Saved " + name + " (" + (outT - inT).ToString("0.0") + " s). It is in the Replay tab.";
        nextClipScan = 0f;
    }
    static void V(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
    static void Q(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
    static Vector3 RV(BinaryReader r) { return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
    static Quaternion RQ(BinaryReader r) { return new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
    void ScanClips()
    {
        clips.Clear();
        if (!Directory.Exists(Dir)) return;
        foreach (string f in Directory.GetFiles(Dir, "*.ssr"))
        {
            try
            {
                using (BinaryReader r = new BinaryReader(File.OpenRead(f)))
                {
                    if (r.ReadString() != "SSR1") continue;
                    ClipInfo c = new ClipInfo { file = f, name = r.ReadString(), scene = r.ReadString() };
                    r.ReadString(); int n = r.ReadInt32(); int count = r.ReadInt32();
                    long frameBytes = 4 + 12 + 16 + 4 + (long)n * 28;
                    r.BaseStream.Seek((count - 1) * frameBytes, SeekOrigin.Current);
                    c.length = count > 0 ? r.ReadSingle() : 0f;
                    clips.Add(c);
                }
            }
            catch (Exception) { }
        }
        clips.Sort((x, y) => string.Compare(x.name, y.name, StringComparison.OrdinalIgnoreCase));
    }
    public void PlayClip(string file)
    {
        try
        {
            if (active) Exit();
            FindParts();
            if (parts == null) { status = "The skater is not loaded."; return; }
            using (BinaryReader r = new BinaryReader(File.OpenRead(file)))
            {
                if (r.ReadString() != "SSR1") { status = "Not a clip file."; return; }
                string name = r.ReadString(), scene = r.ReadString(), key = r.ReadString();
                if (scene != SceneName()) { status = name + " was recorded in " + scene + ". Go to that level to play it."; return; }
                if (key != partsKey) { status = name + " does not fit the skater of this game (other parts)."; return; }
                int n = r.ReadInt32(), count = r.ReadInt32();
                Buffer b = new Buffer(count, n);
                float baseT = Time.unscaledTime;
                for (int f = 0; f < count; f++)
                {
                    b.time[f] = baseT + r.ReadSingle();
                    b.camPos[f] = RV(r); b.camRot[f] = RQ(r); b.fov[f] = r.ReadSingle();
                    for (int k = 0; k < n; k++) { b.pos[f * n + k] = RV(r); b.rot[f * n + k] = RQ(r); }
                }
                b.count = count; src = b; srcName = name;
                path.Clear(); pathSel = -1;
                if (r.BaseStream.Position < r.BaseStream.Length && r.ReadString() == "PATH")
                {
                    int pc = r.ReadInt32(); pathLook = r.ReadBoolean();
                    for (int q = 0; q < pc; q++) path.Add(new PathPt { t = baseT + r.ReadSingle(), pos = RV(r), rot = RQ(r), fov = r.ReadSingle() });
                    ReadLens(r);
                }
            }
            Start();
            playT = t0;
            if (path.Count >= 2) camMode = 3;
        }
        catch (Exception e) { status = "Could not play the clip: " + e.Message; }
    }

    // ---------- the replay screen (drawn over the game while a replay is on) ----------
    Texture2D white;
    void Fill(Rect r, Color c) { if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); } Color o = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = o; }
    bool dragging;
    public void DrawOverlay()
    {
        if (!active || src == null) return;
        Event e = Event.current;
        float s = Mathf.Clamp(Screen.height / 1080f, 0.6f, 3f);
        Matrix4x4 old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = Screen.height / s;
        gW = W; gH = H;
        if (uiHidden)
        {
            GUI.Label(new Rect(10, H - 24, 400, 22), "H = show the replay controls");
            GUI.matrix = old; return;
        }
        const float PH = 190f;
        Rect looksRect = new Rect(W - 520, H - PH - 160, 500, 150);
        bool selOk = showPath && pathSel >= 0 && pathSel < path.Count;
        Rect pointRect = new Rect(16, H - PH - 170, 500, 160);
        Rect lensRect = new Rect(W * 0.5f - 270f, H - PH - 232, 540, 222);
        DrawPathView(e, H - PH, new Rect[] { looksOpen ? looksRect : new Rect(0, 0, 0, 0), selOk ? pointRect : new Rect(0, 0, 0, 0), lensOpen ? lensRect : new Rect(0, 0, 0, 0) });
        Rect panel = new Rect(0, H - PH, W, PH);
        Fill(panel, new Color(0f, 0f, 0f, 0.72f));
        GUI.Label(new Rect(16, H - PH + 4, W - 300, 22), "REPLAY   " + srcName + (status.Length > 0 ? "     " + status : ""));
        if (GUI.Button(new Rect(W - 126, H - PH + 4, 110, 26), "Exit (" + replayKey + ")")) { Exit(); GUI.matrix = old; return; }

        // the timeline
        Rect bar = new Rect(16, H - PH + 40, W - 32, 26);
        Fill(bar, new Color(1f, 1f, 1f, 0.12f));
        float len = Mathf.Max(0.001f, t1 - t0);
        float xIn = bar.x + (inT - t0) / len * bar.width, xOut = bar.x + (outT - t0) / len * bar.width;
        Fill(new Rect(xIn, bar.y, Mathf.Max(1f, xOut - xIn), bar.height), new Color(0.3f, 0.6f, 1f, 0.35f));
        Fill(new Rect(xIn - 1, bar.y - 4, 3, bar.height + 8), new Color(0.3f, 0.7f, 1f, 1f));
        Fill(new Rect(xOut - 1, bar.y - 4, 3, bar.height + 8), new Color(0.3f, 0.7f, 1f, 1f));
        for (int sec = 0; sec <= (int)len; sec++)
        {   // one tick each second
            float tx = bar.x + sec / len * bar.width;
            Fill(new Rect(tx, bar.y + bar.height - 6, 1, 6), new Color(1f, 1f, 1f, 0.4f));
        }
        Vector2 mp = e.mousePosition;
        // the camera path points: yellow marks above the bar. Click a mark to go to it and select it.
        Color yel = new Color(1f, 0.82f, 0.2f, 1f);
        if (path.Count > 1)
            for (int k = 0; k < path.Count - 1; k++)
            {
                float xa = bar.x + (path[k].t - t0) / len * bar.width, xb = bar.x + (path[k + 1].t - t0) / len * bar.width;
                Fill(new Rect(xa, bar.y - 9, Mathf.Max(1f, xb - xa), 2), new Color(1f, 0.82f, 0.2f, camMode == 3 ? 0.8f : 0.35f));
            }
        for (int k = 0; k < path.Count; k++)
        {
            float px = bar.x + (path[k].t - t0) / len * bar.width;
            Rect m = new Rect(px - 6, bar.y - 16, 12, 12);
            Fill(m, k == pathSel ? Color.white : yel);
            if (e.type == EventType.MouseDown && e.button == 0 && new Rect(m.x - 3, m.y - 3, m.width + 6, m.height + 6).Contains(mp))
            { pathSel = k; playT = path[k].t; playing = false; e.Use(); }
        }
        float xp = bar.x + (playT - t0) / len * bar.width;
        Fill(new Rect(xp - 2, bar.y - 4, 4, bar.height + 10), Color.white);
        // drag on the bar to scrub
        if (e.type == EventType.MouseDown && e.button == 0 && new Rect(bar.x, bar.y - 2, bar.width, bar.height + 10).Contains(mp)) { dragging = true; playing = false; pathSel = -1; e.Use(); }
        if (e.type == EventType.MouseUp && e.button == 0) dragging = false;
        if (dragging && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown))
        { playT = Mathf.Clamp(t0 + (mp.x - bar.x) / bar.width * len, t0, t1); e.Use(); }
        GUI.Label(new Rect(bar.x, bar.y + bar.height + 2, 300, 20), (playT - t0).ToString("0.00") + " s  /  " + len.ToString("0.00") + " s");
        GUI.Label(new Rect(bar.x + bar.width - 330, bar.y + bar.height + 2, 330, 20), "Clip part: " + (inT - t0).ToString("0.00") + " s to " + (outT - t0).ToString("0.00") + " s");

        // row 1: play and the clip
        float y = H - PH + 94, x = 16;
        if (GUI.Button(new Rect(x, y, 44, 30), "|<")) playT = inT; x += 48;
        if (GUI.Button(new Rect(x, y, 44, 30), "<")) Step(-1); x += 48;
        if (GUI.Button(new Rect(x, y, 80, 30), playing ? "Pause" : "Play")) TogglePlay(); x += 84;
        if (GUI.Button(new Rect(x, y, 44, 30), ">")) Step(1); x += 48;
        if (GUI.Button(new Rect(x, y, 44, 30), ">|")) playT = outT; x += 60;
        GUI.Label(new Rect(x, y + 5, 48, 22), "Speed"); x += 48;
        foreach (float sp in speeds)
        {
            if (GUI.Toggle(new Rect(x, y, 46, 30), Mathf.Abs(speed - sp) < 0.001f, new GUIContent(sp + "x"), GUI.skin.button)) speed = sp;
            x += 50;
        }
        x += 10;
        loop = GUI.Toggle(new Rect(x, y + 5, 60, 22), loop, new GUIContent(" Loop"), GUI.skin.toggle); x += 70;
        if (GUI.Button(new Rect(x, y, 70, 30), "Set in")) SetIn(); x += 74;
        if (GUI.Button(new Rect(x, y, 70, 30), "Set out")) SetOut(); x += 74;
        if (GUI.Button(new Rect(x, y, 90, 30), "Save clip")) SaveClip(); x += 104;
        if (GUI.Toggle(new Rect(x, y, 90, 30), looksOpen, new GUIContent("Looks"), GUI.skin.button) != looksOpen) looksOpen = !looksOpen;
        x += 94;
        if (GUI.Toggle(new Rect(x, y, 90, 30), lensOpen, new GUIContent("Lens"), GUI.skin.button) != lensOpen) lensOpen = !lensOpen;
        x += 94;
        LookTools lt = LookTools.inst;
        if (lt != null && (lt.greenOn || lt.depthOn)) GUI.Label(new Rect(x, y + 5, 200, 22), (lt.greenOn ? "Green screen on " : "") + (lt.depthOn ? "Depth map on" : ""));
        // the free camera speeds (saved)
        float fx = Mathf.Max(x + 150f, W - 580f);
        GUI.Label(new Rect(fx, y + 5, 110, 22), "Free cam: move");
        float ns = Mathf.Exp(GUI.HorizontalSlider(new Rect(fx + 112, y + 11, 130, 16), Mathf.Log(freeSpeed), Mathf.Log(0.2f), Mathf.Log(60f)));
        if (Mathf.Abs(ns - freeSpeed) > 0.0001f) { freeSpeed = ns; speedChanged = true; }
        GUI.Label(new Rect(fx + 248, y + 5, 50, 22), freeSpeed.ToString(freeSpeed < 10f ? "0.0" : "0"));
        GUI.Label(new Rect(fx + 300, y + 5, 40, 22), "look");
        float nl = GUI.HorizontalSlider(new Rect(fx + 340, y + 11, 130, 16), lookSpeed, 0.3f, 10f);
        if (Mathf.Abs(nl - lookSpeed) > 0.0001f) { lookSpeed = nl; speedChanged = true; }
        GUI.Label(new Rect(fx + 476, y + 5, 50, 22), lookSpeed.ToString("0.0"));
        if (speedChanged && e.type == EventType.MouseUp) { speedChanged = false; dirty = true; }

        // row 2: the camera
        y += 36; x = 16;
        GUI.Label(new Rect(x, y + 5, 60, 22), "Camera"); x += 58;
        string[] cm = { "Recorded", "Free", "Orbit", "Path" };
        for (int k = 0; k < 4; k++) { if (GUI.Toggle(new Rect(x, y, 80, 30), camMode == k, new GUIContent(cm[k]), GUI.skin.button)) camMode = k; x += 84; }
        x += 16;
        if (GUI.Button(new Rect(x, y, 130, 30), "Go to skater (T)")) GoToSkater(); x += 150;
        GUI.Label(new Rect(x, y + 5, 90, 22), "Path points"); x += 88;
        if (GUI.Button(new Rect(x, y, 120, 30), "Add point (K)")) AddPoint(); x += 124;
        if (GUI.Button(new Rect(x, y, 80, 30), "Remove")) RemovePoint(); x += 84;
        if (GUI.Button(new Rect(x, y, 60, 30), "Clear")) { path.Clear(); pathSel = -1; status = "All path points removed."; } x += 70;
        pathLook = GUI.Toggle(new Rect(x, y + 5, 130, 22), pathLook, new GUIContent(" Look at skater"), GUI.skin.toggle); x += 134;
        pathEase = GUI.Toggle(new Rect(x, y + 5, 150, 22), pathEase, new GUIContent(" Slow at points"), GUI.skin.toggle); x += 150;
        showPath = GUI.Toggle(new Rect(x, y, 130, 30), showPath, new GUIContent("Show path (V)"), GUI.skin.button); x += 140;
        GUI.Label(new Rect(x, y + 5, 120, 22), path.Count + (path.Count == 1 ? " point" : " points"));

        GUI.Label(new Rect(16, H - 26, W - 32, 22), camMode == 1 ? "Free camera: hold the right mouse button to look, WASD to move, Q/E down and up, Shift = fast, Ctrl = slow, wheel = speed, T = go to the skater.   Space = play, H = hide this"
            : camMode == 2 ? "Orbit: hold the right mouse button to turn around the skater, wheel = closer or farther.   Space = play, arrows = step, I/O = in/out, H = hide this"
            : camMode == 3 ? (path.Count < 2 ? "Camera path: put the camera where you want it (Free or Orbit), move the time, press K. Do this 2 or more times, then choose Path."
                                             : "Camera path: the camera glides through the yellow points. V = show or hide the path. Click a point (in the view or on the timeline) to edit it. [ ] = move its time.")
            : "Space = play/pause, Left/Right = one frame, Shift+arrows = slow scrub, Up/Down = speed, I/O = in/out, C = camera, K = add path point, H = hide this");

        // the Looks panel (green screen and depth map), above the replay panel
        if (looksOpen && lt != null)
        {
            Rect a = looksRect;
            Fill(a, new Color(0f, 0f, 0f, 0.8f));
            lt.DrawReplay(a);
            if (a.Contains(mp) && (e.type == EventType.MouseDown || e.type == EventType.MouseUp)) e.Use();
        }
        if (lensOpen)
        {
            DrawLensPanel(lensRect);
            if (lensRect.Contains(mp) && (e.type == EventType.MouseDown || e.type == EventType.MouseUp)) e.Use();
        }
        if (selOk && pathSel < path.Count)
        {
            DrawPointPanel(pointRect);
            if (pointRect.Contains(mp) && (e.type == EventType.MouseDown || e.type == EventType.MouseUp)) e.Use();
        }
        GUI.matrix = old;
    }
    bool looksOpen, speedChanged;

    // ---------- the Replay tab of the editor ----------
    Vector2 scroll;
    public void Draw()
    {
        float now = Time.unscaledTime;
        if (now >= nextClipScan) { nextClipScan = now + 3f; ScanClips(); }
        if (status.Length > 0) GUILayout.Label(status);
        GUILayout.Label("REPLAY     Press " + replayKey + " while you skate: the game stops, and you can play the last seconds again.");
        bool r = GUILayout.Toggle(recordOn, " Record all the time (the newest seconds are kept)");
        if (r != recordOn) { recordOn = r; dirty = true; }
        GUILayout.BeginHorizontal();
        GUILayout.Label("   Keep the last", GUILayout.Width(120));
        float[] opts = { 10f, 20f, 30f, 60f, 120f };
        foreach (float o in opts)
            if (GUILayout.Toggle(Mathf.Abs(bufferSeconds - o) < 0.5f, o + " s", GUI.skin.button, GUILayout.Width(60)) && Mathf.Abs(bufferSeconds - o) >= 0.5f) { bufferSeconds = o; dirty = true; live = null; }
        GUILayout.EndHorizontal();
        float have = live != null && live.count > 1 ? live.time[live.Index(live.count - 1)] - live.time[live.head] : 0f;
        GUILayout.Label("   Recorded now: " + have.ToString("0") + " s" + (parts != null ? "   (" + parts.Length + " parts of the skater and board)" : "   (the skater is not loaded)"));
        GUILayout.Label("   The recording is the skater, the board and the camera. The rest of the level stays still, and there is no sound.");
        GUILayout.Space(6);
        if (GUILayout.Button("Start the replay now (" + replayKey + ")", GUILayout.Width(240))) Enter();

        GUILayout.Space(12);
        GUILayout.Label("SAVED CLIPS     In the replay: set in and out, then Save clip. A clip plays in the level where you recorded it.");
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(280));
        string scene = SceneName();
        foreach (ClipInfo c in clips)
        {
            GUILayout.BeginHorizontal();
            bool here = c.scene == scene;
            GUI.enabled = here;
            if (GUILayout.Button("Play", GUILayout.Width(60))) PlayClip(c.file);
            GUI.enabled = true;
            GUILayout.Label(c.name + "   " + c.length.ToString("0.0") + " s   " + (here ? "(this level)" : "(level: " + c.scene + ")"), GUILayout.Width(520));
            if (GUILayout.Button("Delete", GUILayout.Width(70))) { try { File.Delete(c.file); } catch (Exception) { } nextClipScan = 0f; }
            GUILayout.EndHorizontal();
        }
        if (clips.Count == 0) GUILayout.Label("No clips saved yet.");
        GUILayout.EndScrollView();
        GUILayout.Label("Clips are in: " + Dir);
    }
}

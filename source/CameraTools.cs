// Camera tab of the editor.
// Camera view: the game's own camera does all its normal work (follow, smoothing, wall checks). The editor only
// moves its final view by an offset just before the camera draws, and puts it back after the camera draws.
// All offsets at zero = exactly the game's camera.
// Rig shots: the camera has its own place around the player (in front, at the side, above, low, behind), turned with the
// direction of travel (smoothed, so spins in tricks do not turn it), and always aimed at the player.
// Fixed spots: a saved place and direction of the camera in a level. The camera stays there while you skate, and can
// turn to follow you.
// Walls: a sphere check from the player to the camera, which ignores the skater and the board. A wall pulls the
// camera in fast, and it goes back out slowly, so it does not jump.
// Camera effects: depth of field, motion blur, vignette, grain, color fringe, bloom, through a post-processing volume of its own.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

public class CameraTools
{
    public bool dirty;                 // a setting changed: the owner saves the state file
    public string status = "";

    // ----- camera view (offsets from the game camera) -----
    float back, up, side, tilt, turn, fovAdd;
    bool walls = true, skateOnly = true;
    Camera cam;
    bool applied; Vector3 savedPos; Quaternion savedRot; float savedFov;

    class Setup { public string name; public float back, up, side, tilt, turn, fovAdd; }
    // mode: 0 = game camera with offsets, 1 = rig shot, 2 = fixed spot
    int mode;
    float rigYaw = 180f, rigDist = 3f, rigHeight = 0.8f, rigAim = 0.9f, rigFov = 60f, rigLag = 0.15f;
    string rigName = "Front";
    class Spot { public string name, scene; public Vector3 pos; public Quaternion rot; public float fov; public bool follow; }
    List<Spot> spots = new List<Spot>(); Spot activeSpot;
    Vector2 spotScroll;
    // smoothing state
    Vector3 camPos; float headYaw; bool haveRig; float wallDist = -1f; int lastFrame;
    List<Setup> setups = new List<Setup>();
    string nameField = ""; bool nameFocus;
    Vector2 setupScroll;

    // ----- effects -----
    // mode: 0 = the game's setting, 1 = off, 2 = my setting
    int dofMode, mblurMode, vigMode, grainMode, caMode, bloomMode;
    bool dofAuto = true;
    float dofDist = 5f, dofAperture = 2.8f, dofFocal = 50f; int dofKernel = 1;
    float mblurAngle = 270f, vigInt = 0.35f, grainInt = 0.3f, caInt = 0.5f, bloomInt = 3f;
    GameObject volGo; PostProcessVolume vol; PostProcessProfile prof;
    DepthOfField dof; MotionBlur mblur; Vignette vig; Grain grain; ChromaticAberration ca; Bloom bloom;
    bool noLayer;

    public CameraTools()
    {
        Camera.onPreCull += OnPreCull;
        Camera.onPostRender += OnPostRender;
    }

    // ---------- settings file ----------
    static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
    static float P(string s, float d) { float v; return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : d; }
    public void Load(string line)
    {
        line = line.Trim();
        if (line.StartsWith("cam.offset="))
        {
            string[] f = line.Substring(11).Split(',');
            if (f.Length < 8) return;
            back = P(f[0], 0); up = P(f[1], 0); side = P(f[2], 0); tilt = P(f[3], 0); turn = P(f[4], 0); fovAdd = P(f[5], 0);
            walls = f[6] == "1"; skateOnly = f[7] == "1";
        }
        else if (line.StartsWith("cam.rig="))
        {
            string[] f = line.Substring(8).Split(',');
            if (f.Length < 8) return;
            mode = Mathf.Clamp((int)P(f[0], 0), 0, 1);       // a fixed spot is not active again at the start
            rigYaw = P(f[1], 180); rigDist = P(f[2], 3); rigHeight = P(f[3], 0.8f); rigAim = P(f[4], 0.9f); rigFov = P(f[5], 60); rigLag = P(f[6], 0.15f); rigName = f[7];
        }
        else if (line.StartsWith("cam.spot2="))
        {
            string[] f = line.Substring(10).Split('|');
            if (f.Length < 11) return;
            Spot sp = new Spot { name = f[0], scene = f[1] };
            sp.pos = new Vector3(P(f[2], 0), P(f[3], 0), P(f[4], 0));
            sp.rot = new Quaternion(P(f[5], 0), P(f[6], 0), P(f[7], 0), P(f[8], 1));
            sp.fov = P(f[9], 60); sp.follow = f[10] == "1";
            spots.Add(sp);
        }
        else if (line.StartsWith("cam.setup="))
        {
            string[] f = line.Substring(10).Split('|');
            if (f.Length < 7) return;
            Setup s = new Setup(); s.name = f[0];
            s.back = P(f[1], 0); s.up = P(f[2], 0); s.side = P(f[3], 0); s.tilt = P(f[4], 0); s.turn = P(f[5], 0); s.fovAdd = P(f[6], 0);
            setups.Add(s);
        }
        else if (line.StartsWith("cam.fx="))
        {
            string[] f = line.Substring(7).Split(',');
            if (f.Length < 16) return;
            int i = 0;
            dofMode = (int)P(f[i++], 0); dofAuto = f[i++] == "1"; dofDist = P(f[i++], 5); dofAperture = P(f[i++], 2.8f); dofFocal = P(f[i++], 50); dofKernel = (int)P(f[i++], 1);
            mblurMode = (int)P(f[i++], 0); mblurAngle = P(f[i++], 270);
            vigMode = (int)P(f[i++], 0); vigInt = P(f[i++], 0.35f);
            grainMode = (int)P(f[i++], 0); grainInt = P(f[i++], 0.3f);
            caMode = (int)P(f[i++], 0); caInt = P(f[i++], 0.5f);
            bloomMode = (int)P(f[i++], 0); bloomInt = P(f[i++], 3);
        }
        // lines of the earlier camera versions (cam.mode, cam.follow, cam.view, cam.spot, cam.fov) are not used any more
    }
    public void Write(StringBuilder sb)
    {
        sb.Append("cam.offset=").Append(F(back)).Append(',').Append(F(up)).Append(',').Append(F(side)).Append(',').Append(F(tilt)).Append(',')
          .Append(F(turn)).Append(',').Append(F(fovAdd)).Append(',').Append(walls ? "1" : "0").Append(',').Append(skateOnly ? "1" : "0").Append('\n');
        sb.Append("cam.rig=").Append(mode == 2 ? 0 : mode).Append(',').Append(F(rigYaw)).Append(',').Append(F(rigDist)).Append(',').Append(F(rigHeight))
          .Append(',').Append(F(rigAim)).Append(',').Append(F(rigFov)).Append(',').Append(F(rigLag)).Append(',').Append(rigName).Append('\n');
        foreach (Spot p in spots)
            sb.Append("cam.spot2=").Append(p.name).Append('|').Append(p.scene).Append('|').Append(F(p.pos.x)).Append('|').Append(F(p.pos.y)).Append('|').Append(F(p.pos.z))
              .Append('|').Append(F(p.rot.x)).Append('|').Append(F(p.rot.y)).Append('|').Append(F(p.rot.z)).Append('|').Append(F(p.rot.w))
              .Append('|').Append(F(p.fov)).Append('|').Append(p.follow ? "1" : "0").Append('\n');
        foreach (Setup s in setups)
            sb.Append("cam.setup=").Append(s.name).Append('|').Append(F(s.back)).Append('|').Append(F(s.up)).Append('|').Append(F(s.side))
              .Append('|').Append(F(s.tilt)).Append('|').Append(F(s.turn)).Append('|').Append(F(s.fovAdd)).Append('\n');
        sb.Append("cam.fx=").Append(dofMode).Append(',').Append(dofAuto ? "1" : "0").Append(',').Append(F(dofDist)).Append(',').Append(F(dofAperture)).Append(',').Append(F(dofFocal)).Append(',').Append(dofKernel)
          .Append(',').Append(mblurMode).Append(',').Append(F(mblurAngle)).Append(',').Append(vigMode).Append(',').Append(F(vigInt)).Append(',').Append(grainMode).Append(',').Append(F(grainInt))
          .Append(',').Append(caMode).Append(',').Append(F(caInt)).Append(',').Append(bloomMode).Append(',').Append(F(bloomInt)).Append('\n');
    }

    // ---------- camera view ----------
    Camera Cam()
    {
        if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
        return cam;
    }
    // the point that the game camera follows (moved each drawn frame, not each physics step, so no stutter)
    static Transform Pivot() { try { return ThePlayer.instance != null ? ThePlayer.instance.GetFocusPivot() : null; } catch (Exception) { return null; } }
    static Vector3 PlayerPos()
    {
        Transform p = Pivot();
        if (p != null) return p.position - Vector3.up * 0.9f;
        try { return ThePlayer.lastPlayerOnePosition; } catch (Exception) { return Vector3.zero; }
    }
    static string SceneName() { return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
    // a sphere check from a to b, without the skater and the board; returns the free distance
    static RaycastHit[] hits = new RaycastHit[32];
    static float FreeDistance(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a; float len = d.magnitude;
        if (len < 0.01f) return len;
        Transform me = ThePlayer.instance != null ? ThePlayer.instance.transform : null;
        float best = len;
        int n = Physics.SphereCastNonAlloc(a, 0.2f, d / len, hits, len, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = hits[i];
            if (h.collider == null || h.distance <= 0f) continue;
            if (me != null && h.collider.transform.IsChildOf(me)) continue;       // the skater and the board
            if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue;   // loose objects
            if (h.distance < best) best = h.distance;
        }
        return best;
    }
    // a wall pulls in fast, the way out is slow
    float SmoothWall(float free, float want)
    {
        float target = Mathf.Min(free, want);
        if (wallDist < 0f) wallDist = target;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        wallDist = target < wallDist ? Mathf.Lerp(wallDist, target, 1f - Mathf.Exp(-dt * 30f)) : Mathf.Lerp(wallDist, target, 1f - Mathf.Exp(-dt * 3f));
        return wallDist;
    }
    bool IsDefault() { return back == 0f && up == 0f && side == 0f && tilt == 0f && turn == 0f && fovAdd == 0f; }
    bool ShouldApply()
    {
        if (mode == 2) return activeSpot != null && activeSpot.scene == SceneName();
        if (mode == 0 && IsDefault()) return false;
        if (skateOnly) { try { if (!ThePlayer.SKATEMODE) return false; } catch (Exception) { return false; } }
        return true;
    }
    // flips: the camera keeps the place and direction it had at the start of the flip, relative to the middle of the
    // body, so it moves with the jump but does not turn with the flip. After the flip it blends back.
    bool flipCam; Vector3 flipOffset; Quaternion flipRot; Vector3 flipCenterNow, flipCenterVel; bool haveFlipCenter;
    const float FlipBlend = 0.35f;
    float FlipWeight()
    {
        if (MoveTools.FlipNow) return 1f;
        float t = Time.time - MoveTools.FlipEndTime;
        return t < FlipBlend ? 1f - Mathf.SmoothStep(0f, 1f, t / FlipBlend) : 0f;
    }
    void FlipCamera(Camera c)
    {
        float w = FlipWeight();
        Transform t = c.transform;
        if (MoveTools.FlipNow && !flipCam)
        {   // the start of a flip: the view as it is now, before the turn
            flipCam = true; flipOffset = t.position - MoveTools.FlipCenter; flipRot = t.rotation;
            haveFlipCenter = false; flipCenterVel = Vector3.zero;
        }
        if (w <= 0f) { flipCam = false; return; }
        // the middle of the body as it is drawn this frame, smoothed, so the camera height does not jump with the
        // physics steps of the game
        // The flip turns around the middle of the body, so that point moves on a smooth jump path. It is known at each
        // physics step of the game; between the steps it goes on with the speed of the skater.
        Vector3 raw;
        if (MoveTools.FlipNow) raw = MoveTools.FlipCenter + MoveTools.FlipVel * Mathf.Clamp(Time.time - MoveTools.FlipCenterTime, 0f, 0.1f);
        else raw = PlayerPos() + Vector3.up * MoveTools.FlipPivot;
        if (!haveFlipCenter) { flipCenterNow = raw; haveFlipCenter = true; }
        if (MoveTools.FlipNow) flipCenterNow = raw;
        else flipCenterNow = Vector3.SmoothDamp(flipCenterNow, raw, ref flipCenterVel, 0.06f, 1000f, Mathf.Max(Time.unscaledDeltaTime, 0.001f));
        Vector3 hold = flipCenterNow + flipOffset;
        t.SetPositionAndRotation(Vector3.Lerp(t.position, hold, w), Quaternion.Slerp(t.rotation, flipRot, w));
    }

    void OnPreCull(Camera c)
    {   // runs just before the camera draws, after all the game's camera code
        if (c == null || c != cam || applied) return;
        // rig shots and fixed spots do not turn with the skater, so only the game camera needs the hold
        if (mode == 0 && (FlipWeight() > 0f || flipCam))
        {
            if (!applied) { savedPos = c.transform.position; savedRot = c.transform.rotation; savedFov = c.fieldOfView; applied = true; }
            if (mode == 0 && ShouldApply()) { /* offsets: applied below, then the flip hold */ }
            else { FlipCamera(c); return; }
        }
        if (!ShouldApply()) return;
        try
        {
            Transform t = c.transform;
            if (!applied) { savedPos = t.position; savedRot = t.rotation; savedFov = c.fieldOfView; applied = true; }
            if (mode == 1) { Rig(c); return; }
            if (mode == 2) { FixedSpot(c); return; }
            Vector3 fwd = savedRot * Vector3.forward, right = savedRot * Vector3.right;
            Vector3 flatRight = right; flatRight.y = 0f;
            if (flatRight.sqrMagnitude < 0.0001f) flatRight = right;
            flatRight.Normalize();
            // the point that the game camera aims at: the player's distance along its view
            float d = Mathf.Max(1f, Vector3.Distance(savedPos, PlayerPos() + Vector3.up * 0.9f));
            Vector3 target = savedPos + fwd * d + flatRight * side;
            Vector3 np = savedPos - fwd * back + Vector3.up * up + flatRight * side;
            // not closer to the player than 0.5 m
            Vector3 pp = PlayerPos() + Vector3.up * 0.9f;
            if (Vector3.Distance(np, pp) < 0.5f) np = pp + (np - pp).normalized * 0.5f;
            if (walls)
            {   // a wall between the player and the new place: stop in front of the wall (smoothed)
                Vector3 dir = np - pp; float len = dir.magnitude;
                if (len > 0.01f) np = pp + dir / len * SmoothWall(FreeDistance(pp, np), len);
            }
            Vector3 look = target - np;
            Quaternion r = look.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(look, savedRot * Vector3.up) : savedRot;
            r = Quaternion.AngleAxis(turn, Vector3.up) * r * Quaternion.Euler(tilt, 0f, 0f);
            t.SetPositionAndRotation(np, r);
            if (fovAdd != 0f) c.fieldOfView = Mathf.Clamp(savedFov + fovAdd, 5f, 150f);
            if (FlipWeight() > 0f || flipCam) FlipCamera(c);
        }
        catch (Exception e) { status = "Camera error: " + e.Message; }
    }
    void Rig(Camera c)
    {
        Transform t = c.transform;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        Vector3 focus = PlayerPos() + Vector3.up * rigAim;
        // the direction of travel, smoothed: spins in tricks do not turn the camera
        Vector3 v = Vector3.zero;
        try { SkatePlayer sp = ThePlayer.SkatePlayer; if (sp != null) v = sp.skPhysics.velocity; } catch (Exception) { }
        v.y = 0f;
        float yawNow = headYaw;
        if (v.sqrMagnitude > 0.5f) yawNow = Mathf.Atan2(v.x, v.z) * 57.29578f;
        else { Transform p = Pivot(); if (p != null) { Vector3 f = p.forward; f.y = 0f; if (f.sqrMagnitude > 0.01f) yawNow = Mathf.Atan2(f.x, f.z) * 57.29578f; } }
        if (!haveRig || Time.frameCount - lastFrame > 5) { headYaw = yawNow; haveRig = true; camPos = Vector3.zero; wallDist = -1f; }
        lastFrame = Time.frameCount;
        headYaw = Mathf.LerpAngle(headYaw, yawNow, 1f - Mathf.Exp(-dt * 4f));
        Quaternion h = Quaternion.Euler(0f, headYaw + rigYaw, 0f);
        Vector3 off = h * new Vector3(0f, 0f, -rigDist) + Vector3.up * rigHeight;     // rigYaw 0 = behind, 180 = in front
        Vector3 want = focus + off;
        if (walls) { float len = off.magnitude; if (len > 0.01f) want = focus + off / len * SmoothWall(FreeDistance(focus, want), len); }
        // a short lag gives a camera person feel, and takes away small jitters
        if (camPos == Vector3.zero || rigLag <= 0f) camPos = want;
        else camPos = Vector3.Lerp(camPos, want, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, rigLag)));
        Vector3 look = focus - camPos;
        Quaternion r = look.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(look, Vector3.up) : savedRot;
        r = r * Quaternion.Euler(tilt, turn, 0f);
        t.SetPositionAndRotation(camPos, r);
        c.fieldOfView = Mathf.Clamp(rigFov, 5f, 150f);
    }
    void FixedSpot(Camera c)
    {
        Spot s = activeSpot;
        Quaternion r = s.rot;
        if (s.follow)
        {
            Vector3 look = PlayerPos() + Vector3.up * 0.9f - s.pos;
            if (look.sqrMagnitude > 0.0001f) r = Quaternion.LookRotation(look, Vector3.up);
        }
        c.transform.SetPositionAndRotation(s.pos, r);
        c.fieldOfView = Mathf.Clamp(s.fov, 5f, 150f);
    }

    Vector3 lastDrawnPos; Quaternion lastDrawnRot = Quaternion.identity; float lastDrawnFov = 60f;
    void OnPostRender(Camera c)
    {   // put the game camera back, so the game's own camera code finds its own values in the next frame
        if (c == null || c != cam) return;
        lastDrawnPos = c.transform.position; lastDrawnRot = c.transform.rotation; lastDrawnFov = c.fieldOfView;
        if (!applied) return;
        c.transform.SetPositionAndRotation(savedPos, savedRot);
        c.fieldOfView = savedFov;
        applied = false;
    }

    // ---------- effects ----------
    void EnsureVolume()
    {
        Camera c = Cam();
        if (c == null || (volGo != null && vol != null)) return;
        PostProcessLayer layer = c.GetComponent<PostProcessLayer>();
        if (layer == null) { noLayer = true; return; }
        noLayer = false;
        int mask = layer.volumeLayer.value, li = -1;
        for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) { li = i; break; }
        if (li < 0) { noLayer = true; return; }
        if (prof == null)
        {
            prof = ScriptableObject.CreateInstance<PostProcessProfile>();
            dof = prof.AddSettings<DepthOfField>(); mblur = prof.AddSettings<MotionBlur>(); vig = prof.AddSettings<Vignette>();
            grain = prof.AddSettings<Grain>(); ca = prof.AddSettings<ChromaticAberration>(); bloom = prof.AddSettings<Bloom>();
        }
        volGo = new GameObject("ShoeModCameraEffects");
        UnityEngine.Object.DontDestroyOnLoad(volGo);
        volGo.layer = li;
        vol = volGo.AddComponent<PostProcessVolume>();
        vol.isGlobal = true; vol.priority = 10000f; vol.weight = 1f; vol.sharedProfile = prof;
    }
    static void Set(ParameterOverride<float> p, bool on, float v) { p.overrideState = on; if (on) p.value = v; }
    static void FxMode(PostProcessEffectSettings s, int m)
    {
        s.active = m != 0;
        s.enabled.overrideState = m != 0;
        s.enabled.value = m == 2;
    }
    void ApplyEffects()
    {
        if (prof == null) return;
        FxMode(dof, dofMode);
        float d = dofDist;
        if (dofAuto && Cam() != null)
        {   // the distance from the camera place that is drawn
            Vector3 cp = mode == 1 && camPos != Vector3.zero ? camPos : mode == 2 && activeSpot != null ? activeSpot.pos : cam.transform.position;
            d = Mathf.Max(0.1f, Vector3.Distance(cp, PlayerPos() + Vector3.up * 0.9f) + (mode == 0 ? back : 0f));
        }
        Set(dof.focusDistance, dofMode == 2, d);
        Set(dof.aperture, dofMode == 2, dofAperture);
        Set(dof.focalLength, dofMode == 2, dofFocal);
        dof.kernelSize.overrideState = dofMode == 2; dof.kernelSize.value = (KernelSize)Mathf.Clamp(dofKernel, 0, 3);
        FxMode(mblur, mblurMode); Set(mblur.shutterAngle, mblurMode == 2, mblurAngle);
        FxMode(vig, vigMode); Set(vig.intensity, vigMode == 2, vigInt);
        FxMode(grain, grainMode); Set(grain.intensity, grainMode == 2, grainInt);
        FxMode(ca, caMode); Set(ca.intensity, caMode == 2, caInt);
        FxMode(bloom, bloomMode); Set(bloom.intensity, bloomMode == 2, bloomInt);
    }
    bool AnyEffect() { return dofMode + mblurMode + vigMode + grainMode + caMode + bloomMode > 0; }

    public void LateUpdate()
    {
        try
        {
            Cam();
            if (AnyEffect() || vol != null)
            {
                EnsureVolume();
                if (vol != null) vol.enabled = AnyEffect();
                ApplyEffects();
            }
        }
        catch (Exception e) { status = "Camera error: " + e.Message; }
    }

    // ---------- menu ----------
    Rect Area(float w, float h) { return GUILayoutUtility.GetRect(w, h, GUI.skin.label, GUILayout.Width(w), GUILayout.Height(h)); }
    float Slider(string label, string left, string right, float v, float min, float max, string fmt, string unit)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(100));
        GUILayout.Label(left, GUILayout.Width(52));
        Rect r = Area(180, 20); r.y += 5;
        float n = GUI.HorizontalSlider(r, v, min, max);
        GUILayout.Label(right, GUILayout.Width(52));
        GUILayout.Label((n > 0f && min < 0f ? "+" : "") + n.ToString(fmt, CultureInfo.InvariantCulture) + unit, GUILayout.Width(56));
        if (GUILayout.Button("0", GUILayout.Width(24))) n = 0f;
        GUILayout.EndHorizontal();
        if (n != v) dirty = true;
        return n;
    }
    float FxSlider(string label, float v, float min, float max, string fmt)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(120));
        Rect r = Area(170, 20); r.y += 5;
        float n = GUI.HorizontalSlider(r, v, min, max);
        GUILayout.Label(n.ToString(fmt, CultureInfo.InvariantCulture), GUILayout.Width(50));
        GUILayout.EndHorizontal();
        if (n != v) dirty = true;
        return n;
    }
    int Modes(string label, int v)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(120));
        string[] n = { "Game", "Off", "Custom" };
        for (int i = 0; i < 3; i++) if (GUILayout.Toggle(v == i, n[i], GUI.skin.button, GUILayout.Width(70)) && v != i) { v = i; dirty = true; }
        GUILayout.EndHorizontal();
        return v;
    }
    void Preset(string name, float b, float u, float s, float ti, float tu, float fa)
    {
        if (!GUILayout.Button(name, GUILayout.Width(110))) return;
        back = b; up = u; side = s; tilt = ti; turn = tu; fovAdd = fa; dirty = true;
        status = name + ".";
    }

    public void Draw()
    {
        if (status.Length > 0) GUILayout.Label(status);
        GUILayout.BeginHorizontal();

        // ----- left: camera view -----
        GUILayout.BeginVertical(GUILayout.Width(500));
        GUILayout.BeginHorizontal();
        GUILayout.Label("CAMERA", GUILayout.Width(70));
        string[] mn = { "Game camera + offsets", "Rig shots", "Fixed spots" };
        for (int i = 0; i < 3; i++)
            if (GUILayout.Toggle(mode == i, mn[i], GUI.skin.button, GUILayout.Width(i == 0 ? 160 : 110)) && mode != i) { mode = i; dirty = true; haveRig = false; wallDist = -1f; }
        GUILayout.EndHorizontal();
        if (mode == 1) { DrawRig(); DrawWalls(); GUILayout.EndVertical(); GUILayout.Space(12); DrawEffects(); GUILayout.EndHorizontal(); return; }
        if (mode == 2) { DrawSpots(); GUILayout.EndVertical(); GUILayout.Space(12); DrawEffects(); GUILayout.EndHorizontal(); return; }
        GUILayout.Label("The game camera, moved by the values below. All at 0 = the game camera.");
        GUILayout.BeginHorizontal();
        Preset("Game camera", 0, 0, 0, 0, 0, 0);
        Preset("Over shoulder", -0.6f, -0.15f, 0.55f, 0, 0, -5);
        Preset("Farther", 1.5f, 0.4f, 0, 0, 0, 0);
        Preset("Closer", -0.8f, -0.1f, 0, 0, 0, 0);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Preset("Low", 0, -0.6f, 0, -6, 0, 0);
        Preset("High", 0.5f, 1.8f, 0, 0, 0, 0);
        Preset("Wide", 0, 0, 0, 0, 0, 20);
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
        back = Slider("Distance", "closer", "farther", back, -2f, 6f, "0.00", " m");
        up = Slider("Height", "lower", "higher", up, -1.5f, 4f, "0.00", " m");
        side = Slider("Side", "left", "right", side, -2f, 2f, "0.00", " m");
        tilt = Slider("Tilt", "up", "down", tilt, -30f, 30f, "0", "°");
        turn = Slider("Turn", "left", "right", turn, -60f, 60f, "0", "°");
        fovAdd = Slider("Zoom", "in", "out", fovAdd, -30f, 40f, "0", "°");
        GUILayout.Label("Height and distance keep the camera aimed at the player. Side moves the view sideways (over the shoulder).");
        DrawWalls();

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        GUILayout.Label("SAVED VIEWS", GUILayout.Width(100));
        if (GUILayout.Toggle(nameFocus, nameFocus ? nameField + "_" : (nameField.Length > 0 ? nameField : "(click to type a name)"), GUI.skin.button, GUILayout.Width(200)) != nameFocus) nameFocus = !nameFocus;
        Event e = Event.current;
        if (nameFocus && e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Backspace) { if (nameField.Length > 0) nameField = nameField.Substring(0, nameField.Length - 1); e.Use(); }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Escape) { nameFocus = false; e.Use(); }
            else { char ch = e.character; if ((char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-') && nameField.Length < 20) nameField += ch; if (ch != 0) e.Use(); }
        }
        if (GUILayout.Button("Save this view", GUILayout.Width(120)))
        {
            Setup s = new Setup(); s.name = nameField.Trim().Length > 0 ? nameField.Trim() : "View " + (setups.Count + 1);
            s.back = back; s.up = up; s.side = side; s.tilt = tilt; s.turn = turn; s.fovAdd = fovAdd;
            setups.RemoveAll(delegate (Setup x) { return x.name == s.name; });
            setups.Add(s); dirty = true; nameFocus = false; status = "Saved: " + s.name;
        }
        GUILayout.EndHorizontal();
        setupScroll = GUILayout.BeginScrollView(setupScroll, GUILayout.Height(100));
        for (int i = 0; i < setups.Count; i++)
        {
            Setup s = setups[i];
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Use", GUILayout.Width(50))) { back = s.back; up = s.up; side = s.side; tilt = s.tilt; turn = s.turn; fovAdd = s.fovAdd; dirty = true; status = "View: " + s.name; }
            if (GUILayout.Button("Delete", GUILayout.Width(60))) { setups.RemoveAt(i); dirty = true; GUILayout.EndHorizontal(); break; }
            GUILayout.Label(s.name);
            GUILayout.EndHorizontal();
        }
        if (setups.Count == 0) GUILayout.Label("No views saved yet.");
        GUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUILayout.Space(12);
        DrawEffects();
        GUILayout.EndHorizontal();
    }

    void DrawWalls()
    {
        bool w = GUILayout.Toggle(walls, " Do not move the camera into walls");
        if (w != walls) { walls = w; dirty = true; wallDist = -1f; }
        bool so = GUILayout.Toggle(skateOnly, " Only while skating (walking and scenes use the game camera)");
        if (so != skateOnly) { skateOnly = so; dirty = true; }
    }

    void RigPreset(string name, float yaw, float dist, float height, float aim, float fov, float lag)
    {
        if (!GUILayout.Toggle(rigName == name, name, GUI.skin.button, GUILayout.Width(110)) || rigName == name) return;
        rigName = name; rigYaw = yaw; rigDist = dist; rigHeight = height; rigAim = aim; rigFov = fov; rigLag = lag; tilt = 0f; turn = 0f;
        dirty = true; haveRig = false; status = name + ".";
    }
    void DrawRig()
    {
        GUILayout.Label("The camera has its own place around you and always aims at you. It turns with the direction you travel.");
        GUILayout.BeginHorizontal();
        RigPreset("Front", 180f, 3f, 0.6f, 0.9f, 60f, 0.12f);        // a camera person in front, looking back at you
        RigPreset("Front low", 180f, 2.2f, -0.4f, 0.6f, 75f, 0.08f);
        RigPreset("Side", 90f, 3.5f, 0.5f, 0.9f, 55f, 0.15f);
        RigPreset("Chase", 0f, 4.5f, 1.4f, 1.1f, 60f, 0.25f);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        RigPreset("Fisheye", 0f, 1.6f, -0.2f, 0.5f, 110f, 0.05f);     // a low follow with a very wide view
        RigPreset("Top down", 0f, 0.5f, 9f, 0f, 50f, 0.2f);
        RigPreset("Drone", 135f, 7f, 4f, 0.8f, 55f, 0.6f);
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
        float o;
        o = rigYaw; rigYaw = Slider("Around", "behind", "front", rigYaw, 0f, 360f, "0", "°"); if (o != rigYaw) rigName = "Custom";
        o = rigDist; rigDist = Slider("Distance", "closer", "farther", rigDist, 0.3f, 15f, "0.0", " m"); if (o != rigDist) rigName = "Custom";
        o = rigHeight; rigHeight = Slider("Height", "lower", "higher", rigHeight, -1f, 12f, "0.0", " m"); if (o != rigHeight) rigName = "Custom";
        o = rigAim; rigAim = Slider("Aim at", "feet", "head", rigAim, 0f, 1.8f, "0.0", " m"); if (o != rigAim) rigName = "Custom";
        o = rigFov; rigFov = Slider("View", "narrow", "wide", rigFov, 20f, 120f, "0", "°"); if (o != rigFov) rigName = "Custom";
        o = rigLag; rigLag = Slider("Lag", "tight", "loose", rigLag, 0f, 1f, "0.00", " s"); if (o != rigLag) rigName = "Custom";
        tilt = Slider("Tilt", "up", "down", tilt, -30f, 30f, "0", "°");
        turn = Slider("Turn", "left", "right", turn, -60f, 60f, "0", "°");
    }

    void DrawSpots()
    {
        GUILayout.Label("Put the camera where you want it, save the spot, then skate. The camera stays there.");
        GUILayout.Label("1. Use the game camera or a rig shot to get the view.  2. Click \"Save the camera here\".");
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(nameFocus, nameFocus ? nameField + "_" : (nameField.Length > 0 ? nameField : "(click to type a name)"), GUI.skin.button, GUILayout.Width(200)) != nameFocus) nameFocus = !nameFocus;
        NameKeys();
        if (GUILayout.Button("Save the camera here", GUILayout.Width(170)))
        {
            Camera c = Cam();
            if (c == null) status = "No camera found.";
            else
            {   // the place that was drawn last (with offsets or a rig shot), not the game camera behind it
                Vector3 p = lastDrawnPos; Quaternion r = lastDrawnRot; float f = lastDrawnFov;
                Spot s = new Spot { name = nameField.Trim().Length > 0 ? nameField.Trim() : "Spot " + (spots.Count + 1), scene = SceneName(), pos = p, rot = r, fov = f, follow = true };
                spots.RemoveAll(x => x.name == s.name && x.scene == s.scene);
                spots.Add(s); dirty = true; nameFocus = false; activeSpot = s; status = "Saved: " + s.name + ". The camera stays there now.";
            }
        }
        GUILayout.EndHorizontal();
        if (activeSpot != null && GUILayout.Button("Let the camera go (back to the game camera)", GUILayout.Width(330))) { activeSpot = null; status = ""; }
        string scene = SceneName();
        spotScroll = GUILayout.BeginScrollView(spotScroll, GUILayout.Height(300));
        int shown = 0;
        for (int i = 0; i < spots.Count; i++)
        {
            Spot s = spots[i];
            if (s.scene != scene) continue;
            shown++;
            GUILayout.BeginHorizontal();
            bool on = activeSpot == s;
            if (GUILayout.Toggle(on, on ? "In use" : "Use", GUI.skin.button, GUILayout.Width(60)) != on) { activeSpot = on ? null : s; }
            bool fo = GUILayout.Toggle(s.follow, " Turn to follow me", GUILayout.Width(150));
            if (fo != s.follow) { s.follow = fo; dirty = true; }
            if (GUILayout.Button("Delete", GUILayout.Width(60))) { if (activeSpot == s) activeSpot = null; spots.RemoveAt(i); dirty = true; GUILayout.EndHorizontal(); break; }
            GUILayout.Label(s.name);
            GUILayout.EndHorizontal();
        }
        if (shown == 0) GUILayout.Label("No spots saved in this level yet.");
        int other = spots.Count - shown;
        if (other > 0) GUILayout.Label(other + " spots saved in other levels. They show in their own level.");
        GUILayout.EndScrollView();
    }
    void NameKeys()
    {
        Event e = Event.current;
        if (!nameFocus || e.type != EventType.KeyDown) return;
        if (e.keyCode == KeyCode.Backspace) { if (nameField.Length > 0) nameField = nameField.Substring(0, nameField.Length - 1); e.Use(); }
        else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Escape) { nameFocus = false; e.Use(); }
        else { char ch = e.character; if ((char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-') && nameField.Length < 20) nameField += ch; if (ch != 0) e.Use(); }
    }

    void DrawEffects()
    {
        // ----- right: effects -----
        GUILayout.BeginVertical(GUILayout.Width(450));
        GUILayout.Label("CAMERA EFFECTS     Game = the game's own setting");
        if (noLayer) GUILayout.Label("The game camera has no effect layer here, so the effects cannot change.");
        dofMode = Modes("Depth of field", dofMode);
        if (dofMode == 2)
        {
            bool da = GUILayout.Toggle(dofAuto, " Focus on the player automatically");
            if (da != dofAuto) { dofAuto = da; dirty = true; }
            if (!dofAuto) dofDist = FxSlider("Focus distance (m)", dofDist, 0.1f, 60f, "0.0");
            dofAperture = FxSlider("Aperture (f-stop)", dofAperture, 0.1f, 32f, "0.0");
            GUILayout.Label("   A low f-stop gives more blur. A high f-stop gives less blur.");
            dofFocal = FxSlider("Focal length (mm)", dofFocal, 1f, 300f, "0");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Blur size", GUILayout.Width(120));
            string[] ks = { "Small", "Medium", "Large", "Very large" };
            for (int i = 0; i < 4; i++) if (GUILayout.Toggle(dofKernel == i, ks[i], GUI.skin.button, GUILayout.Width(74)) && dofKernel != i) { dofKernel = i; dirty = true; }
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(4);
        mblurMode = Modes("Motion blur", mblurMode);
        if (mblurMode == 2) mblurAngle = FxSlider("Strength (shutter)", mblurAngle, 0f, 360f, "0");
        vigMode = Modes("Vignette", vigMode);
        if (vigMode == 2) vigInt = FxSlider("Strength", vigInt, 0f, 1f, "0.00");
        grainMode = Modes("Film grain", grainMode);
        if (grainMode == 2) grainInt = FxSlider("Strength", grainInt, 0f, 1f, "0.00");
        caMode = Modes("Color fringe", caMode);
        if (caMode == 2) caInt = FxSlider("Strength", caInt, 0f, 1f, "0.00");
        bloomMode = Modes("Glow (bloom)", bloomMode);
        if (bloomMode == 2) bloomInt = FxSlider("Strength", bloomInt, 0f, 20f, "0.0");
        GUILayout.Space(4);
        if (GUILayout.Button("All effects back to the game settings", GUILayout.Width(280)))
        { dofMode = mblurMode = vigMode = grainMode = caMode = bloomMode = 0; dirty = true; }
        GUILayout.EndVertical();
    }
}

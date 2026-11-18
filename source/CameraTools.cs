// Camera tab of the editor.
// Camera view: the game's own camera does all its normal work (follow, smoothing, wall checks). The editor only
// moves its final view by an offset just before the camera draws, and puts it back after the camera draws.
// All offsets at zero = exactly the game's camera.
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
    static Vector3 PlayerPos() { try { return ThePlayer.lastPlayerOnePosition; } catch (Exception) { return Vector3.zero; } }
    bool IsDefault() { return back == 0f && up == 0f && side == 0f && tilt == 0f && turn == 0f && fovAdd == 0f; }
    bool ShouldApply()
    {
        if (IsDefault()) return false;
        if (skateOnly) { try { if (!ThePlayer.SKATEMODE) return false; } catch (Exception) { return false; } }
        return true;
    }
    void OnPreCull(Camera c)
    {   // runs just before the camera draws, after all the game's camera code
        if (c == null || c != cam || applied) return;
        if (!ShouldApply()) return;
        try
        {
            Transform t = c.transform;
            savedPos = t.position; savedRot = t.rotation; savedFov = c.fieldOfView; applied = true;
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
            {   // a wall between the game camera and the new place: stop in front of the wall
                Vector3 dir = np - savedPos; float len = dir.magnitude;
                RaycastHit hit;
                if (len > 0.01f && Physics.Raycast(savedPos, dir / len, out hit, len + 0.2f, ~0, QueryTriggerInteraction.Ignore))
                    np = savedPos + dir / len * Mathf.Max(0f, hit.distance - 0.2f);
            }
            Vector3 look = target - np;
            Quaternion r = look.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(look, savedRot * Vector3.up) : savedRot;
            r = Quaternion.AngleAxis(turn, Vector3.up) * r * Quaternion.Euler(tilt, 0f, 0f);
            t.SetPositionAndRotation(np, r);
            if (fovAdd != 0f) c.fieldOfView = Mathf.Clamp(savedFov + fovAdd, 5f, 150f);
        }
        catch (Exception e) { status = "Camera error: " + e.Message; }
    }
    void OnPostRender(Camera c)
    {   // put the game camera back, so the game's own camera code finds its own values in the next frame
        if (c == null || c != cam || !applied) return;
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
        if (dofAuto && Cam() != null) d = Mathf.Max(0.1f, Vector3.Distance(cam.transform.position, PlayerPos() + Vector3.up * 0.9f) + back);
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
        GUILayout.Label("CAMERA VIEW     The game camera, moved by the values below. All at 0 = the game camera.");
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
        bool w = GUILayout.Toggle(walls, " Do not move the camera into walls");
        if (w != walls) { walls = w; dirty = true; }
        bool so = GUILayout.Toggle(skateOnly, " Only while skating (walking and scenes use the game camera)");
        if (so != skateOnly) { skateOnly = so; dirty = true; }

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
        GUILayout.EndHorizontal();
    }
}

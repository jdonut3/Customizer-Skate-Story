// Green screen and depth map, for video editing.
//  Green screen: the camera draws the whole level as usual, so that the glass body of the skater and the board show the
//  level through them and in their reflections, as in the game. Then, after the clear (see-through) objects, the mod
//  paints the key color over every pixel that is not the skater or the board: it clears the depth, draws the skater and
//  the board into the depth only, and draws one flat color plane far away, which shows only where there is no skater.
//  The game's color look (post-processing) and the fog are off by default, so that the key color stays clean.
//  (With the depth map also on, the old way is used: the camera draws only the skater's layers.)
//  Depth map: the post-processing debug view of the game shows the depth of each pixel as gray. The range is the far
//  distance of the camera. "Near is white" turns the gray around, with one full-screen draw over the frame.
//  Both work in normal play and in the replay. Both on: a depth map of the skater only.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

public class LookTools
{
    public static LookTools inst;
    public bool dirty;
    public string status = "";

    // settings
    public bool greenOn, depthOn;
    public int keyColor;                        // 0 green, 1 blue, 2 magenta, 3 black, 4 white
    public bool keepLook;                       // green screen: keep the game's color look and fog
    public float depthRange = 60f;              // m
    public bool nearWhite = true;
    static readonly Color[] keys = { new Color(0f, 1f, 0f), new Color(0f, 0f, 1f), new Color(1f, 0f, 1f), Color.black, Color.white };
    static readonly string[] keyNames = { "Green", "Blue", "Magenta", "Black", "White" };

    public LookTools() { inst = this; }

    // ---------- settings file ----------
    static string F(float v) { return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }
    static float P(string s, float d) { float v; return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : d; }
    public void Load(string line)
    {
        line = line.Trim();
        if (!line.StartsWith("look=")) return;
        string[] f = line.Substring(5).Split(',');
        if (f.Length < 6) return;
        // the screen modes always start off: only the settings are kept
        keyColor = Mathf.Clamp((int)P(f[2], 0f), 0, keys.Length - 1);
        keepLook = f[3] == "1"; depthRange = Mathf.Clamp(P(f[4], 60f), 2f, 1000f); nearWhite = f[5] == "1";
    }
    public void Write(StringBuilder sb)
    {
        sb.Append("look=0,0,").Append(keyColor).Append(',').Append(keepLook ? "1" : "0").Append(',').Append(F(depthRange)).Append(',').Append(nearWhite ? "1" : "0").Append('\n');
    }

    // ---------- each frame ----------
    bool hooked;
    Camera cam; PostProcessLayer layer; PostProcessDebug dbg;
    bool layerOff; bool fogOff, oldFog;
    List<Renderer> hidden = new List<Renderer>();
    int playerMask, seenLoads = -1; float nextFind;
    HashSet<Transform> roots = new HashSet<Transform>();

    void Hook()
    {
        if (hooked) return; hooked = true;
        Camera.onPreCull += OnPreCull;
        Camera.onPostRender += OnPostRender;
    }

    public void LateUpdate()
    {
        try
        {
            Hook();
            Camera c = Camera.main;
            if (c != cam) { Undo(); cam = c; layer = c != null ? c.GetComponent<PostProcessLayer>() : null; dbg = null; }
            if (cam == null) return;
            float now = Time.unscaledTime;
            // the skater's layers and the other objects on them: after a level load, and now and then
            if (depthOn != lastDepth) { lastDepth = depthOn; nextFind = 0f; }
            if (greenOn && (SceneLoads.count != seenLoads || now >= nextFind)) { seenLoads = SceneLoads.count; nextFind = now + 5f; FindPlayer(); }
            if ((!greenOn || !depthOn) && hidden.Count > 0) ShowOthers();
            bool wantKey = greenOn && !depthOn;
            if (!wantKey && cbCam != null) DropKey();

            // the game's color look: off for a clean green screen. The depth map needs it on.
            bool wantLayerOff = greenOn && !keepLook && !depthOn;
            if (layer != null && wantLayerOff != layerOff)
            {
                if (wantLayerOff) { if (layer.enabled) { layer.enabled = false; layerOff = true; } }
                else { layer.enabled = true; layerOff = false; }
            }
            bool wantFogOff = greenOn && !keepLook;
            if (wantFogOff != fogOff)
            {
                if (wantFogOff) { oldFog = RenderSettings.fog; RenderSettings.fog = false; fogOff = true; }
                else { RenderSettings.fog = oldFog; fogOff = false; }
            }

            // the depth view of the game's post-processing
            if (depthOn)
            {
                if (layer == null) { status = "This camera has no post-processing, so the depth map cannot show."; }
                else
                {
                    status = "";
                    if (dbg == null) { dbg = cam.GetComponent<PostProcessDebug>(); if (dbg == null) dbg = cam.gameObject.AddComponent<PostProcessDebug>(); }
                    dbg.postProcessLayer = layer; dbg.debugOverlay = DebugOverlay.Depth;
                    dbg.lightMeter = dbg.histogram = dbg.waveform = dbg.vectorscope = false;
                    if (!dbg.enabled) dbg.enabled = true;
                    if (layer.debugLayer != null && layer.debugLayer.overlaySettings != null) layer.debugLayer.overlaySettings.linearDepth = true;
                }
            }
            else if (dbg != null && dbg.enabled)
            {
                dbg.enabled = false;
                if (layer != null && layer.debugLayer != null) layer.debugLayer.RequestDebugOverlay(DebugOverlay.None);
            }
        }
        catch (Exception e) { status = "Green screen / depth error: " + e.Message; }
    }

    // turn all back to the game's own state (camera change, or both modes off)
    void Undo()
    {
        if (layerOff && layer != null) layer.enabled = true;
        layerOff = false;
        if (fogOff) { RenderSettings.fog = oldFog; fogOff = false; }
        if (dbg != null) { dbg.enabled = false; if (layer != null && layer.debugLayer != null) layer.debugLayer.RequestDebugOverlay(DebugOverlay.None); }
        ShowOthers();
        DropKey();
    }
    bool lastDepth;

    void FindPlayer()
    {
        roots.Clear();
        try { roots.Add(ThePlayer.instance.transform.root); } catch (Exception) { }
        try { roots.Add(ThePlayer.SkatePlayer.transform.root); } catch (Exception) { }
        int mask = 0;
        foreach (Transform r in roots)
            if (r != null) foreach (Renderer rd in r.GetComponentsInChildren<Renderer>(true)) mask |= 1 << rd.gameObject.layer;
        if (mask == 0) return;
        playerMask = mask;
        // the shapes of the skater and the board for the key: solid models only, no shadow shapes, no particles or trails
        matte.Clear();
        foreach (Transform r in roots)
            if (r != null)
                foreach (Renderer rd in r.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(rd is MeshRenderer) && !(rd is SkinnedMeshRenderer)) continue;
                    if (rd.name.ToLower().Contains("shadow")) continue;
                    matte.Add(rd);
                }
        // other objects on the same layers: not drawn while the green screen and the depth map are both on
        ShowOthers();
        if (!depthOn) return;
        foreach (Renderer rd in UnityEngine.Object.FindObjectsOfType<Renderer>())
        {
            if (rd == null || (mask & (1 << rd.gameObject.layer)) == 0) continue;
            if (roots.Contains(rd.transform.root)) continue;
            rd.forceRenderingOff = true; hidden.Add(rd);
        }
    }
    void ShowOthers()
    {
        foreach (Renderer rd in hidden) if (rd != null) rd.forceRenderingOff = false;
        hidden.Clear();
    }

    // the camera values for this frame only
    bool camSet; int keepMask; CameraClearFlags keepClear; Color keepBg; float keepFar;
    void OnPreCull(Camera c)
    {
        if (c == null || c != Camera.main || camSet || (!greenOn && !depthOn)) return;
        keepMask = c.cullingMask; keepClear = c.clearFlags; keepBg = c.backgroundColor; keepFar = c.farClipPlane;
        if (greenOn && !depthOn) { Key(c); }
        else if (greenOn && playerMask != 0)
        {
            c.cullingMask = playerMask; c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = keys[keyColor];
        }
        if (depthOn) c.farClipPlane = Mathf.Max(c.nearClipPlane + 1f, depthRange);
        camSet = true;
    }
    void OnPostRender(Camera c)
    {
        if (c == null || c != Camera.main || !camSet) return;
        c.cullingMask = keepMask; c.clearFlags = keepClear; c.backgroundColor = keepBg; c.farClipPlane = keepFar;
        camSet = false;
    }

    // ---------- the key: the color around the skater and the board ----------
    List<Renderer> matte = new List<Renderer>();
    UnityEngine.Rendering.CommandBuffer cb; Camera cbCam;
    Material depthMat, keyMat; Mesh plane;
    const UnityEngine.Rendering.CameraEvent KeyEvent = UnityEngine.Rendering.CameraEvent.AfterForwardAlpha;
    static Material Mat(Shader sh, UnityEngine.Rendering.BlendMode s, UnityEngine.Rendering.BlendMode d, bool zw)
    {
        Material m = new Material(sh); m.hideFlags = HideFlags.HideAndDontSave;
        m.SetInt("_SrcBlend", (int)s); m.SetInt("_DstBlend", (int)d);
        m.SetInt("_ZWrite", zw ? 1 : 0); m.SetInt("_Cull", 0);
        m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        return m;
    }
    void Key(Camera c)
    {
        if (depthMat == null)
        {
            Shader sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null) { status = "The green screen shader is missing."; return; }
            depthMat = Mat(sh, UnityEngine.Rendering.BlendMode.Zero, UnityEngine.Rendering.BlendMode.One, true);   // depth only: the color stays
            keyMat = Mat(sh, UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode.Zero, false);
            plane = new Mesh();
            plane.vertices = new Vector3[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f) };
            plane.colors = new Color[] { Color.white, Color.white, Color.white, Color.white };
            plane.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            plane.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        }
        if (cb == null) { cb = new UnityEngine.Rendering.CommandBuffer(); cb.name = "ShoeMod green screen"; }
        if (cbCam != c) { DropKey(); c.AddCommandBuffer(KeyEvent, cb); cbCam = c; }
        keyMat.SetColor("_Color", keys[keyColor]);
        cb.Clear();
        cb.ClearRenderTarget(true, false, Color.black);
        foreach (Renderer rd in matte)
        {
            if (rd == null || !rd.enabled || !rd.gameObject.activeInHierarchy) continue;
            if (ShoeModBehaviour.ScarfHidden && rd.name.StartsWith("Scarf")) continue;
            int subs = Mathf.Max(1, rd.sharedMaterials.Length);
            for (int k = 0; k < subs; k++) cb.DrawRenderer(rd, depthMat, k, 0);
        }
        // the color plane: near the far end of the view, as big as the view
        float d = c.farClipPlane * 0.97f;
        float h = d * Mathf.Tan(c.fieldOfView * 0.5f * 0.01745329f) * 1.3f, w = h * c.aspect;
        Transform t = c.transform;
        cb.DrawMesh(plane, Matrix4x4.TRS(t.position + t.forward * d, t.rotation, new Vector3(w, h, 1f)), keyMat, 0, 0);
    }
    void DropKey()
    {
        if (cbCam != null && cb != null) { try { cbCam.RemoveCommandBuffer(KeyEvent, cb); } catch (Exception) { } }
        cbCam = null;
        if (cb != null) cb.Clear();
    }

    // ---------- near is white: turn the gray around, over the whole frame (called first in OnGUI) ----------
    Material inv;
    public void DrawOver()
    {
        if (!depthOn || !nearWhite || layer == null || Event.current.type != EventType.Repaint) return;
        if (inv == null)
        {
            Shader sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null) return;
            inv = new Material(sh);
            inv.hideFlags = HideFlags.HideAndDontSave;
            inv.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusDstColor);
            inv.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            inv.SetInt("_Cull", 0); inv.SetInt("_ZWrite", 0);
            inv.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }
        GL.PushMatrix();
        GL.LoadOrtho();
        inv.SetPass(0);
        GL.Begin(7);                    // quads
        GL.Color(Color.white);
        GL.Vertex3(0f, 0f, 0f); GL.Vertex3(0f, 1f, 0f); GL.Vertex3(1f, 1f, 0f); GL.Vertex3(1f, 0f, 0f);
        GL.End();
        GL.PopMatrix();
    }

    // ---------- menus ----------
    void SetGreen(bool v) { greenOn = v; if (v) { nextFind = 0f; } }
    // the Screen tab
    public void Draw()
    {
        GUILayout.Space(10);
        GUILayout.Label("GREEN SCREEN AND DEPTH MAP     For video editing. These also are in the replay (Looks button). They are off when the game starts.");
        if (status.Length > 0) GUILayout.Label(status);
        GUILayout.BeginHorizontal();
        bool g = GUILayout.Toggle(greenOn, " Green screen: only the skater and the board, on one color", GUILayout.Width(420));
        if (g != greenOn) SetGreen(g);
        for (int k = 0; k < keys.Length; k++)
            if (GUILayout.Toggle(keyColor == k, keyNames[k], GUI.skin.button, GUILayout.Width(72)) && keyColor != k) { keyColor = k; dirty = true; }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Space(20);
        bool kl = GUILayout.Toggle(keepLook, " Keep the game's color look and fog on the skater (the key color can change a little)");
        if (kl != keepLook) { keepLook = kl; dirty = true; }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        depthOn = GUILayout.Toggle(depthOn, " Depth map: distance as gray", GUILayout.Width(220));
        bool nw = GUILayout.Toggle(nearWhite, " Near is white", GUILayout.Width(120));
        if (nw != nearWhite) { nearWhite = nw; dirty = true; }
        GUILayout.Label("Range", GUILayout.Width(44));
        Rect sr = GUILayoutUtility.GetRect(260f, 20f, GUI.skin.label, GUILayout.Width(260)); sr.y += 5;
        float r = GUI.HorizontalSlider(sr, Mathf.Log(depthRange), Mathf.Log(2f), Mathf.Log(1000f));
        float nr = Mathf.Round(Mathf.Exp(r));
        if (Mathf.Abs(nr - depthRange) >= 1f) { depthRange = nr; dirty = true; }
        GUILayout.Label(depthRange.ToString("0") + " m (objects farther away are not drawn)");
        GUILayout.EndHorizontal();
    }

    // the small panel in the replay screen (no layout: fixed rects)
    public void DrawReplay(Rect a)
    {
        float x = a.x + 10, y = a.y + 8;
        GUI.Label(new Rect(x, y, 300, 22), "LOOKS"); y += 24;
        bool g = GUI.Toggle(new Rect(x, y, 140, 24), greenOn, new GUIContent(" Green screen"), GUI.skin.toggle);
        if (g != greenOn) SetGreen(g);
        float bx = x + 140;
        for (int k = 0; k < keys.Length; k++)
        {
            if (GUI.Toggle(new Rect(bx, y, 64, 24), keyColor == k, new GUIContent(keyNames[k]), GUI.skin.button) && keyColor != k) { keyColor = k; dirty = true; }
            bx += 66;
        }
        y += 28;
        bool kl = GUI.Toggle(new Rect(x + 20, y, 300, 24), keepLook, new GUIContent(" Keep the color look and fog"), GUI.skin.toggle);
        if (kl != keepLook) { keepLook = kl; dirty = true; }
        y += 32;
        depthOn = GUI.Toggle(new Rect(x, y, 140, 24), depthOn, new GUIContent(" Depth map"), GUI.skin.toggle);
        bool nw = GUI.Toggle(new Rect(x + 140, y, 130, 24), nearWhite, new GUIContent(" Near is white"), GUI.skin.toggle);
        if (nw != nearWhite) { nearWhite = nw; dirty = true; }
        y += 28;
        GUI.Label(new Rect(x, y, 50, 22), "Range");
        float r = GUI.HorizontalSlider(new Rect(x + 50, y + 6, 240, 16), Mathf.Log(depthRange), Mathf.Log(2f), Mathf.Log(1000f));
        float nr = Mathf.Round(Mathf.Exp(r));
        if (Mathf.Abs(nr - depthRange) >= 1f) { depthRange = nr; dirty = true; }
        GUI.Label(new Rect(x + 300, y, 120, 22), depthRange.ToString("0") + " m");
        if (status.Length > 0) GUI.Label(new Rect(x, y + 24, a.width - 20, 22), status);
    }
}

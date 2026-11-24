// Replay lens: zoom, depth of field (focus, aperture, focal length, blur size) and vignette, for the replay only.
//  The values go into a post-processing volume of the mod that is on only during a replay, with a higher priority than
//  the game's volumes and the Camera tab. Out of Path mode, the lens panel sets one set of values.
//  In Path mode, each path point keeps its own lens values, and the camera glides between them with the path.
using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

public partial class ReplayTools
{
    public class Lens
    {
        public float zoom = 1f, focus = 5f, aperture = 5.6f, focal = 50f, vignette = 0f;
        public Lens Copy() { return (Lens)MemberwiseClone(); }
        public static Lens Blend(Lens a, Lens b, float u)
        {
            return new Lens { zoom = Mathf.Lerp(a.zoom, b.zoom, u), focus = Mathf.Exp(Mathf.Lerp(Mathf.Log(a.focus), Mathf.Log(b.focus), u)),
                aperture = Mathf.Exp(Mathf.Lerp(Mathf.Log(a.aperture), Mathf.Log(b.aperture), u)), focal = Mathf.Lerp(a.focal, b.focal, u),
                vignette = Mathf.Lerp(a.vignette, b.vignette, u) };
        }
    }
    Lens baseLens = new Lens();
    bool dofOn, autoFocus = true; int blurSize = 1;
    bool lensOpen;
    static readonly string[] blurNames = { "Small", "Medium", "Large", "Very large" };

    // the lens at a time: from the path points in Path mode, else the base values
    Lens LensAt(float t)
    {
        if (camMode != 3 || path.Count == 0) return baseLens;
        int n = path.Count;
        if (n == 1 || t <= path[0].t) return path[0].lens ?? baseLens;
        if (t >= path[n - 1].t) return path[n - 1].lens ?? baseLens;
        int k = 0; while (k < n - 2 && t >= path[k + 1].t) k++;
        float u = (t - path[k].t) / Mathf.Max(0.0001f, path[k + 1].t - path[k].t);
        u = u * u * (3f - 2f * u);              // values ease in and out between two points
        return Lens.Blend(path[k].lens ?? baseLens, path[k + 1].lens ?? baseLens, u);
    }
    // zoom on a field of view: 2x shows half as wide
    static float Zoomed(float fov, float zoom)
    {
        float t = Mathf.Tan(fov * 0.5f * 0.01745329f) / Mathf.Max(0.1f, zoom);
        return Mathf.Clamp(Mathf.Atan(t) * 2f * 57.29578f, 2f, 150f);
    }

    // ---------- the volume ----------
    GameObject lensGo; PostProcessVolume lensVol; PostProcessProfile lensProf; DepthOfField lensDof; Vignette lensVig;
    bool EnsureLens()
    {
        if (lensVol != null) return true;
        Camera c = Camera.main; if (c == null) return false;
        PostProcessLayer layer = c.GetComponent<PostProcessLayer>(); if (layer == null) return false;
        int mask = layer.volumeLayer.value, li = -1;
        for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) { li = i; break; }
        if (li < 0) return false;
        lensProf = ScriptableObject.CreateInstance<PostProcessProfile>();
        lensDof = lensProf.AddSettings<DepthOfField>(); lensVig = lensProf.AddSettings<Vignette>();
        lensGo = new GameObject("ShoeModReplayLens"); UnityEngine.Object.DontDestroyOnLoad(lensGo); lensGo.layer = li;
        lensVol = lensGo.AddComponent<PostProcessVolume>();
        lensVol.isGlobal = true; lensVol.priority = 20000f; lensVol.weight = 1f; lensVol.sharedProfile = lensProf;
        lensVol.enabled = false;
        return true;
    }
    static void On(PostProcessEffectSettings s, bool on) { s.active = true; s.enabled.overrideState = true; s.enabled.value = on; }
    // each frame of a replay, in the late update (before the post-processing reads the volumes)
    void ApplyLens()
    {
        if (!active || src == null) { if (lensVol != null && lensVol.enabled) lensVol.enabled = false; return; }
        if (!EnsureLens()) return;
        Lens l = LensAt(playT);
        float focus = l.focus;
        if (autoFocus)
        {
            Vector3 p; Quaternion q; float f; View(camMode, out p, out q, out f);
            focus = Mathf.Max(0.1f, Vector3.Distance(p, skaterCenter));
        }
        lensFocusNow = focus;
        On(lensDof, dofOn);
        lensDof.focusDistance.overrideState = dofOn; lensDof.focusDistance.value = focus;
        lensDof.aperture.overrideState = dofOn; lensDof.aperture.value = l.aperture;
        lensDof.focalLength.overrideState = dofOn; lensDof.focalLength.value = l.focal;
        lensDof.kernelSize.overrideState = dofOn; lensDof.kernelSize.value = (KernelSize)Mathf.Clamp(blurSize, 0, 3);
        bool vigOn = l.vignette > 0.001f;
        On(lensVig, vigOn);
        lensVig.intensity.overrideState = vigOn; lensVig.intensity.value = l.vignette;
        if (!lensVol.enabled) lensVol.enabled = true;
    }
    float lensFocusNow;
    void LensOff() { if (lensVol != null) lensVol.enabled = false; }

    // ---------- clips: the lens after the path ----------
    void WriteLens(BinaryWriter w)
    {
        w.Write("LENS"); w.Write(dofOn); w.Write(autoFocus); w.Write(blurSize);
        WL(w, baseLens);
        foreach (PathPt p in path) WL(w, p.lens ?? baseLens);
    }
    static void WL(BinaryWriter w, Lens l) { w.Write(l.zoom); w.Write(l.focus); w.Write(l.aperture); w.Write(l.focal); w.Write(l.vignette); }
    static Lens RL(BinaryReader r) { return new Lens { zoom = r.ReadSingle(), focus = r.ReadSingle(), aperture = r.ReadSingle(), focal = r.ReadSingle(), vignette = r.ReadSingle() }; }
    void ReadLens(BinaryReader r)
    {
        if (r.BaseStream.Position >= r.BaseStream.Length || r.ReadString() != "LENS") return;
        dofOn = r.ReadBoolean(); autoFocus = r.ReadBoolean(); blurSize = r.ReadInt32();
        baseLens = RL(r);
        foreach (PathPt p in path) p.lens = RL(r);
    }

    // ---------- the lens panel ----------
    float LogSlider(Rect r, float v, float lo, float hi) { return Mathf.Exp(GUI.HorizontalSlider(r, Mathf.Log(Mathf.Clamp(v, lo, hi)), Mathf.Log(lo), Mathf.Log(hi))); }
    void DrawLensPanel(Rect a)
    {
        Fill(a, new Color(0f, 0f, 0f, 0.8f));
        bool forPoint = camMode == 3 && pathSel >= 0 && pathSel < path.Count;
        Lens l = forPoint ? (path[pathSel].lens ?? (path[pathSel].lens = baseLens.Copy())) : baseLens;
        float x = a.x + 10, y = a.y + 8, sx = x + 110, sw = 250, vx = sx + sw + 10;
        GUI.Label(new Rect(x, y, a.width - 20, 22), "LENS (replay only)   " + (forPoint ? "Editing point " + (pathSel + 1) + ". The values glide from point to point."
            : camMode == 3 && path.Count > 0 ? "Path mode: select a point to set its lens." : "These values are for Recorded, Free and Orbit."));
        if (GUI.Button(new Rect(a.xMax - 34, y, 26, 22), "x")) lensOpen = false;
        y += 28;
        if (!forPoint)
        {
            GUI.Label(new Rect(x, y, 100, 22), "Zoom");
            l.zoom = GUI.HorizontalSlider(new Rect(sx, y + 6, sw, 16), l.zoom, 0.5f, 4f);
            GUI.Label(new Rect(vx, y, 120, 22), l.zoom.ToString("0.00") + "x");
            if (GUI.Button(new Rect(vx + 70, y, 50, 22), "1x")) l.zoom = 1f;
            y += 26;
        }
        else
        {
            GUI.Label(new Rect(x, y, a.width - 20, 22), "Zoom for this point: in the point panel."); y += 26;
        }
        dofOn = GUI.Toggle(new Rect(x, y, 140, 22), dofOn, new GUIContent(" Depth of field"), GUI.skin.toggle);
        autoFocus = GUI.Toggle(new Rect(x + 150, y, 200, 22), autoFocus, new GUIContent(" Focus on the skater"), GUI.skin.toggle);
        y += 26;
        GUI.enabled = dofOn && !autoFocus;
        GUI.Label(new Rect(x, y, 100, 22), "Focus");
        l.focus = LogSlider(new Rect(sx, y + 6, sw, 16), l.focus, 0.3f, 100f);
        GUI.Label(new Rect(vx, y, 160, 22), (autoFocus ? lensFocusNow : l.focus).ToString("0.0") + " m" + (autoFocus ? " (auto)" : ""));
        y += 26;
        GUI.enabled = dofOn;
        GUI.Label(new Rect(x, y, 100, 22), "Aperture");
        l.aperture = LogSlider(new Rect(sx, y + 6, sw, 16), l.aperture, 1f, 22f);
        GUI.Label(new Rect(vx, y, 160, 22), "f/" + l.aperture.ToString("0.0") + "  (low = more blur)");
        y += 26;
        GUI.Label(new Rect(x, y, 100, 22), "Focal length");
        l.focal = GUI.HorizontalSlider(new Rect(sx, y + 6, sw, 16), l.focal, 10f, 300f);
        GUI.Label(new Rect(vx, y, 160, 22), l.focal.ToString("0") + " mm  (high = more blur)");
        y += 26;
        GUI.Label(new Rect(x, y, 100, 22), "Blur size");
        for (int k = 0; k < 4; k++) if (GUI.Toggle(new Rect(sx + k * 76, y, 72, 22), blurSize == k, new GUIContent(blurNames[k]), GUI.skin.button)) blurSize = k;
        y += 28;
        GUI.enabled = true;
        GUI.Label(new Rect(x, y, 100, 22), "Vignette");
        l.vignette = GUI.HorizontalSlider(new Rect(sx, y + 6, sw, 16), l.vignette, 0f, 1f);
        GUI.Label(new Rect(vx, y, 120, 22), l.vignette < 0.005f ? "off" : l.vignette.ToString("0.00"));
    }
}

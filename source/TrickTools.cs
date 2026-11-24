// Two ground tricks, drawn only (the game's physics, grind and speed do not change):
//  Darkslide: in a grind, press the darkslide buttons: the board rolls over onto its grip tape, and the skater stands
//  on the wheels (the body goes up by the wheel height). It rolls back when the grind ends, or with the buttons again.
//  Hippy jump: hold the hippy jump button and ollie. The skater does the game's own ollie (the same jump and the same
//  body animation), but the board stays on the ground: the drawn board keeps rolling under the skater on the ground,
//  flat, with the turn it had at take-off, and the skater lands back on it.
//  Both are drawn just before the frame (like the flips), so the camera does not see them, and they go back after it.
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class MoveTools
{
    public bool darkOn = true, hippyOn = true;
    KeyCode[] darkKeys = { KeyCode.JoystickButton2, KeyCode.JoystickButton3 };     // X + Y
    KeyCode[] hippyKeys = { KeyCode.JoystickButton1, KeyCode.None };                // B (one button: hold it and ollie)
    float wheelH = 0.11f;               // how far the body goes up on the wheels (m)
    float hippyH = 0.7f, hippyDur = 0.75f, hippyKnees = 0.3f;
    bool darkActive, darkHeld, hippyHeld; float darkW;
    float hippyT0 = -10f; public float hippyW;
    string trickInfo = "";

    // ---------- each frame (from FrameSpin) ----------
    void GroundTricks()
    {
        if (sp == null || ReplayTools.Active) return;
        bool grind = false; try { grind = sp.skimGrindState != null && sp.skimGrindState.inGrind; } catch (Exception) { }
        bool air = false; try { air = ThePlayer.IS_AIRBORNE; } catch (Exception) { }
        // darkslide: a press in a grind turns it on (or off); the end of the grind turns it off
        bool dk = binding == 0 && Held(darkKeys);
        if (darkOn && dk && !darkHeld && grind) { darkActive = !darkActive; trickInfo = darkActive ? "Darkslide" : ""; }
        darkHeld = dk;
        if (!grind || !darkOn) darkActive = false;
        darkW = Mathf.MoveTowards(darkW, darkActive ? 1f : 0f, Time.deltaTime / 0.22f);
        // hippy jump: the button held at the take-off of an ollie (from the ground, not from a grind)
        bool hk = binding == 0 && hippyKeys[0] != KeyCode.None && Input.GetKey(hippyKeys[0]);
        if (air && !tkWasAir && hippyOn && hk && !tkWasGrind) HippyStart();
        if (air) tkGndFrames = 0; else tkGndFrames++;
        if (hippyActive && tkGndFrames >= 2) { hippyActive = false; trickInfo = ""; }       // landed
        hippyBlend = Mathf.MoveTowards(hippyBlend, hippyActive ? 1f : 0f, Time.deltaTime / (hippyActive ? 0.06f : 0.12f));
        tkWasAir = air; tkWasGrind = grind;
    }
    bool tkWasAir, tkWasGrind, hippyActive; int tkGndFrames; float hippyBlend;
    Vector3 hipOff; float hipH, hipGroundY; Quaternion hipRot;
    List<Vector3> hipRelP = new List<Vector3>(); List<Quaternion> hipRelR = new List<Quaternion>();
    RaycastHit[] hipHits = new RaycastHit[16];
    // the ground under a point (not the skater)
    bool Ground(Vector3 p, out RaycastHit hit)
    {
        hit = new RaycastHit();
        int n = UnityEngine.Physics.RaycastNonAlloc(p + Vector3.up * 1.5f, Vector3.down, hipHits, 12f, ~0, QueryTriggerInteraction.Ignore);
        Transform me = ThePlayer.instance != null ? ThePlayer.instance.transform.root : null, me2 = sp != null ? sp.transform.root : null;
        float best = 1e30f; bool any = false;
        for (int i = 0; i < n; i++)
        {
            Collider c = hipHits[i].collider;
            if (c == null || (me != null && c.transform.IsChildOf(me)) || (me2 != null && c.transform.IsChildOf(me2))) continue;
            if (hipHits[i].distance < best) { best = hipHits[i].distance; hit = hipHits[i]; any = true; }
        }
        return any;
    }
    void HippyStart()
    {
        List<Transform> bl = BoardParts(); if (bl.Count == 0) return;
        Transform main = bl[bl.Count - 1];
        RaycastHit h; if (!Ground(main.position, out h)) return;
        hipOff = main.position - sp.transform.position; hipOff.y = 0f;
        hipGroundY = h.point.y; hipH = Mathf.Max(0f, main.position.y - h.point.y);
        hipRot = Quaternion.FromToRotation(main.up, h.normal) * main.rotation;
        hipRelP.Clear(); hipRelR.Clear();
        Quaternion inv = Quaternion.Inverse(main.rotation);
        foreach (Transform b in bl) { hipRelP.Add(inv * (b.position - main.position)); hipRelR.Add(inv * b.rotation); }
        hippyActive = true; trickInfo = "Hippy jump";
    }
    float HippyArc(float u) { return 4f * u * (1f - u); }            // 0 at the start and the end, 1 at the top

    // ---------- just before the frame is drawn ----------
    List<Transform> tkT = new List<Transform>(); List<Vector3> tkP = new List<Vector3>(); List<Quaternion> tkR = new List<Quaternion>();
    void SaveT(Transform t) { if (t == null || tkT.Contains(t)) return; tkT.Add(t); tkP.Add(t.position); tkR.Add(t.rotation); }
    void RestoreTricks()
    {
        for (int i = tkT.Count - 1; i >= 0; i--) if (tkT[i] != null) tkT[i].SetPositionAndRotation(tkP[i], tkR[i]);
        tkT.Clear(); tkP.Clear(); tkR.Clear();
    }
    void DrawTricks()
    {
        if (sp == null || sp.board == null) return;
        ThePlayer p = ThePlayer.instance;
        if (p == null || p.finalSkaterMesh == null || p.finalSkaterMesh.skaterMeshRenderer == null) return;
        Transform root = p.finalSkaterMesh.skaterMeshRenderer.rootBone; if (root == null) return;
        if (poseFm != p.finalSkaterMesh) poseFm = p.finalSkaterMesh;
        List<Transform> bl = BoardParts();
        Transform board = bl.Count > 0 ? bl[bl.Count - 1] : sp.board.transform;
        if (darkW > 0.001f)
        {   // the board rolls over around its own long line; the body goes up onto the wheels
            float e = darkW * darkW * (3f - 2f * darkW);
            Vector3 up = board.up, axis = board.forward, pivot = board.position;
            Quaternion q = Quaternion.AngleAxis(180f * e, axis);
            foreach (Transform b in bl) { SaveT(b); b.SetPositionAndRotation(pivot + q * (b.position - pivot), q * b.rotation); }
            SaveT(root); root.position += up * (wheelH * e);
        }
        if (hippyBlend > 0.001f && hipRelP.Count == bl.Count)
        {   // the board on the ground under the skater: it moves along with the skater, flat on the ground under it
            Vector3 at = sp.transform.position + hipOff;
            RaycastHit h;
            Quaternion rot = hipRot;
            if (Ground(at, out h)) { hipGroundY = h.point.y; rot = Quaternion.FromToRotation(hipRot * Vector3.up, h.normal) * hipRot; }
            Vector3 mp = new Vector3(at.x, hipGroundY + hipH, at.z);
            float e = hippyBlend;
            for (int i = 0; i < bl.Count; i++)
            {
                Transform b = bl[i]; SaveT(b);
                b.SetPositionAndRotation(Vector3.Lerp(b.position, mp + rot * hipRelP[i], e), Quaternion.Slerp(b.rotation, rot * hipRelR[i], e));
            }
        }
    }

    // ---------- the knees in a hippy jump (in the late update, with the pose step) ----------
    void HippyLegs()
    {
        if (hippyW <= 0f) return;
        float lift = hippyKnees * Mathf.Sin(Mathf.Clamp01(hippyW) * 3.14159265f);
        if (lift < 0.005f || sp == null || sp.board == null) return;
        Vector3 up = sp.board.transform.up, knee = sp.board.transform.forward;
        for (int s = 0; s < 2; s++)
        {
            List<Transform> c = legs[s];
            if (c == null || c.Count < 3) continue;
            Transform foot = c[c.Count - 1];
            Quaternion fr = foot.rotation;
            foreach (Transform b in c) Keep(b);
            Reach(c, foot.position + up * lift, knee, 10);
            foot.rotation = fr;
        }
        posed = true;
    }

    // ---------- menu ----------
    void DrawTrickMenu()
    {
        GUILayout.Space(6);
        GUILayout.Label("GROUND TRICKS");
        darkOn = Toggle(darkOn, " Darkslide: in a grind, press the buttons: the board rolls onto its grip tape");
        if (darkOn)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("   Buttons:  " + (binding == 3 ? "hold the two buttons now... (Esc = no change)" : Combo(darkKeys)), GUILayout.Width(360));
            if (GUILayout.Button("Change", GUILayout.Width(70))) { binding = 3; held.Clear(); }
            GUILayout.EndHorizontal();
            wheelH = Slider("   Up on the wheels", wheelH, -0.3f, 0.3f, "0.00", " m");   // below 0: the body goes down
        }
        hippyOn = Toggle(hippyOn, " Hippy jump: hold the button and ollie: you jump, the board stays on the ground");
        if (hippyOn)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("   Button:  " + (binding == 4 ? "press the button now... (Esc = no change)" : KeyName(hippyKeys[0])), GUILayout.Width(360));
            if (GUILayout.Button("Change", GUILayout.Width(70))) { binding = 4; held.Clear(); }
            GUILayout.EndHorizontal();
            GUILayout.Label("   The jump is the game's own ollie (its height: the Jump setting). The board rolls on under you.");
        }
        if (trickInfo.Length > 0 && (darkActive || hippyActive)) GUILayout.Label("   Now: " + trickInfo);
    }
}

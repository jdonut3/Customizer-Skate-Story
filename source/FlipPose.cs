// Flips, part 2: a natural pose and timing.
//  A real flip has three parts: the body leaves the ground open, then tucks (the knees come up to the chest, the back
//  curls, the hands grab the board), then opens again before the landing. A tucked body turns faster than an open one
//  (less spread out, so the same push gives more turn), so the flip is slow at the start, fast in the middle and slow
//  at the end. Here the turn speed at each moment is 1 / (1 - spin * tuck), and the flip angle is the sum of it,
//  scaled so that the full flip is 360 degrees (or 720).
//  The pose: the game writes the final skeleton in the late update of its final skater mesh, and Unity works out the
//  skinned body right after the late updates. So during a flip the mod switches the game's step off and runs it itself,
//  then bends the bones (back, legs, arms) at once after it. The whole body and the board then turn as before, just
//  before the frame is drawn. After the frame, all of it goes back.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public partial class MoveTools
{
    // settings (saved)
    int flipStyle;                     // 0 = over the nose (the board turns end over end), 1 = over the toes or heels (a body flip)
    bool poseOn = true;
    float tuckAmt = 0.8f;              // 0 = no tuck, 1 = a full tuck
    float tuckSpin = 0.5f;             // how much faster the flip turns in the tuck (0 = even)
    float tuckIn = 0.22f, tuckOut = 0.3f;   // part of the flip to go into the tuck, and to open again
    float curlDeg = 30f;               // the back curls forward this much in a full tuck
    float legLift = 0.3f;              // the board comes up to the body this much in a full tuck (m)
    int grabMode = 3;                  // 0 = no grab, 1 = left hand, 2 = right hand, 3 = both hands
    bool grabHeel;                     // grab on the heel side of the board (else the toe side)
    const float DefFeel = 0.8f;
    public int flipTab;                // the flip menu part: 0 = buttons, 1 = timing, 2 = pose

    // ---------- timing ----------
    float[] angleTable = new float[129];
    float TuckShape(float u)
    {
        float a = Mathf.Clamp01(u / Mathf.Max(0.02f, tuckIn)), b = Mathf.Clamp01((1f - u) / Mathf.Max(0.02f, tuckOut));
        return a * a * (3f - 2f * a) * b * b * (3f - 2f * b);
    }
    void BuildAngleTable()
    {
        int n = angleTable.Length - 1; float sum = 0f; angleTable[0] = 0f;
        float k = Mathf.Clamp(tuckSpin * (poseOn ? Mathf.Max(0.2f, tuckAmt) : 1f), 0f, 0.9f);
        for (int i = 1; i <= n; i++)
        {
            float u = (i - 0.5f) / n;
            sum += 1f / (1f - k * TuckShape(u));
            angleTable[i] = sum;
        }
        for (int i = 1; i <= n; i++) angleTable[i] /= sum;
    }
    float Progress(float u)
    {
        u = Mathf.Clamp01(u) * (angleTable.Length - 1);
        int i = Mathf.Min((int)u, angleTable.Length - 2);
        return Mathf.Lerp(angleTable[i], angleTable[i + 1], u - i);
    }

    // ---------- the skeleton parts ----------
    FinalSkaterMesh poseFm; Action fmLate; bool fmOff;
    Transform[] spine = new Transform[0];
    List<Transform>[] legs = new List<Transform>[2], arms = new List<Transform>[2];
    List<Transform> touched = new List<Transform>(); List<Vector3> keepLP = new List<Vector3>(); List<Quaternion> keepLR = new List<Quaternion>();
    bool posed; Vector3 pivotBase; bool havePivot;

    static Transform FindBone(Transform[] bones, string name)
    {
        foreach (Transform b in bones) if (b != null && b.name == name) return b;
        return null;
    }
    // the bones from the top one down to the end one (both in the list)
    static List<Transform> Chain(Transform top, Transform end)
    {
        List<Transform> l = new List<Transform>();
        if (top == null || end == null) return null;
        for (Transform t = end; t != null; t = t.parent) { l.Add(t); if (t == top) { l.Reverse(); return l; } }
        return null;
    }
    bool FindSkeleton(FinalSkaterMesh fm)
    {
        if (fm == poseFm && fmLate != null) return spine.Length > 0 || legs[0] != null;
        poseFm = fm; fmLate = null;
        if (fm == null || fm.skaterMeshRenderer == null) return false;
        MethodInfo mi = typeof(FinalSkaterMesh).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi == null) return false;
        try { fmLate = (Action)Delegate.CreateDelegate(typeof(Action), fm, mi); } catch (Exception) { fmLate = () => mi.Invoke(fm, null); }
        Transform[] bones = fm.skaterMeshRenderer.bones;
        List<Transform> sp0 = new List<Transform>();
        foreach (string n in new[] { "DEF-spine_001", "DEF-spine_002", "DEF-spine_003", "DEF-spine_004" }) { Transform b = FindBone(bones, n); if (b != null) sp0.Add(b); }
        spine = sp0.ToArray();
        string[] side = { "L", "R" };
        for (int s = 0; s < 2; s++)
        {
            legs[s] = Chain(FindBone(bones, "DEF-thigh_" + side[s]), FindBone(bones, "DEF-foot_" + side[s]));
            arms[s] = Chain(FindBone(bones, "DEF-upper_arm_" + side[s]), FindBone(bones, "DEF-hand_" + side[s]));
        }
        poseInfo = "Bones found: back " + spine.Length + ", legs " + (legs[0] != null ? legs[0].Count : 0) + "/" + (legs[1] != null ? legs[1].Count : 0)
                + ", arms " + (arms[0] != null ? arms[0].Count : 0) + "/" + (arms[1] != null ? arms[1].Count : 0);
        return true;
    }
    string poseInfo = "";
    void Keep(Transform t) { if (t == null || touched.Contains(t)) return; touched.Add(t); keepLP.Add(t.localPosition); keepLR.Add(t.localRotation); }
    void PutBack()
    {
        for (int i = touched.Count - 1; i >= 0; i--) if (touched[i] != null) { touched[i].localPosition = keepLP[i]; touched[i].localRotation = keepLR[i]; }
        touched.Clear(); keepLP.Clear(); keepLR.Clear(); posed = false;
    }

    // the front of the skater (the toe side of the board), from where the knees are
    Vector3 Front(Transform board)
    {
        Vector3 up = board.up, f = Vector3.zero;
        Transform hips = poseFm != null && poseFm.skaterMeshRenderer != null ? poseFm.skaterMeshRenderer.rootBone : null;
        for (int s = 0; s < 2; s++) if (legs[s] != null && legs[s].Count > 2 && hips != null) f += legs[s][legs[s].Count / 2].position - hips.position;
        float d = Vector3.Dot(f, board.right);
        return Mathf.Abs(d) > 0.01f ? board.right * Mathf.Sign(d) : board.right;
    }

    // ---------- each frame: called from the mod's late update ----------
    public void LatePose()
    {
        if (ReplayTools.Active) return;                 // a replay sets the poses itself
        PutBack();
        ThePlayer p = ThePlayer.instance;
        FinalSkaterMesh fm = p != null ? p.finalSkaterMesh : null;
        bool flipWant = flipOn && flipping && poseOn;
        bool want = (flipWant || spinPoseW > 0.001f || (hippyW > 0f && hippyKnees > 0.001f)) && sp != null && sp.board != null && fm != null;
        if (want && !fmOff && fm.enabled)
        {
            if (!FindSkeleton(fm) || fmLate == null) return;
            fm.enabled = false; fmOff = true;           // the game's step: run by the mod from now on, until the flip ends
        }
        if (!fmOff) return;
        if (fm == null || fm != poseFm) { fmOff = false; return; }
        try { fmLate(); } catch (Exception e) { status = "Flip pose error: " + e.Message; }
        if (!want) return;                              // the pose is over: the next updates give the step back
        if (hippyW > 0f && !flipWant && spinPoseW <= 0.001f) { HippyLegs(); return; }
        float w;
        Transform board = sp.board.transform;
        if (flipWant)
        {
            float span = Mathf.Max(0.05f, flipEnd - flipStart);
            float u = Mathf.Clamp01((Time.time - flipStart) / span);
            w = TuckShape(u) * tuckAmt;
            pivotBase = board.position + board.up * FlipPivot; havePivot = true;
        }
        else
        {   // an extra spin: the held air pose (smooth in and out), and the tuck on top of it
            float s0 = spinPoseW, e = s0 * s0 * (3f - 2f * s0);
            HoldPose(e);
            w = e * (spinPose && poseOn ? tuckAmt : 0f);
        }
        if (w < 0.001f) return;
        Vector3 up = board.up, front = Front(board), knee = board.forward;
        // 1. the back curls forward, a part at each spine bone
        if (spine.Length > 0 && curlDeg > 0f)
        {
            Vector3 ax = Vector3.Cross(up, front).normalized;
            Quaternion q = Quaternion.AngleAxis(curlDeg * w / spine.Length, ax);
            foreach (Transform b in spine) { Keep(b); b.rotation = q * b.rotation; }
        }
        // 2. the legs bend: both feet come up by the same amount, along the board's up line, and the board comes with
        //    them by exactly that amount. So the board stays under the feet: first each leg reaches as far as it can,
        //    then both legs go to the smaller of the two, and the board moves by it.
        List<Transform> boards = BoardParts();
        Vector3 lift = up * legLift * w;
        Vector3[] f0 = new Vector3[2]; Quaternion[] fr0 = new Quaternion[2]; bool[] ok = new bool[2];
        for (int s = 0; s < 2; s++)
        {
            List<Transform> c = legs[s];
            ok[s] = c != null && c.Count >= 3;
            if (!ok[s]) continue;
            Transform foot = c[c.Count - 1];
            f0[s] = foot.position; fr0[s] = foot.rotation;
            foreach (Transform b in c) Keep(b);
        }
        float got = 1e30f;
        for (int s = 0; s < 2; s++)
            if (ok[s]) { Reach(legs[s], f0[s] + lift, knee, 10); got = Mathf.Min(got, Vector3.Dot(legs[s][legs[s].Count - 1].position - f0[s], up)); }
        if (got != 1e30f)
        {
            got = Mathf.Clamp(got, 0f, legLift * w);
            Vector3 d = up * got;
            for (int s = 0; s < 2; s++)
                if (ok[s])
                {   // the same lift for both feet, from the start pose of this frame
                    List<Transform> c = legs[s];
                    for (int k = 0; k < c.Count; k++) { int idx = touched.IndexOf(c[k]); if (idx >= 0) { c[k].localPosition = keepLP[idx]; c[k].localRotation = keepLR[idx]; } }
                    Reach(c, f0[s] + d, knee, 12);
                    c[c.Count - 1].rotation = fr0[s];       // the foot stays flat on the board
                }
            // the board: moved by the lift, then a last fix by how far the feet are from where they should be
            Vector3 miss = Vector3.zero; int n = 0;
            for (int s = 0; s < 2; s++) if (ok[s]) { miss += legs[s][legs[s].Count - 1].position - (f0[s] + d); n++; }
            Vector3 move = d + (n > 0 ? miss / n : Vector3.zero);
            foreach (Transform b in boards) { Keep(b); b.position += move; }
        }
        // 3. the hands grab the board edge (toe side or heel side), each hand near its own end of the board
        if (grabMode > 0)
        {
            Vector3 edge = (grabHeel ? -front : front) * 0.1f + up * 0.04f;
            for (int s = 0; s < 2; s++)
            {
                if (!(grabMode == 3 || grabMode == s + 1)) continue;
                List<Transform> c = arms[s];
                if (c == null || c.Count < 3) continue;
                Transform hand = c[c.Count - 1];
                float end = Mathf.Sign(Vector3.Dot(hand.position - board.position, board.forward));
                if (grabMode != 3) end = 0f;            // one hand: in the middle of the board
                Transform gb = boards.Count > 0 ? boards[boards.Count - 1] : board;
                Vector3 grab = gb.position + edge + board.forward * (0.16f * end);
                Vector3 target = Vector3.Lerp(hand.position, grab, Mathf.Clamp01(w / Mathf.Max(0.05f, tuckAmt)));
                foreach (Transform b in c) Keep(b);
                for (int it = 0; it < 10; it++)
                    for (int j = c.Count - 2; j >= 0; j--)
                    {
                        Vector3 a = hand.position - c[j].position, b = target - c[j].position;
                        if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) continue;
                        c[j].rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(a, b), 0.6f) * c[j].rotation;
                    }
            }
        }
        posed = true;
    }
    // The feet on the board. On the ground (when the game makes the pose itself), the mod notes where the feet sit on
    // the board, in the board's own space. In the air, while a spin or a flip goes, just before the frame is drawn, the
    // whole drawn body is moved (turned around the board's up line, and shifted) so that the feet are back on those
    // spots. The board is where the game puts it; the body follows it. So the board is always under the feet, also when
    // the extra spin turns the board and the game's body smoothing is a moment behind.
    Vector3 refL, refR; bool haveRef; int missed; float fitW;
    Transform fitRoot; Vector3 fitPos; Quaternion fitRot; bool fitOn;
    Transform DrawnBoard() { List<Transform> bl = BoardParts(); return bl.Count > 0 ? bl[bl.Count - 1] : null; }
    void FeetOnBoard()
    {
        if (sp == null || sp.board == null) return;
        ThePlayer p = ThePlayer.instance;
        FinalSkaterMesh fm = p != null ? p.finalSkaterMesh : null;
        if (fm == null || fm.skaterMeshRenderer == null) return;
        if (poseFm != fm || legs[0] == null) { if (!FindSkeleton(fm)) return; }
        if (legs[0] == null || legs[1] == null || legs[0].Count < 2 || legs[1].Count < 2) return;
        Transform fl = legs[0][legs[0].Count - 1], fr = legs[1][legs[1].Count - 1];
        Transform board = DrawnBoard(); if (board == null) return;
        Transform root = fm.skaterMeshRenderer.rootBone; if (root == null) return;
        bool air = false; try { air = ThePlayer.IS_AIRBORNE; } catch (Exception) { }
        bool busy = flipping || spinPoseW > 0.001f || fmOff || (spinOn && spinAdded != 0f);
        // the fit fades out over a quarter second after the spin or flip (it stopped at once before: a small jump)
        fitW = Mathf.MoveTowards(fitW, busy && air ? 1f : 0f, Time.deltaTime * (busy && air ? 20f : 4f));
        Quaternion inv = Quaternion.Inverse(board.rotation);
        if (fitW <= 0f && !air && !busy && fm.enabled)
        {   // on the ground, the game's own pose: note the feet on the board, and the whole pose. Only when both feet are on
            // the board (not while pushing with one foot on the ground).
            Vector3 l0 = inv * (fl.position - board.position), r0 = inv * (fr.position - board.position);
            bool onBoard = l0.y < 0.3f && l0.y > -0.15f && r0.y < 0.3f && r0.y > -0.15f && Vector3.Distance(l0, r0) < 0.9f;
            missed = onBoard ? 0 : missed + 1;
            if (onBoard || (!havePose && missed > 90))          // (if the board's axes are not as expected: take it anyway)
            {
                refL = l0; refR = r0; haveRef = true;
                TakePose(root, board);
            }
            return;
        }
        if (fitW <= 0f || !haveRef || hippyActive) return;               // (a hippy jump: the board is on the ground, the feet are not)
        Vector3 a = fl.position, b = fr.position;
        Vector3 a2 = board.position + board.rotation * refL, b2 = board.position + board.rotation * refR;
        Vector3 up = board.up;
        Vector3 d1 = Vector3.ProjectOnPlane(b - a, up), d2 = Vector3.ProjectOnPlane(b2 - a2, up);
        Quaternion turn = d1.sqrMagnitude > 1e-6f && d2.sqrMagnitude > 1e-6f ? Quaternion.AngleAxis(Vector3.SignedAngle(d1, d2, up), up) : Quaternion.identity;
        Vector3 mid = (a + b) * 0.5f, mid2 = (a2 + b2) * 0.5f;
        fitRoot = root; fitPos = root.position; fitRot = root.rotation; fitOn = true;
        // turn the body around the middle of the feet, then move that middle onto the board spots
        Vector3 np = mid2 + turn * (root.position - mid);
        root.SetPositionAndRotation(Vector3.Lerp(root.position, np, fitW), Quaternion.Slerp(root.rotation, turn * root.rotation, fitW));
    }

    // ---------- the held air pose (a small animation layer) ----------
    // In an extra spin the board turns fast. The game's body follows the board through smoothed angles and its leg
    // reach (IK), so in a fast spin the hips lag behind the feet and the knees and feet twist. Fixing single bones after
    // that does not work well. So during the spin the mod holds a clean pose of its own: the last pose the game made on
    // the ground (each bone's place on its parent, and the body's place on the board), taken just before drawing. In the
    // air the body is set to that pose on the board, blended in and out with the spin; the tuck and grab go on top.
    // The body and the board then turn as one, and nothing twists.
    Transform[] poseBones = new Transform[0]; Vector3[] poseLP = new Vector3[0]; Quaternion[] poseLR = new Quaternion[0];
    Vector3 poseRootP; Quaternion poseRootR; bool havePose; Transform poseRootT;
    void TakePose(Transform root, Transform board)
    {
        if (poseRootT != root || poseBones.Length == 0)
        {
            List<Transform> l = new List<Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t != root) l.Add(t);
            poseBones = l.ToArray(); poseLP = new Vector3[poseBones.Length]; poseLR = new Quaternion[poseBones.Length]; poseRootT = root;
        }
        for (int i = 0; i < poseBones.Length; i++) { Transform t = poseBones[i]; if (t == null) continue; poseLP[i] = t.localPosition; poseLR[i] = t.localRotation; }
        Quaternion inv = Quaternion.Inverse(board.rotation);
        poseRootP = inv * (root.position - board.position); poseRootR = inv * root.rotation;
        havePose = true;
    }
    void HoldPose(float e)
    {
        if (!havePose || e < 0.001f || poseRootT == null || poseFm == null || poseFm.skaterMeshRenderer == null || poseFm.skaterMeshRenderer.rootBone != poseRootT) return;
        Transform board = DrawnBoard(); if (board == null) return;
        Transform root = poseRootT;
        Keep(root);
        Vector3 rp = board.position + board.rotation * poseRootP; Quaternion rr = board.rotation * poseRootR;
        root.SetPositionAndRotation(Vector3.Lerp(root.position, rp, e), Quaternion.Slerp(root.rotation, rr, e));
        for (int i = 0; i < poseBones.Length; i++)
        {
            Transform t = poseBones[i]; if (t == null) continue;
            Keep(t);
            t.localRotation = Quaternion.Slerp(t.localRotation, poseLR[i], e);
            t.localPosition = Vector3.Lerp(t.localPosition, poseLP[i], e);
        }
        posed = true;
    }

    // move the end of a leg to a target: turn each joint only around the knee line, so the legs bend the way knees bend
    static void Reach(List<Transform> c, Vector3 target, Vector3 axis, int iters)
    {
        Transform end = c[c.Count - 1];
        for (int it = 0; it < iters; it++)
        {
            for (int j = c.Count - 2; j >= 0; j--)
            {
                Vector3 a = Vector3.ProjectOnPlane(end.position - c[j].position, axis), b = Vector3.ProjectOnPlane(target - c[j].position, axis);
                if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) continue;
                float ang = Mathf.Clamp(Vector3.SignedAngle(a, b, axis), -25f, 25f);
                c[j].rotation = Quaternion.AngleAxis(ang, axis) * c[j].rotation;
            }
            if ((end.position - target).sqrMagnitude < 1e-6f) break;
        }
    }
    // the board objects that are drawn: the board of the skater, and the final board of the game (if it is not inside it)
    List<Transform> boardList = new List<Transform>();
    List<Transform> BoardParts()
    {
        boardList.Clear();
        if (sp == null || sp.board == null) return boardList;
        Transform b = sp.board.transform, fbt = null;
        try { FinalSkateboardMesh fb = poseFm != null ? poseFm.finalSkateboardMesh : null; if (fb != null && fb.final_board != null) fbt = fb.final_board.transform; } catch (Exception) { }
        if (fbt == null || fbt.IsChildOf(b)) boardList.Add(b);
        else if (b.IsChildOf(fbt)) boardList.Add(fbt);
        else { boardList.Add(b); boardList.Add(fbt); }
        return boardList;
    }

    // the next update: give the game its step back when the flip is over
    void GiveBackStep()
    {
        if (!fmOff || ReplayTools.Active) return;
        if (flipping && flipOn && poseOn) return;
        if (spinPoseW > 0.001f || hippyW > 0f || (poseOffT >= 0f && Time.time - poseOffT < 0.3f)) return;
        if (poseFm != null) poseFm.enabled = true;
        fmOff = false;
    }

    // ---------- menu ----------
    void DrawFlipMenu()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("   ", GUILayout.Width(20));
        string[] tabs = { "Buttons", "Timing", "Pose" };
        for (int k = 0; k < 3; k++) if (GUILayout.Toggle(flipTab == k, tabs[k], GUI.skin.button, GUILayout.Width(100)) && flipTab != k) flipTab = k;
        GUILayout.EndHorizontal();
        if (flipTab == 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("   Front flip:  " + (binding == 1 ? "hold the two buttons now... (Esc = no change)" : Combo(frontKeys)), GUILayout.Width(360));
            if (GUILayout.Button("Change", GUILayout.Width(70))) { binding = 1; held.Clear(); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("   Back flip:  " + (binding == 2 ? "hold the two buttons now... (Esc = no change)" : Combo(backKeys)), GUILayout.Width(360));
            if (GUILayout.Button("Change", GUILayout.Width(70))) { binding = 2; held.Clear(); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("   Turns", GUILayout.Width(180));
            if (GUILayout.Toggle(flipTurns == 1, "Single", GUI.skin.button, GUILayout.Width(80)) && flipTurns != 1) { flipTurns = 1; dirty = true; }
            if (GUILayout.Toggle(flipTurns == 2, "Double", GUI.skin.button, GUILayout.Width(80)) && flipTurns != 2) { flipTurns = 2; dirty = true; }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("   Flip over", GUILayout.Width(180));
            if (GUILayout.Toggle(flipStyle == 0, "The nose", GUI.skin.button, GUILayout.Width(100)) && flipStyle != 0) { flipStyle = 0; dirty = true; }
            if (GUILayout.Toggle(flipStyle == 1, "The toes", GUI.skin.button, GUILayout.Width(100)) && flipStyle != 1) { flipStyle = 1; dirty = true; }
            GUILayout.EndHorizontal();
            GUILayout.Label(flipStyle == 0 ? "   The nose: the board turns end over end, in the direction you ride."
                                           : "   The toes: a body flip. Front = over the toes, back = over the heels (the way a person flips).");
            flipStick = Toggle(flipStick, " Also flip with the stick (up = front, down = back)");
            GUILayout.Label("   One flip per jump. Hold the buttons as you pop for the fastest start. A flip always ends before you land.");
        }
        else if (flipTab == 1)
        {
            flipFeel = Slider("   Flip time (per turn)", flipFeel, 0.3f, 2f, "0.00", " s");
            flipDelay = Slider("   Start delay", flipDelay, 0f, 0.6f, "0.00", " s");
            tuckSpin = Slider("   Faster in the tuck", tuckSpin, 0f, 0.85f, "0.00", "");
            tuckIn = Slider("   Time to tuck", tuckIn, 0.05f, 0.5f, "0.00", "");
            tuckOut = Slider("   Time to open", tuckOut, 0.05f, 0.6f, "0.00", "");
            flipMargin = Slider("   Finish before landing", flipMargin, 0f, 0.4f, "0.00", " s");
            flipLift = Slider("   Lift so the flip has time", flipLift, 0f, 8f, "0.0", " m/s");
            FlipPivot = Slider("   Turn point", FlipPivot, 0f, 1.5f, "0.00", " m");
            GUILayout.Label("   Faster in the tuck: 0 = the same speed all the way, higher = slow open start and end, fast tucked middle.");
            GUILayout.Label("   Time to tuck and to open: parts of the flip (0.25 = a quarter). Lift: extra jump speed when the air is too short.");
            GUILayout.Label("   Turn point: 0 = around the board, 1 = around the chest.");
        }
        else
        {
            poseOn = Toggle(poseOn, " Change the pose in the flip (tuck and grab)");
            GUI.enabled = poseOn;
            tuckAmt = Slider("   Tuck", tuckAmt, 0f, 1f, "0.00", "");
            legLift = Slider("   Knees up", legLift, 0f, 0.6f, "0.00", " m");
            curlDeg = Slider("   Back curl", curlDeg, 0f, 70f, "0", " deg");
            GUILayout.BeginHorizontal();
            GUILayout.Label("   Grab", GUILayout.Width(180));
            string[] g = { "None", "Left", "Right", "Both" };
            for (int k = 0; k < 4; k++) if (GUILayout.Toggle(grabMode == k, g[k], GUI.skin.button, GUILayout.Width(62)) && grabMode != k) { grabMode = k; dirty = true; }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("   Grab side", GUILayout.Width(180));
            if (GUILayout.Toggle(!grabHeel, "Toe side", GUI.skin.button, GUILayout.Width(100)) && grabHeel) { grabHeel = false; dirty = true; }
            if (GUILayout.Toggle(grabHeel, "Heel side", GUI.skin.button, GUILayout.Width(100)) && !grabHeel) { grabHeel = true; dirty = true; }
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            GUILayout.Label("   Tuck: how far the body folds. Knees up: how far the board comes up to the body.");
            GUILayout.Label(poseInfo.Length > 0 ? "   " + poseInfo : "   (The bones are found at the first flip.)");
        }
        if (GUILayout.Button("Flip settings back to the start values", GUILayout.Width(280)))
        {
            flipDelay = DefDelay; FlipPivot = DefPivot; flipMargin = DefMargin; flipLift = DefLift; flipFeel = DefFeel; flipTurns = 1;
            poseOn = true; tuckAmt = 0.8f; tuckSpin = 0.5f; tuckIn = 0.22f; tuckOut = 0.3f; curlDeg = 30f; legLift = 0.3f; grabMode = 3; grabHeel = false;
            dirty = true;
        }
    }
}

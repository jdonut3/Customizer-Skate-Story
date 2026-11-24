// Movement tab of the editor.
//  Push power: the speed that each push adds is multiplied.
//  Power slide boost: a power slide gives a speed boost like a push (the game's own push boost, times a strength).
//  Game speed: slow motion (or fast) for the full game, with a hotkey, or only during tricks.
//  Grinds: a rail snap (in the air, the game's own rail detector checks spots around the board, and a rail below
//  steers the skater onto it), and grinds that stay on:
//  the game ends a grind when you steer sideways or go too slow, by filling a short timer. The editor keeps that
//  timer empty, so a grind ends only at the end of the rail or when you jump off. A minimum grind speed keeps you moving.
//  Jump height: when the game makes a jump (an ollie), the extra part of the jump force is added (or taken away).
//  Health: the fragile mode (one hit kills, after the end of the game) can be turned off without a change to the save,
//  damage can be turned off, and the game's own god mode (no bails) can be turned on.
//  Air spins: the game slows a spin down from 2x to a quarter in the first second of a jump. "Keep spinning" adds the
//  missing turn to the whole skater (as the game's own spin does) for the full jump, so you can spin past 360. When you
//  let go of the stick, the turn goes on to the nearest half turn, so you land regular or switch.
//  Flips: in the air, hold two buttons together (set in the menu). Only the drawn skater and board turn, around the middle
//  of the body, at the full frame rate; the game's jump, speed, landing and camera do not change. A flip starts fast and
//  always ends before the landing (it speeds up if the ground comes sooner).
// The game speed uses the game's own speed value (TimeMan.debugGameSpeed), so sounds slow down with it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

public partial class MoveTools
{
    public bool dirty;
    public string status = "";

    bool pushOn; float pushMul = 1.5f;
    bool slideOn; float slideMul = 1f; int slideWhen = 1;   // slideWhen: 0 = when the slide starts, 1 = when it ends
    bool wasSliding; float slideTime, lastSlideBoost = -10f;
    bool slideKeep = true; float slideHold;           // slideKeep: no speed loss while power sliding
    bool slowOn; float slowSpeed = 0.5f;          // the full game in slow motion (hotkey F7)
    bool trickSlowOn; float trickSlowSpeed = 0.5f; // slow motion only in the air and during tricks
    public KeyCode slowKey = KeyCode.F7;

    bool snapOn; float snapRadius = 1f;          // meters around the board in which a rail is found
    bool stickyOn; float minGrindSpeed = 3f;
    float noSnapUntil; bool wasGrinding; public string snapInfo = "";

    bool jumpOn; float jumpMul = 1.5f;
    bool spinOn; float spinMul = 1f;
    bool flipOn; float flipSpeed = 720f; float flipRate = 720f; bool flipStick = false;
    float flipTime, flipDur = 0.65f, flipFeel = 0.8f;     // flipFeel: the flip time on a jump with enough air (s)
    // flip feel (sliders)
    const float DefDelay = 0f, DefSharp = 2.6f, DefSoft = 0.25f, DefPivot = 0.45f, DefMargin = 0.04f, DefLift = 2.5f;
    float flipDelay = DefDelay;        // seconds after leaving the ground before the flip starts
    float flipSharp = DefSharp;        // how hard the flip starts (1 = even, 4 = a very fast start)
    float flipSoft = DefSoft;          // 0 = the end stays fast, 1 = a soft slow end
    float flipMargin = DefMargin;      // the flip ends this many seconds before the board comes down
    float flipLift = DefLift;          // the most extra up speed for a jump with almost no air (m/s)
    int flipTurns = 1;                 // 1 = single flip, 2 = double flip
    float pendingDir;                  // a flip that waits for the start delay
    // flip buttons: two buttons held together. Defaults: both stick clicks = front flip, both shoulder buttons = back flip.
    KeyCode[] frontKeys = { KeyCode.JoystickButton8, KeyCode.JoystickButton9 };
    KeyCode[] backKeys = { KeyCode.JoystickButton4, KeyCode.JoystickButton5 };
    int binding;                 // 0 = no, 1 = waiting for the front flip buttons, 2 = for the back flip buttons
    float flipDone, flipDir; bool flipping, flipUsed;
    Vector3 lastPop; bool popSeen;
    bool noFragile, noDamage, noBails;
    int savedGod = -1; bool damageTouched;
    const string Fragile = "dontclear-skull-fragile";
    static FieldInfo flagSetField;

    SkatePlayer sp; float nextFind;
    Vector2 scroll;
    float pushWindow;                              // seconds after a push in which speed gains count as push speed
    Vector3 lastVel; bool haveLast;
    float curScale = 1f; bool speedTouched; float speedNow = 1f; string otherSlow = "";
    void ResetGameSpeed()
    {
        TimeMan tm = null; try { tm = TimeMan.main; } catch (Exception) { }
        if (tm == null) return;
        tm.lineChallengeTimeScale = 1f; tm.physicDebugTimeScale = 1f; tm.dramaTimeScale = 1f;
        if (!ReplayTools.Active && !EditorTools.Editing) tm.uiPauseTimeScale = 1f;
        tm.debugGameSpeed = curScale;
        status = "The game's speed values are back to normal.";
    }

    // ---------- settings file ----------
    static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
    static float P(string s, float d) { float v; return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : d; }
    static KeyCode K(string s) { int v; return int.TryParse(s, out v) ? (KeyCode)v : KeyCode.None; }
    public void Load(string line)
    {
        line = line.Trim();
        if (!line.StartsWith("move=")) return;
        string[] f = line.Substring(5).Split(',');
        if (f.Length < 7) return;
        pushOn = f[0] == "1"; pushMul = P(f[1], 1.5f); slideOn = f[2] == "1"; slideMul = P(f[3], 2f);
        slowSpeed = P(f[4], 0.5f); trickSlowOn = f[5] == "1"; trickSlowSpeed = P(f[6], 0.5f);
        if (f.Length > 7) slideWhen = (int)P(f[7], 1); else slideMul = 1f;
        if (f.Length > 8) slideKeep = f[8] == "1";
        if (f.Length > 22) { spinOn = f[18] == "1"; spinMul = Mathf.Clamp(P(f[19], 1f), 0.25f, 3f); flipOn = f[20] == "1"; flipStick = f[22] == "1"; }
        if (f.Length > 27) { flipFeel = Mathf.Clamp(P(f[23], DefFeel), 0.3f, 2f); frontKeys = new[] { K(f[24]), K(f[25]) }; backKeys = new[] { K(f[26]), K(f[27]) }; }
        if (f.Length > 34)
        {
            flipDelay = Mathf.Clamp(P(f[28], DefDelay), 0f, 0.6f); flipSharp = Mathf.Clamp(P(f[29], DefSharp), 1f, 4f); flipSoft = Mathf.Clamp01(P(f[30], DefSoft));
            FlipPivot = Mathf.Clamp(P(f[31], DefPivot), 0f, 1.5f); flipMargin = Mathf.Clamp(P(f[32], DefMargin), 0f, 0.4f);
            flipLift = Mathf.Clamp(P(f[33], DefLift), 0f, 8f); flipTurns = Mathf.Clamp((int)P(f[34], 1), 1, 2);
        }
        else if (f.Length > 22) flipStick = false;     // an older line: the stick for flips is off now
        if (f.Length > 44)
        {
            flipStyle = Mathf.Clamp((int)P(f[35], 0), 0, 1); tuckAmt = Mathf.Clamp01(P(f[36], 0.8f)); tuckSpin = Mathf.Clamp(P(f[37], 0.5f), 0f, 0.85f);
            tuckIn = Mathf.Clamp(P(f[38], 0.22f), 0.05f, 0.5f); tuckOut = Mathf.Clamp(P(f[39], 0.3f), 0.05f, 0.6f); curlDeg = Mathf.Clamp(P(f[40], 30f), 0f, 70f);
            grabMode = Mathf.Clamp((int)P(f[41], 3), 0, 3); grabHeel = f[42] == "1"; legLift = Mathf.Clamp(P(f[43], 0.3f), 0f, 0.6f); poseOn = f[44] == "1";
        }
        if (f.Length > 46) { vibMax = Mathf.Clamp01(P(f[45], 1f)); spinPose = f[46] == "1"; }
        if (f.Length > 56)
        {
            darkOn = f[47] == "1"; darkKeys = new[] { K(f[48]), K(f[49]) }; wheelH = Mathf.Clamp(P(f[50], 0.11f), -0.3f, 0.3f);
            hippyOn = f[51] == "1"; hippyKeys = new[] { K(f[52]), K(f[53]) }; hippyH = Mathf.Clamp(P(f[54], 0.7f), 0.2f, 2f);
            hippyDur = Mathf.Clamp(P(f[55], 0.75f), 0.3f, 1.5f); hippyKnees = Mathf.Clamp(P(f[56], 0.3f), 0f, 0.6f);
        }
        if (f.Length > 17) { jumpOn = f[13] == "1"; jumpMul = Mathf.Clamp(P(f[14], 1.5f), 0.5f, 4f); noFragile = f[15] == "1"; noDamage = f[16] == "1"; noBails = f[17] == "1"; }
        if (f.Length > 12) { snapOn = f[9] == "1"; snapRadius = Mathf.Clamp(P(f[10], 1f), 0.25f, 2f); stickyOn = f[11] == "1"; minGrindSpeed = P(f[12], 3f); }     // an older line: its slide value had a different meaning
        // the full-game slow motion is not saved: the game always starts at normal speed
    }
    public void Write(StringBuilder sb)
    {
        sb.Append("move=").Append(pushOn ? "1" : "0").Append(',').Append(F(pushMul)).Append(',').Append(slideOn ? "1" : "0").Append(',').Append(F(slideMul))
          .Append(',').Append(F(slowSpeed)).Append(',').Append(trickSlowOn ? "1" : "0").Append(',').Append(F(trickSlowSpeed)).Append(',').Append(slideWhen).Append(',').Append(slideKeep ? "1" : "0")
          .Append(',').Append(snapOn ? "1" : "0").Append(',').Append(F(snapRadius)).Append(',').Append(stickyOn ? "1" : "0").Append(',').Append(F(minGrindSpeed))
          .Append(',').Append(jumpOn ? "1" : "0").Append(',').Append(F(jumpMul)).Append(',').Append(noFragile ? "1" : "0").Append(',').Append(noDamage ? "1" : "0").Append(',').Append(noBails ? "1" : "0")
          .Append(',').Append(spinOn ? "1" : "0").Append(',').Append(F(spinMul)).Append(',').Append(flipOn ? "1" : "0").Append(',').Append(F(flipSpeed)).Append(',').Append(flipStick ? "1" : "0")
          .Append(',').Append(F(flipFeel)).Append(',').Append((int)frontKeys[0]).Append(',').Append((int)frontKeys[1]).Append(',').Append((int)backKeys[0]).Append(',').Append((int)backKeys[1])
          .Append(',').Append(F(flipDelay)).Append(',').Append(F(flipSharp)).Append(',').Append(F(flipSoft)).Append(',').Append(F(FlipPivot))
          .Append(',').Append(F(flipMargin)).Append(',').Append(F(flipLift)).Append(',').Append(flipTurns)
          .Append(',').Append(flipStyle).Append(',').Append(F(tuckAmt)).Append(',').Append(F(tuckSpin)).Append(',').Append(F(tuckIn)).Append(',').Append(F(tuckOut))
          .Append(',').Append(F(curlDeg)).Append(',').Append(grabMode).Append(',').Append(grabHeel ? "1" : "0").Append(',').Append(F(legLift)).Append(',').Append(poseOn ? "1" : "0")
          .Append(',').Append(F(vibMax)).Append(',').Append(spinPose ? "1" : "0")
          .Append(',').Append(darkOn ? "1" : "0").Append(',').Append((int)darkKeys[0]).Append(',').Append((int)darkKeys[1]).Append(',').Append(F(wheelH))
          .Append(',').Append(hippyOn ? "1" : "0").Append(',').Append((int)hippyKeys[0]).Append(',').Append((int)hippyKeys[1]).Append(',').Append(F(hippyH))
          .Append(',').Append(F(hippyDur)).Append(',').Append(F(hippyKnees)).Append('\n');
    }

    // ---------- each frame ----------
    void FindPlayer(float now)
    {
        if (sp != null && now < nextFind) return;
        nextFind = now + 2f;
        SkatePlayer n = null;
        try { n = ThePlayer.SkatePlayer; } catch (Exception) { }
        if (n == sp) return;
        if (sp != null) sp.OnPush = (Action)Delegate.Remove(sp.OnPush, (Action)OnPushed);
        sp = n; haveLast = false;
        if (sp != null) sp.OnPush = (Action)Delegate.Combine(sp.OnPush, (Action)OnPushed);
    }
    void OnPushed() { pushWindow = 0.6f; }

    public void Update(float now)
    {
        inst = this; HookCameras();
        AfterDraw();
        GiveBackStep();
        try { LogFrame(); } catch (Exception e) { status = "Test log error: " + e.Message; }
        // the game speed first and on its own: an error in another part must not stop it
        try { GameSpeed(); } catch (Exception e) { status = "Game speed error: " + e.Message; }
        try { FindPlayer(now); } catch (Exception e) { status = "Movement error: " + e.Message; }
        try { Snap(); } catch (Exception e) { status = "Rail snap error: " + e.Message; }
        try { Sticky(); } catch (Exception e) { status = "Grind error: " + e.Message; }
        try { Jump(); } catch (Exception e) { status = "Jump error: " + e.Message; }
        try { Health(); } catch (Exception e) { status = "Health error: " + e.Message; }
        try { Speeds(); } catch (Exception e) { status = "Speed error: " + e.Message; }
    }

    // ---------- grinds ----------
    // ---------- rail snap ----------
    // In the air and falling, the game's rail detector (the same one the game uses to start a grind) is moved to
    // spots around the board. If it finds a rail below the board, the skater's speed is turned along the rail and
    // pulled sideways onto it, so the game's normal grind start catches it when the board comes down.
    static readonly Vector3[] dirs = { new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1),
        new Vector3(0.7071f, 0, 0.7071f), new Vector3(-0.7071f, 0, 0.7071f), new Vector3(0.7071f, 0, -0.7071f), new Vector3(-0.7071f, 0, -0.7071f) };
    void Snap()
    {
        if (sp == null) return;
        SkatePlayer.SkimGrindState g = sp.skimGrindState;
        bool grinding = g != null && g.inGrind;
        float now = Time.time;
        if (wasGrinding && !grinding) noSnapUntil = now + 0.5f;      // just jumped off: no snap back onto the same rail
        wasGrinding = grinding;
        if (!snapOn || grinding || now < noSnapUntil || (g != null && g.forceNoGrindTimer > 0f)) return;
        bool air = false;
        try { air = ThePlayer.IS_AIRBORNE; } catch (Exception) { }
        Vector3 v = sp.skPhysics.velocity;
        if (!air || v.y > 0.5f) return;                              // only in the air, from the top of the jump on
        GrindableDetectorProbe p = sp.skimGrindProbe;
        if (p == null) return;
        Transform pt = p.transform;
        Vector3 home = pt.parent != null ? pt.parent.TransformPoint(p.homePosition) : pt.position;
        Vector3 keep = pt.position;
        bool found = false; float best = 1e9f; Vector3 rp = Vector3.zero, rf = Vector3.zero;
        try
        {
            for (int h = 0; h < 3 && !found; h++)
            {
                float down = h * 0.6f;
                for (int ri = 0; ri <= 3; ri++)
                {
                    float r = snapRadius * ri / 3f;
                    int n = ri == 0 ? 1 : dirs.Length;
                    for (int d = 0; d < n; d++)
                    {
                        pt.position = home + dirs[d] * r + Vector3.down * down;
                        p.UpdateMe();
                        GrindableDetectorProbe.Result res = p.result;
                        if (!res.isGrind) continue;
                        Vector3 q = res.idealLedgePoint;
                        if (q.y > home.y + 0.25f || q.y < home.y - 2.5f) continue;   // the rail must be below the board
                        Vector3 off = q - home; off.y = 0f;
                        if (off.magnitude > snapRadius + 0.1f) continue;
                        float score = off.magnitude + (home.y - q.y) * 0.3f;
                        if (score < best) { best = score; rp = q; rf = res.idealLedgeForward; found = true; }
                    }
                }
            }
        }
        finally
        {   // put the detector back, so the game's next check starts from its own place
            pt.position = keep;
            p.UpdateMe();
        }
        if (!found) { snapInfo = ""; return; }
        rf.y = 0f;
        if (rf.sqrMagnitude < 0.0001f) return;
        rf.Normalize();
        Vector3 toRail = rp - home; toRail.y = 0f;
        Vector3 side = toRail - rf * Vector3.Dot(toRail, rf);           // the sideways distance to the rail line
        Vector3 vh = new Vector3(v.x, 0f, v.z);
        float along = Vector3.Dot(vh, rf);
        if (Mathf.Abs(along) < 0.5f) along = Vector3.Dot(sp.velocityTransformForward, rf) < 0f ? -Mathf.Max(vh.magnitude, 2f) : Mathf.Max(vh.magnitude, 2f);
        Vector3 pull = Vector3.ClampMagnitude(side / 0.12f, 10f);         // reach the rail line in about 0.12 seconds
        Vector3 nh = rf * along + pull;
        v.x = nh.x; v.z = nh.z;
        float above = home.y - rp.y;
        if (above > 0.4f) v.y = Mathf.Min(v.y, -above / 0.25f);           // come down onto the rail quickly
        sp.skPhysics.velocity = v;
        lastVel = v;
        snapInfo = "Rail snap active";
    }

    // Called each physics step.
    public void Physics()
    {
        try { Sticky(); Jump(); AirTricks(); ProtectTick(); } catch (Exception) { }
    }

    // ---------- air spins and flips ----------
    // What you see in the air is the skater model, turned by the game's stance rotation (x = forward and back, y = spin).
    // The physics body is not turned: the game turns it back to the direction of travel, and the camera follows it.
    float spinAdded; bool wasAir;
    float spinRate, spinUntil; bool settleWanted;          // deg/s of the extra spin, and until when (set by the physics step)
    public float spinPoseW;             // 0..1: how far the body is in the spin pose (tuck and grab)
    // each drawn frame: the extra spin, a small part each frame, so the turn is smooth at any frame rate
    public void FrameSpin()
    {
        if (sp == null || ReplayTools.Active) return;
        try { GroundTricks(); } catch (Exception) { }
        bool air = false; try { air = ThePlayer.IS_AIRBORNE; } catch (Exception) { }
        float dt = Time.deltaTime;
        if (spinOn && air && Time.time < spinUntil && spinRate != 0f && dt > 0f)
        {
            float d = spinRate * dt;
            TurnLocalYaw(d);
            spinAdded += d;
            try { sp.skState.degreesTurnedInAir += d; sp.skState.degreesTurnedInAir_abs += Mathf.Abs(d); } catch (Exception) { }
        }
        if (spinOn && air && settleWanted && spinAdded != 0f && Time.time >= spinUntil && dt > 0f) SettleSpin(dt, GroundSoon());
        if (!air) settleWanted = false;
        // the spin pose: in while you spin, out before the landing. The game's air flag can flicker for one step (near a
        // wall, on a bump): the pose only goes when the skater has been on the ground for a moment, never at once.
        if (air) lastAirT = Time.time; else lastGndT = Time.time;
        bool steadyAir = Time.time - lastGndT > 0.12f || (air && Time.time - lastGndT > 0.05f);
        bool steadyGround = Time.time - lastAirT > 0.15f;
        bool want = spinOn && steadyAir && spinAdded != 0f && !GroundSoon();     // the spin pose layer (the tuck in it: Pose settings)
        spinPoseW = Mathf.MoveTowards(spinPoseW, want ? 1f : 0f, Time.deltaTime * (want ? 4f : 3.5f));
        if (steadyGround) spinPoseW = Mathf.MoveTowards(spinPoseW, 0f, Time.deltaTime * 5f);
        if (spinPoseW <= 0.001f) { if (poseOffT < 0f) poseOffT = Time.time; } else poseOffT = -1f;
    }
    public bool spinPose = true;        // tuck and grab in an extra spin
    float lastAirT = -10f, lastGndT = -10f; public float poseOffT = -1f;
    // controller vibration: at most this strong (1 = the game's own), and a lower cap the level editor sets in pipe airs
    public float vibMax = 1f; public static float VibCapNow = 1f;
    public void VibCap()
    {
        float cap = Mathf.Min(vibMax, VibCapNow);
        if (cap >= 0.999f) return;
        try
        {
            VibrationMan vm = VibrationMan.main; if (vm == null) return;
            foreach (VibrationMan.Vib v in new[] { vm.vib, vm.vib_4, vm.vib_flicker })
            {
                if (v == null) continue;
                foreach (VibrationMan.Motor mo in new[] { v.strongMotor, v.weakMotor, v.leftMotor, v.rightMotor })
                    if (mo != null && mo.renderValue > cap) mo.renderValue = cap;
            }
        }
        catch (Exception) { }
    }        // tuck and grab in an extra spin
    // ---------- test log for spins: the angle of each part, each drawn frame in the air ----------
    public bool spinLog; System.Text.StringBuilder logBuf = new System.Text.StringBuilder(); int logLines; bool logAir; float lastExtra;
    static float Yaw(Transform t) { if (t == null) return -999f; Vector3 f = t.forward; return Mathf.Atan2(f.x, f.z) * 57.29578f; }
    public void LogFrame()
    {
        if (!spinLog || sp == null) return;
        bool air = false; try { air = ThePlayer.IS_AIRBORNE; } catch (Exception) { }
        if (!air)
        {
            if (logAir)
            {   // the jump ended: write it
                logBuf.Append("LAND  dTurn=").Append(sp.skState.degreesTurnedInAir.ToString("0")).Append('\n');
                try { string lp = System.IO.Path.Combine(System.IO.Path.Combine(Application.persistentDataPath, "ShoeMod"), "spinlog.txt"); string old = System.IO.File.Exists(lp) ? System.IO.File.ReadAllText(lp) : ""; System.IO.File.WriteAllText(lp, old + logBuf.ToString()); } catch (Exception) { }
                logBuf.Length = 0;
            }
            logAir = false; return;
        }
        if (!logAir) { logAir = true; logBuf.Append("JUMP ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n'); }
        if (++logLines > 20000) return;
        ThePlayer p = ThePlayer.instance;
        Vector2 mv = Vector2.zero; try { mv = sp.input.move; } catch (Exception) { }
        SkatePlayer.SkState st = sp.skState;
        logBuf.Append("t=").Append(st.airborneTimer.ToString("0.00"))
          .Append(" mvx=").Append(mv.x.ToString("0.0"))
          .Append(" body=").Append(Yaw(sp.transform).ToString("0"))
          .Append(" board=").Append(Yaw(sp.board != null ? sp.board.transform : null).ToString("0"))
          .Append(" anim=").Append(Yaw(sp.skateAnimator != null ? sp.skateAnimator.transform : null).ToString("0"))
          .Append(" head=").Append(Yaw(p != null && p.finalSkaterMesh != null ? p.finalSkaterMesh.finalHead : null).ToString("0"))
          .Append(" pboard=").Append(Yaw(p != null && p.physicsBoard != null ? p.physicsBoard.transform : null).ToString("0"))
          .Append(" stT=").Append(sp.stance.StanceRotationTarget.y.ToString("0"))
          .Append(" stNow=").Append(sp.skateAnimatorStanceRotation.y.ToString("0"))
          .Append(" dTurn=").Append(st.degreesTurnedInAir.ToString("0"))
          .Append(" car=").Append(st.canAirRotate.ToString("0.0")).Append(" dbl=").Append(st.doubledAirRot)
          .Append(" extra=").Append(lastExtra.ToString("0.0"))
          .Append(" vy=").Append(sp.skPhysics.velocity.y.ToString("0.0")).Append(" y=").Append(sp.transform.position.y.ToString("0.00"))
          .Append(" flip=").Append(flipping ? FlipAngle().ToString("0") : "-")
          .Append(" up=").Append(sp.transform.up.y.ToString("0.00"))
          .Append(" gap=").Append(p != null && p.finalSkaterMesh != null && p.finalSkaterMesh.finalHead != null && sp.board != null
                ? Vector3.Distance(p.finalSkaterMesh.finalHead.position, sp.board.transform.position).ToString("0.00") : "-").Append('\n');
        if (flipLog.Length > 0) { logBuf.Append(flipLog).Append('\n'); flipLog = ""; }
    }
    static System.Reflection.FieldInfo stanceNow;
    void AirTricks()
    {
        if (sp == null || sp.skState == null || sp.stance == null) return;
        SkatePlayer.SkState st = sp.skState;
        bool air = false;
        try { air = ThePlayer.IS_AIRBORNE; } catch (Exception) { }
        bool grind = sp.skimGrindState != null && sp.skimGrindState.inGrind;
        float dt = TimeMan.skateDeltaTime;
        if (!air || grind)
        {
            if (wasAir) Landed();
            wasAir = false; flipUsed = false;
            return;
        }
        wasAir = true;
        if (dt <= 0f) return;
        Vector2 mv = Vector2.zero;
        try { mv = sp.input.move; } catch (Exception) { }
        Vector3 tgt = sp.stance.StanceRotationTarget;

        // keep spinning: the game's speed falls from 2x to 0.25x in the first second. The extra turn goes on the whole
        // skater (body and board together, as the game's own spin does), so the legs do not twist.
        if (spinOn && st.isAirborneMode)
        {
            if (Mathf.Abs(mv.x) > 0.3f)
            {
                float rate = Mathf.Lerp(120f, 200f, Mathf.Abs(st.canAirRotate));
                if (st.doubledAirRot > 0) rate *= 1.8f;
                bool gameSpins = st.canAirRotate != 0f || st.doubledAirRot > 0;
                float gameMul = gameSpins ? Mathf.Lerp(2f, 0.25f, st.airborneTimer) : 0f;
                float extra = mv.x * dt * rate * Mathf.Max(0f, 2f * spinMul - gameMul);
                if (!gameSpins) extra *= 0.5f;                 // no flick at the start: a slower spin
                lastExtra = extra;
                // the speed of the extra turn: it is turned in each drawn frame (FrameSpin), not in big physics steps
                spinRate = extra / dt; spinUntil = Time.time + Mathf.Max(0.06f, dt * 1.5f);
                if (extra != 0f && spinAdded == 0f) spinAdded = 0.001f * Mathf.Sign(extra);
            }
            // the turn on to the nearest half turn: also in each drawn frame (FrameSpin), so the end is smooth too
            settleWanted = spinAdded != 0f && (Mathf.Abs(mv.x) <= 0.3f || GroundSoon());
        }

        // flips: only what is drawn turns (see FlipVisual). Here: start a flip, and make it end before the landing.
        if (flipOn)
        {
            if (!flipping && !flipUsed && pendingDir == 0f && st.airborneTimer > 0f)
            {
                float want = 0f;
                if (flipStick && Mathf.Abs(mv.y) > 0.75f && Mathf.Abs(mv.y) > Mathf.Abs(mv.x)) want = mv.y > 0f ? 1f : -1f;
                if (Held(frontKeys)) want = 1f;
                if (Held(backKeys)) want = -1f;
                if (want != 0f) pendingDir = want;          // it starts after the start delay
            }
            if (!flipping && pendingDir != 0f && st.airborneTimer >= flipDelay) { StartFlip(pendingDir); pendingDir = 0f; }
            if (flipping)
            {   // the flip must end before the board comes down: if the ground is nearer than planned, end earlier
                float left = TimeToGround();
                float endBy = Time.time + Mathf.Max(0.05f, left - flipMargin);
                if (endBy < flipEnd) flipEnd = Mathf.Max(Time.time + 0.05f, endBy);
            }
        }
        // the trick count: the real turn of the skater in this jump (not the turns that the game takes back)
        if (spinOn && !flipping)
        {
            float y = YawNow();
            if (haveYaw) measured += Mathf.DeltaAngle(lastYaw, y);
            lastYaw = y; haveYaw = true;
            st.degreesTurnedInAir = measured;
            st.degreesTurnedInAir_abs = Mathf.Abs(measured);
        }
        sp.stance.StanceRotationTarget = tgt;
    }
    float measured, lastYaw; bool haveYaw;
    float YawNow() { Vector3 f = sp.transform.forward; f.y = 0f; return f.sqrMagnitude < 0.0001f ? lastYaw : Mathf.Atan2(f.x, f.z) * 57.29578f; }

    Vector3 flipAxis; int savedGodFlip = -1; float protectUntil; string flipLog = "";
    // for the camera: a flip is on, and the middle of the body (the point the flip turns around)
    public static float FlipPivot = 0.45f;     // the flip turns around this point above the board (m)
    public static bool FlipNow; public static float FlipCenterTime; public static Vector3 FlipVel;
    Vector3 flipHVel; public static float FlipEndTime = -10f; public static Vector3 FlipCenter;
    void StartFlip(float dir)
    {
        Vector3 r = sp.transform.right; r.y = 0f;
        if (r.sqrMagnitude < 0.01f) return;
        flipAxis = r.normalized;
        if (flipStyle == 1)
        {   // a body flip: around the line of the board (the skater's left and right). Front = the head goes over the toes.
            Vector3 fwd = sp.transform.forward; fwd.y = 0f;
            Vector3 front = sp.board != null ? Front(sp.board.transform) : sp.transform.right; front.y = 0f;
            if (fwd.sqrMagnitude > 0.01f && front.sqrMagnitude > 0.01f)
            {
                flipAxis = fwd.normalized;
                if (Vector3.Dot(Vector3.Cross(Vector3.up, front.normalized), flipAxis) < 0f) flipAxis = -flipAxis;
            }
        }
        flipDir = dir; flipping = true; flipUsed = true;
        float want = flipFeel * flipTurns + flipMargin;
        float left = TimeToGround();
        if (left < want && flipLift > 0f)
        {   // not enough air for the full flip: more up speed (at most the Lift value), so the flip has its time
            float g = sp.skPhysics.gravity.magnitude; if (g < 1f) g = 25f;
            float d = groundDist;
            Vector3 v = sp.skPhysics.velocity;
            float need = (g * want * want - 2f * d) / (2f * want);
            float vy = Mathf.Min(need, Mathf.Max(v.y, 0f) + flipLift);
            if (vy > v.y) { v.y = vy; sp.skPhysics.velocity = v; lastVel = v; }
            left = TimeToGround();
        }
        flipStart = Time.time;
        flipEnd = flipStart + Mathf.Clamp(Mathf.Min(flipFeel * flipTurns, left - flipMargin), 0.25f, 4f);
        BuildAngleTable();
        flipLog = "flip start air=" + left.ToString("0.00") + " dur=" + (flipEnd - flipStart).ToString("0.00");
    }
    // the time until the board comes down: from the height above the ground and the up and down speed
    float TimeToGround()
    {
        float g = sp.skPhysics.gravity.magnitude; if (g < 1f) g = 25f;
        float vy = sp.skPhysics.velocity.y;
        Vector3 o = sp.transform.position + Vector3.up * 0.3f;
        int n = UnityEngine.Physics.RaycastNonAlloc(o, Vector3.down, groundHits, 30f, ~0, QueryTriggerInteraction.Ignore);
        Transform me = ThePlayer.instance != null ? ThePlayer.instance.transform : null;
        float d = 30f;
        for (int i = 0; i < n; i++)
            if (groundHits[i].collider != null && (me == null || !groundHits[i].collider.transform.IsChildOf(me)) && groundHits[i].distance < d) d = groundHits[i].distance;
        d = Mathf.Max(0f, d - 0.3f);
        groundDist = d;
        return (vy + Mathf.Sqrt(Mathf.Max(0f, vy * vy + 2f * g * d))) / g;
    }
    public float flipStart, flipEnd; float groundDist;

    static bool Held(KeyCode[] k)
    {
        if (k == null || k.Length < 2 || k[0] == KeyCode.None || k[1] == KeyCode.None) return false;
        return Input.GetKey(k[0]) && Input.GetKey(k[1]);
    }
    static string KeyName(KeyCode k)
    {
        switch (k)
        {
            case KeyCode.JoystickButton0: return "A";
            case KeyCode.JoystickButton1: return "B";
            case KeyCode.JoystickButton2: return "X";
            case KeyCode.JoystickButton3: return "Y";
            case KeyCode.JoystickButton4: return "LB";
            case KeyCode.JoystickButton5: return "RB";
            case KeyCode.JoystickButton6: return "Back";
            case KeyCode.JoystickButton7: return "Start";
            case KeyCode.JoystickButton8: return "Left stick click";
            case KeyCode.JoystickButton9: return "Right stick click";
            case KeyCode.None: return "(none)";
        }
        return k.ToString();
    }
    static string Combo(KeyCode[] k) { return KeyName(k[0]) + " + " + KeyName(k[1]); }
    // waiting for new flip buttons: the first two buttons that are held together
    List<KeyCode> held = new List<KeyCode>(); static KeyCode[] allKeys;
    void Bind()
    {
        if (allKeys == null)
        {
            List<KeyCode> l = new List<KeyCode>();
            foreach (KeyCode k in (KeyCode[])Enum.GetValues(typeof(KeyCode)))
                if (k != KeyCode.None && k != KeyCode.Mouse0 && k != KeyCode.Mouse1 && k != KeyCode.Escape && (int)k < (int)KeyCode.Joystick1Button0) l.Add(k);
            allKeys = l.ToArray();
        }
        if (Input.GetKey(KeyCode.Escape)) { binding = 0; held.Clear(); status = "No change to the buttons."; return; }
        foreach (KeyCode k in allKeys) if (Input.GetKey(k) && !held.Contains(k)) held.Add(k);
        held.RemoveAll(k => !Input.GetKey(k));
        if (binding == 4 && held.Count >= 1)
        {   // the hippy jump: one button
            hippyKeys = new[] { held[0], KeyCode.None };
            status = "Hippy jump: " + KeyName(held[0]);
            binding = 0; held.Clear(); dirty = true; return;
        }
        if (held.Count >= 2)
        {
            KeyCode[] n = { held[0], held[1] };
            if (binding == 1) frontKeys = n; else if (binding == 2) backKeys = n; else if (binding == 3) darkKeys = n; else hippyKeys = n;
            status = (binding == 1 ? "Front flip: " : binding == 2 ? "Back flip: " : binding == 3 ? "Darkslide: " : "Hippy jump: ") + Combo(n);
            binding = 0; held.Clear(); dirty = true;
        }
    }

    // ---------- the drawn flip ----------
    // The physics body does not turn. Just before a frame is drawn, the drawn skater (the final skater mesh) and the drawn
    // board turn around the middle of the body; just after the frame, they go back. So the flip is smooth at the full frame
    // rate, the game's jump and speed do not change, and the game camera (already placed for this frame) does not see it.
    public static MoveTools inst;
    static bool hooked;
    public static void HookCameras()
    {
        if (hooked) return; hooked = true;
        Camera.onPreCull += c => { if (inst != null && c != null && c == Camera.main) { try { inst.BeforeDraw(c); } catch (Exception) { } } };
        Camera.onPostRender += c => { if (inst != null && c != null && c == Camera.main) inst.AfterDraw(); };
    }
    bool drawnTurned; int drawnFrame = -1;
    Transform[] drawnParts = new Transform[3]; Vector3[] drawnPos = new Vector3[3]; Quaternion[] drawnRot = new Quaternion[3];
    public float FlipAngle()
    {
        if (!flipping) return 0f;
        float u = Mathf.Clamp01((Time.time - flipStart) / Mathf.Max(0.05f, flipEnd - flipStart));
        return 360f * flipTurns * Progress(u) * flipDir;      // see FlipPose.cs: slow open, fast tucked, slow open
    }
    public void BeforeDraw(Camera c)
    {
        if (drawnTurned || drawnFrame == Time.frameCount) return;
        if (ReplayTools.Active) return;                 // a replay shows the recorded poses
        FeetOnBoard();
        try { DrawTricks(); } catch (Exception) { }
        if (!flipping || sp == null || sp.board == null) return;
        ThePlayer p = ThePlayer.instance;
        if (p == null || p.finalSkaterMesh == null) return;
        if (Time.time >= flipEnd) { flipping = false; return; }
        float a = FlipAngle();
        Transform board = sp.board.transform;
        Vector3 pivot = havePivot ? pivotBase : board.position + board.up * FlipPivot;
        havePivot = false;
        Quaternion q = Quaternion.AngleAxis(a, flipAxis);
        // the body and the shoes are drawn from the skeleton: turn its root bone (or the whole final mesh object)
        Transform body = p.finalSkaterMesh.skaterMeshRenderer != null && p.finalSkaterMesh.skaterMeshRenderer.rootBone != null
            ? p.finalSkaterMesh.skaterMeshRenderer.rootBone : p.finalSkaterMesh.transform;
        // the body and every drawn board object turn by the same turn around the same point, so they stay together
        if (poseFm == null) poseFm = p.finalSkaterMesh;
        List<Transform> bl = BoardParts();
        drawnParts[0] = body; drawnParts[1] = bl.Count > 0 ? bl[0] : board; drawnParts[2] = bl.Count > 1 ? bl[1] : null;
        for (int i = 0; i < 3; i++)
        {
            Transform t = drawnParts[i];
            if (t == null) continue;
            drawnPos[i] = t.position; drawnRot[i] = t.rotation;
            t.SetPositionAndRotation(pivot + q * (t.position - pivot), q * t.rotation);
        }
        drawnTurned = true; drawnFrame = Time.frameCount;
    }
    public void AfterDraw()
    {
        if (drawnTurned)
        {
            for (int i = 2; i >= 0; i--) if (drawnParts[i] != null) drawnParts[i].SetPositionAndRotation(drawnPos[i], drawnRot[i]);
            drawnTurned = false;
        }
        RestoreTricks();
        if (fitOn) { if (fitRoot != null) fitRoot.SetPositionAndRotation(fitPos, fitRot); fitOn = false; }
        if (posed) PutBack();           // then the tuck and the grab go back
    }

    // no bails during the flip and for a short time after the landing
    void Protect(bool on)
    {
        if (on) { if (savedGodFlip < 0) savedGodFlip = SkateSingleton.GODMODE; SkateSingleton.GODMODE = 1; protectUntil = 1e30f; }
        else protectUntil = Time.time + 0.6f;
    }
    void ProtectTick()
    {
        if (binding != 0) Bind();
        if (savedGodFlip >= 0 && Time.time > protectUntil)
        {
            SkateSingleton.GODMODE = noBails ? 1 : savedGodFlip;
            savedGodFlip = -1;
        }
    }
    // turn the skater around a world axis through the middle of the body (about 0.7 m above the board)
    void TurnAround(Vector3 axis, float deg)
    {
        FlipCenter = sp.transform.position + sp.transform.up * FlipPivot;
        FlipCenterTime = Time.time; FlipVel = sp.skPhysics.velocity;
        Quaternion q = Quaternion.AngleAxis(deg, axis);
        Vector3 pivot = sp.transform.position + sp.transform.up * FlipPivot;
        Vector3 np = pivot + q * (sp.transform.position - pivot);
        TurnSmoothed(q);
        TurnSmoothedPos(q, pivot);
        sp.transform.SetPositionAndRotation(np, q * sp.transform.rotation);
        SkateInterpolatedTransform s = Sit();
        if (s != null)
        {
            // the same turn on the saved pose (local to the parent), around its own middle of the body
            Transform p = sp.transform.parent;
            Quaternion pr = p != null ? p.rotation : Quaternion.identity;
            Quaternion ql = Quaternion.Inverse(pr) * q * pr;
            TransformState c = s.current;
            Quaternion cr = (Quaternion)c.rot; Vector3 cp = (Vector3)c.pos;
            Vector3 piv = cp + cr * Vector3.up * FlipPivot;
            c.pos = piv + ql * (cp - piv);
            c.rot = ql * cr;
            s.current = c;
        }
    }

    // After a spin: turn the skater on to the nearest half turn of the direction of travel (regular or switch), fast,
    // so that the game's own slow turn back has nothing to do and you do not land facing the wrong way.
    void SettleSpin(float dt, bool now)
    {
        Vector3 v = sp.skPhysics.velocity; v.y = 0f;
        Vector3 f = sp.transform.forward; f.y = 0f;
        if (v.sqrMagnitude < 0.25f || f.sqrMagnitude < 0.01f) { spinAdded = 0f; return; }
        float a = Vector3.SignedAngle(f, v, Vector3.up);
        float r = a - Mathf.Round(a / 180f) * 180f;          // the rest to the nearest half turn
        float d = Mathf.MoveTowards(0f, r, (now ? 1800f : 900f) * dt);     // fast near the ground, never in one jump
        TurnWorldYaw(d);
        st_turn(d);
        if (Mathf.Abs(r - d) < 0.5f) spinAdded = 0f;
    }
    // The game keeps a saved pose of the skater for its own interpolation, and puts it back before each physics step.
    // A turn must go into that saved pose too, or the game throws it away.
    SkateInterpolatedTransform sit;
    SkateInterpolatedTransform Sit() { if (sit == null || sit.gameObject != sp.gameObject) sit = sp.GetComponent<SkateInterpolatedTransform>(); return sit; }
    // The drawn body follows the board through two smoothed angles. With a fast turn, the smoothing takes the short way
    // around and the body stays behind (the legs twist). These angles get the same turn as the board.
    static System.Reflection.FieldInfo smoothParent, smoothBody, smoothPos, smoothPosVel;
    // the drawn body also follows a smoothed point above the board (in world space): it gets the same turn around the pivot
    void TurnSmoothedPos(Quaternion q, Vector3 pivot)
    {
        const System.Reflection.BindingFlags B = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        if (smoothPos == null) smoothPos = typeof(SkatePlayer).GetField("skaterBodyParentSmoothPos", B);
        if (smoothPosVel == null) smoothPosVel = typeof(SkatePlayer).GetField("skaterBodyParentSmoothPosVelocity", B);
        try
        {
            if (smoothPos != null) { Vector3 p = (Vector3)smoothPos.GetValue(sp); smoothPos.SetValue(sp, pivot + q * (p - pivot)); }
            if (smoothPosVel != null) { Vector3 v = (Vector3)smoothPosVel.GetValue(sp); smoothPosVel.SetValue(sp, q * v); }
        }
        catch (Exception) { }
    }
    void TurnSmoothed(Quaternion worldDelta)
    {
        const System.Reflection.BindingFlags B = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        if (smoothParent == null) smoothParent = typeof(SkatePlayer).GetField("_skaterBodyParentSmoothRot", B);
        if (smoothBody == null) smoothBody = typeof(SkatePlayer).GetField("skaterBodySmoothRot", B);
        try
        {
            if (smoothParent != null) smoothParent.SetValue(sp, worldDelta * (Quaternion)smoothParent.GetValue(sp));
            if (smoothBody != null) smoothBody.SetValue(sp, worldDelta * (Quaternion)smoothBody.GetValue(sp));
        }
        catch (Exception) { }
    }
    void TurnLocalYaw(float deg)
    {
        Quaternion q = Quaternion.Euler(0f, deg, 0f);
        Quaternion r0 = sp.transform.rotation;
        TurnSmoothed(r0 * q * Quaternion.Inverse(r0));
        TurnSmoothedPos(r0 * q * Quaternion.Inverse(r0), sp.transform.position);
        sp.transform.rotation = sp.transform.rotation * q;
        SkateInterpolatedTransform s = Sit();
        if (s != null) { TransformState c = s.current; c.rot = (Quaternion)c.rot * q; s.current = c; TransformState l = s.last; l.rot = (Quaternion)l.rot * q; s.last = l; }
    }
    void TurnWorldYaw(float deg)
    {
        Quaternion q = Quaternion.AngleAxis(deg, Vector3.up);
        TurnSmoothed(q);
        TurnSmoothedPos(q, sp.transform.position);
        sp.transform.rotation = q * sp.transform.rotation;
        SkateInterpolatedTransform s = Sit();
        if (s != null)
        {   // the saved pose is local to the parent
            Transform p = sp.transform.parent;
            Quaternion ql = p != null ? Quaternion.Inverse(p.rotation) * q * p.rotation : q;
            TransformState c = s.current; c.rot = ql * (Quaternion)c.rot; s.current = c;
            TransformState l = s.last; l.rot = ql * (Quaternion)l.rot; s.last = l;
        }
    }
    void st_turn(float d) { sp.skState.degreesTurnedInAir += d; sp.skState.degreesTurnedInAir_abs += Mathf.Abs(d); }
    void Landed()
    {
        flipping = false; pendingDir = 0f;
        spinAdded = 0f; measured = 0f; haveYaw = false;
        Vector3 tgt = sp.stance.StanceRotationTarget;
        // take full turns out of the flip angle, in the target and in the shown angle, so the skater does not turn back
        float k = Mathf.Round(tgt.x / 360f) * 360f;
        if (k != 0f)
        {
            tgt.x -= k;
            try
            {
                if (stanceNow == null) stanceNow = typeof(SkatePlayer).GetField("<skateAnimatorStanceRotation>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (stanceNow != null) { Vector3 now = (Vector3)stanceNow.GetValue(sp); now.x -= k; stanceNow.SetValue(sp, now); }
            }
            catch (Exception) { }
        }
        sp.stance.StanceRotationTarget = tgt;
    }
    // is the ground close below the skater, at this fall speed?
    bool GroundSoon()
    {
        Vector3 v = sp.skPhysics.velocity;
        if (v.y > 0f) return false;
        float reach = 0.8f + Mathf.Max(0f, -v.y) * 0.3f;
        Vector3 o = sp.transform.position + Vector3.up * 0.3f;
        int n = UnityEngine.Physics.RaycastNonAlloc(o, Vector3.down, groundHits, reach + 0.3f, ~0, QueryTriggerInteraction.Ignore);
        Transform me = ThePlayer.instance != null ? ThePlayer.instance.transform : null;
        for (int i = 0; i < n; i++)
            if (groundHits[i].collider != null && (me == null || !groundHits[i].collider.transform.IsChildOf(me))) return true;
        return false;
    }
    static RaycastHit[] groundHits = new RaycastHit[16];

    // ---------- jump height ----------
    // The game sets lastPopForce at the moment it adds the jump force to the speed. A new value means a new jump.
    void Jump()
    {
        if (sp == null || sp.skState == null) { popSeen = false; return; }
        Vector3 f = sp.skState.lastPopForce;
        if (!popSeen) { lastPop = f; popSeen = true; return; }
        if (f.x == lastPop.x && f.y == lastPop.y && f.z == lastPop.z) return;
        lastPop = f;
        if (!jumpOn || jumpMul == 1f || f.y <= 0.05f) return;
        Vector3 v = sp.skPhysics.velocity + f * (jumpMul - 1f);
        sp.skPhysics.velocity = v;
        lastVel = v;
    }

    // ---------- health ----------
    void Health()
    {
        // Fragile mode: the game sets the full health to 1. The flag is not changed (it also sets the look of the body).
        // The editor keeps its own health number (normal size) and gives it to the game each frame. A hit lowers it,
        // and the game kills the skater only when it reaches 0.
        FragileHealth();
        // An older version of the editor took the flag out of the game's memory: put it back.
        try
        {
            SkateGameData d = SkateSingleton.data;
            if (d != null && d.flags != null && d.flags.Contains(Fragile) && !d.skull_fragile)
            {
                if (flagSetField == null) flagSetField = typeof(SkateGameData).GetField("flags_hashset", BindingFlags.NonPublic | BindingFlags.Instance);
                HashSet<string> hs = flagSetField != null ? flagSetField.GetValue(d) as HashSet<string> : null;
                if (hs != null) hs.Add(Fragile);
            }
        }
        catch (Exception) { }
        // No damage: the game skips a hit that comes during its short safe time after a hit. That safe time is kept very long.
        PlayerHealthMan h = noDamage || damageTouched ? HealthMan() : null;
        if (noDamage && h != null) { h.invulnerableDelay = 1e9f; damageTouched = true; }
        else if (!noDamage && damageTouched) { if (h != null) h.invulnerableDelay = 0.2f; damageTouched = false; }
        // No bails: the game's own god mode.
        if (noBails) { if (savedGod < 0) savedGod = SkateSingleton.GODMODE; SkateSingleton.GODMODE = 1; }
        else if (savedGod >= 0 && savedGodFlip < 0) { SkateSingleton.GODMODE = savedGod; savedGod = -1; }
    }
    int myHp = -1, lastSet = -1; float lastHitSeen = -1f;
    void FragileHealth()
    {
        PlayerHealthMan h = HealthMan();
        bool frag = false;
        try { SkateGameData d = SkateSingleton.data; frag = d != null && d.skull_fragile; } catch (Exception) { }
        if (!noFragile || !frag || h == null)
        {
            if (lastSet > 0 && h != null && h.currentHealth > h.currentHealthMax) h.currentHealth = h.currentHealthMax;
            myHp = -1; lastSet = -1;
            return;
        }
        float mul = 1f;
        try { mul = SkateSingleton.PLAYERHEALTH_MULTIPLIER; } catch (Exception) { }
        int full = Mathf.Max(1, Mathf.RoundToInt(100f * mul));
        int c = h.currentHealth;
        float hit = h.lastDamageTime;
        if (myHp < 0) myHp = full;
        else if (hit != lastHitSeen) myHp -= Mathf.Max(0, lastSet - c);     // a hit since the last frame
        else if (c != lastSet) myHp = full;                                  // the game healed or put the skater back
        lastHitSeen = hit;
        if (h.GetTimeSinceLastDamage() > 6f) myHp = full;                    // back to full health after 6 seconds without a hit
        if (myHp <= 0) myHp = full;                                          // the skater broke: the next life starts full
        h.currentHealth = myHp;
        lastSet = myHp;
    }
    static PlayerHealthMan HealthMan()
    {
        try { SkateSingleton m = SkateSingleton.main; return m != null ? m.PlayerHealthMan : null; } catch (Exception) { return null; }
    }
    public bool FragileNow()
    {
        try { SkateGameData d = SkateSingleton.data; return d != null && d.flags != null && d.flags.Contains(Fragile); } catch (Exception) { return false; }
    }

    public void Sticky()
    {
        if (!stickyOn || sp == null) return;
        SkatePlayer.SkimGrindState g = sp.skimGrindState;
        if (g == null || !g.inGrind) return;
        g.getOffLedgeTimer = 0f;                              // steering sideways or going slow does not end the grind
        Vector3 f = g.grindForward;
        if (minGrindSpeed <= 0f || f.sqrMagnitude < 0.0001f) return;
        f.Normalize();
        Vector3 v = sp.skPhysics.velocity;
        float along = Vector3.Dot(v, f);
        if (Mathf.Abs(along) >= minGrindSpeed) return;
        float dir = along < 0f ? -1f : 1f;
        if (Mathf.Abs(along) < 0.05f) dir = Vector3.Dot(sp.velocityTransformForward, f) < 0f ? -1f : 1f;
        sp.skPhysics.velocity = v + f * (dir * minGrindSpeed - along);
        lastVel = sp.skPhysics.velocity;
    }

    void Speeds()
    {
        float dt = Time.deltaTime;
        if (pushWindow > 0f) pushWindow -= dt;
        if (sp == null || !sp.isActiveAndEnabled) { haveLast = false; return; }
        Vector3 v = sp.skPhysics.velocity;
        if (!haveLast) { lastVel = v; haveLast = true; return; }
        bool air = false, slide = false;
        try { air = ThePlayer.IS_AIRBORNE; slide = ThePlayer.IS_POWERSLIDE; } catch (Exception) { }
        Vector3 h = new Vector3(v.x, 0f, v.z), lh = new Vector3(lastVel.x, 0f, lastVel.z);
        float gain = h.magnitude - lh.magnitude;
        float mul = 1f;
        if (!air && gain > 0f && h.sqrMagnitude > 0.01f && pushOn && pushWindow > 0f && gain > 0.05f && !slide) mul = pushMul;
        if (mul != 1f)
        {   // add (or take away) the extra part of this frame's speed gain, in the direction of travel
            Vector3 extra = h.normalized * (gain * (mul - 1f));
            v += extra;
            sp.skPhysics.velocity = v;
        }
        lastVel = v;
        SlideBoost(slide && !air, dt);
    }

    // The same boost as one push of the game: speed below 30 % gets the most, high speed gets little.
    float PushBoost()
    {
        float t = sp.speedT;
        if (t < 0.3f) return t + 3f;
        if (t < 1f) return t + 0.75f;
        if (t < 2f) return 1f;
        return 0.1f;
    }
    void SlideBoost(bool sliding, float dt)
    {
        if (!slideOn) { wasSliding = sliding; return; }
        float now = Time.time;
        bool give = false;
        if (sliding && !wasSliding) slideHold = Flat(sp.skPhysics.velocity).magnitude;   // the speed at the start of the slide
        if (sliding) slideTime += dt;
        if (slideWhen == 0 && sliding && !wasSliding) give = true;
        if (slideWhen == 1 && !sliding && wasSliding && slideTime >= 0.15f) give = true;
        if (!sliding) slideTime = 0f;
        wasSliding = sliding;
        if (sliding && slideKeep) HoldSpeed();
        if (!give || now - lastSlideBoost < 0.5f) return;
        bool air = false;
        try { air = ThePlayer.IS_AIRBORNE; } catch (Exception) { }
        if (air) return;
        Vector3 v = sp.skPhysics.velocity;
        if (new Vector3(v.x, 0f, v.z).sqrMagnitude < 0.04f) return;     // no direction to boost in
        sp.SpeedBoost(PushBoost() * slideMul);
        lastSlideBoost = now;
        lastVel = sp.skPhysics.velocity;
        if (sliding) slideHold = Flat(lastVel).magnitude;     // a boost at the start: the boosted speed is kept through the slide
    }
    static Vector3 Flat(Vector3 v) { return new Vector3(v.x, 0f, v.z); }
    // The slide brakes the skater. This puts the speed back to the speed at the start of the slide,
    // in the current direction of travel. Speed gains (for example downhill) stay.
    void HoldSpeed()
    {
        Vector3 v = sp.skPhysics.velocity, h = Flat(v);
        float m = h.magnitude;
        if (m < 0.2f || m >= slideHold) { if (m > slideHold) slideHold = m; return; }
        Vector3 nh = h / m * slideHold;
        v.x = nh.x; v.z = nh.z;
        sp.skPhysics.velocity = v;
        lastVel = v;
    }

    void GameSpeed()
    {
        if (Input.GetKeyDown(slowKey)) { slowOn = !slowOn; status = slowOn ? "Slow motion on (F7 to turn it off)." : "Slow motion off."; }
        // both can be on: the full-game speed, times the trick speed in the air and in tricks
        float want = slowOn ? slowSpeed : 1f;
        if (trickSlowOn)
        {
            bool trick = false;
            try { trick = ThePlayer.AirborneOrTricking; } catch (Exception) { }
            if (trick) want *= trickSlowSpeed;
        }
        // a short smooth change, not a jump
        curScale = Mathf.MoveTowards(curScale, want, Time.unscaledDeltaTime * 4f);
        TimeMan tm = null;
        try { tm = TimeMan.main; } catch (Exception) { }
        if (tm == null) return;
        // while a speed setting is on, the mod sets the game's speed value each frame. Game menus (photo mode, the debug
        // menu) also write this value; before, a value they left behind stayed when the mod's speed was back at 1.
        bool modOn = slowOn || trickSlowOn;
        if (modOn || curScale != 1f || speedTouched)
        {
            tm.debugGameSpeed = curScale;
            speedTouched = modOn || curScale != 1f;
        }
        // the mod's own freeze value (used only by the replay): never left on after a replay
        if (!ReplayTools.Active && !EditorTools.Editing && tm.uiPauseTimeScale != 1f) tm.uiPauseTimeScale = 1f;
        speedNow = Time.timeScale;
        otherSlow = "";
        if (Mathf.Abs(tm.lineChallengeTimeScale - 1f) > 0.01f) otherSlow += "  line challenge " + tm.lineChallengeTimeScale.ToString("0.00") + "x";
        if (Mathf.Abs(tm.physicDebugTimeScale - 1f) > 0.01f) otherSlow += "  physics debug " + tm.physicDebugTimeScale.ToString("0.00") + "x";
        if (Mathf.Abs(tm.dramaTimeScale - 1f) > 0.01f) otherSlow += "  scene " + tm.dramaTimeScale.ToString("0.00") + "x";
    }

    // ---------- menu ----------
    Rect Area(float w, float h) { return GUILayoutUtility.GetRect(w, h, GUI.skin.label, GUILayout.Width(w), GUILayout.Height(h)); }
    float Slider(string label, float v, float min, float max, string fmt, string unit)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(180));
        Rect r = Area(200, 20); r.y += 5;
        float n = GUI.HorizontalSlider(r, v, min, max);
        float stepN = max - min <= 1.01f ? 100f : 20f;              // short ranges: 0.01 steps, long ones: 0.05
        n = Mathf.Round(n * stepN) / stepN;
        GUILayout.Label(n.ToString(fmt, CultureInfo.InvariantCulture) + unit, GUILayout.Width(70));
        GUILayout.EndHorizontal();
        if (n != v) dirty = true;
        return n;
    }
    bool Toggle(bool v, string label)
    {
        bool n = GUILayout.Toggle(v, label);
        if (n != v) dirty = true;
        return n;
    }

    public void Draw()
    {
        if (status.Length > 0) GUILayout.Label(status);
        if (sp == null) GUILayout.Label("The skater is not loaded yet. The speed changes start when you ride the board.");
        GUILayout.Space(6);
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(600));
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(480));
        GUILayout.Label("SPEED");
        pushOn = Toggle(pushOn, " Change the push power");
        if (pushOn) pushMul = Slider("   Speed from each push", pushMul, 0.25f, 4f, "0.00", "x");
        GUILayout.Label("1x = the game's normal push.");
        GUILayout.Space(6);
        slideOn = Toggle(slideOn, " Power slides give a speed boost, like a push");
        if (slideOn)
        {
            slideMul = Slider("   Boost strength", slideMul, 0.25f, 4f, "0.00", "x push");
            GUILayout.BeginHorizontal();
            GUILayout.Label("   Boost", GUILayout.Width(180));
            if (GUILayout.Toggle(slideWhen == 0, "When the slide starts", GUI.skin.button, GUILayout.Width(140)) && slideWhen != 0) { slideWhen = 0; dirty = true; }
            if (GUILayout.Toggle(slideWhen == 1, "When the slide ends", GUI.skin.button, GUILayout.Width(140)) && slideWhen != 1) { slideWhen = 1; dirty = true; }
            GUILayout.EndHorizontal();
            GUILayout.Label("   Like a push, the boost is big at low speed and small at top speed.");
            slideKeep = Toggle(slideKeep, " No speed loss during the slide (the speed carries through and after it)");
        }

        GUILayout.Space(12);
        GUILayout.Label("JUMP");
        jumpOn = Toggle(jumpOn, " Change the jump height");
        if (jumpOn) jumpMul = Slider("   Jump power", jumpMul, 0.5f, 4f, "0.00", "x");
        GUILayout.Label("1x = the game's normal jump. The jump goes higher, and you are in the air longer.");

        GUILayout.Space(12);
        GUILayout.Label("HEALTH");
        GUILayout.Label(FragileNow() ? "Your save has the fragile mode: one hit kills you." : "Your save does not have the fragile mode.");
        noFragile = Toggle(noFragile, " Normal health in the fragile mode (the look and the save do not change)");
        noDamage = Toggle(noDamage, " No damage from hits and hard landings");
        noBails = Toggle(noBails, " No bails: you do not crash, fall or break, and all tricks land");
        GUILayout.Label("Falling out of the level still sends you back, as in the normal game.");
        GUILayout.EndVertical();
        GUILayout.Space(20);
        GUILayout.BeginVertical(GUILayout.Width(480));
        GUILayout.Label("AIR TRICKS");
        spinOn = Toggle(spinOn, " Keep spinning for the full jump (spin past 360)");
        if (spinOn)
        {
            spinMul = Slider("   Spin speed", spinMul, 0.25f, 3f, "0.00", "x");
            spinPose = Toggle(spinPose, "   Tuck and grab while spinning (uses the flip Pose settings)");
        }
        flipOn = Toggle(flipOn, " Flips: in the air, hold two buttons together");
        if (flipOn) DrawFlipMenu();
        DrawTrickMenu();
        bool lg = GUILayout.Toggle(spinLog, " Test log for spins (writes ShoeMod/spinlog.txt, not saved)");
        if (lg != spinLog) { spinLog = lg; logLines = 0; }

        GUILayout.Space(12);
        GUILayout.Label("GRINDS");
        snapOn = Toggle(snapOn, " Snap onto rails: in the air above a rail, you are pulled onto it");
        if (snapOn) snapRadius = Slider("   Snap distance", snapRadius, 0.25f, 2f, "0.00", " m");
        stickyOn = Toggle(stickyOn, " Stay on the rail until you jump off (steering and slow speed do not end it)");
        if (stickyOn) minGrindSpeed = Slider("   Minimum grind speed", minGrindSpeed, 0f, 10f, "0.0", " m/s");
        GUILayout.Label("A grind still ends at the end of the rail. With a long snap distance, the board can also catch edges you did not aim for.");

        GUILayout.Space(12);
        vibMax = Slider("Controller vibration", vibMax, 0f, 1f, "0.00", "");
        GUILayout.Space(8);
        GUILayout.Label("GAME SPEED");
        bool so = GUILayout.Toggle(slowOn, " Slow motion for the full game   (F7 turns it on and off while you play)");
        if (so != slowOn) { slowOn = so; status = ""; }
        slowSpeed = Slider("   Game speed", slowSpeed, 0.1f, 2f, "0.00", "x");
        trickSlowOn = Toggle(trickSlowOn, " Slow motion only in the air and during tricks");
        if (trickSlowOn) trickSlowSpeed = Slider("   Game speed in tricks", trickSlowSpeed, 0.1f, 1f, "0.00", "x");
        if (slowOn && trickSlowOn) GUILayout.Label("   Both are on: in tricks the speed is " + (slowSpeed * trickSlowSpeed).ToString("0.00") + "x.");
        GUILayout.BeginHorizontal();
        GUILayout.Label("   Speed now: " + speedNow.ToString("0.00") + "x" + (otherSlow.Length > 0 ? "   Game values not at 1:" + otherSlow : ""), GUILayout.Width(330));
        if (GUILayout.Button("Reset speed", GUILayout.Width(100))) ResetGameSpeed();
        GUILayout.EndHorizontal();
        GUILayout.Label("Below 1x is slow motion. Above 1x is faster than normal. Sounds change speed with the game.");

        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.Space(12);
        GUILayout.EndScrollView();
        if (GUILayout.Button("All movement back to the game settings", GUILayout.Width(300)))
        { pushOn = slideOn = slowOn = trickSlowOn = snapOn = stickyOn = jumpOn = noFragile = noDamage = noBails = spinOn = flipOn = false; dirty = true; status = "Movement is back to the game settings."; }
    }
}

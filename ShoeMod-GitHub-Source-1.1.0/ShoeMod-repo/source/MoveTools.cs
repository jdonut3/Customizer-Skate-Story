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
// The game speed uses the game's own speed value (TimeMan.debugGameSpeed), so sounds slow down with it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

public class MoveTools
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
    Vector3 lastPop; bool popSeen;
    bool noFragile, noDamage, noBails;
    int savedGod = -1; bool damageTouched;
    const string Fragile = "dontclear-skull-fragile";
    static FieldInfo flagSetField;

    SkatePlayer sp; float nextFind;
    float pushWindow;                              // seconds after a push in which speed gains count as push speed
    Vector3 lastVel; bool haveLast;
    float curScale = 1f; bool speedTouched;

    // ---------- settings file ----------
    static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
    static float P(string s, float d) { float v; return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : d; }
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
        if (f.Length > 17) { jumpOn = f[13] == "1"; jumpMul = Mathf.Clamp(P(f[14], 1.5f), 0.5f, 4f); noFragile = f[15] == "1"; noDamage = f[16] == "1"; noBails = f[17] == "1"; }
        if (f.Length > 12) { snapOn = f[9] == "1"; snapRadius = Mathf.Clamp(P(f[10], 1f), 0.25f, 2f); stickyOn = f[11] == "1"; minGrindSpeed = P(f[12], 3f); }     // an older line: its slide value had a different meaning
        // the full-game slow motion is not saved: the game always starts at normal speed
    }
    public void Write(StringBuilder sb)
    {
        sb.Append("move=").Append(pushOn ? "1" : "0").Append(',').Append(F(pushMul)).Append(',').Append(slideOn ? "1" : "0").Append(',').Append(F(slideMul))
          .Append(',').Append(F(slowSpeed)).Append(',').Append(trickSlowOn ? "1" : "0").Append(',').Append(F(trickSlowSpeed)).Append(',').Append(slideWhen).Append(',').Append(slideKeep ? "1" : "0")
          .Append(',').Append(snapOn ? "1" : "0").Append(',').Append(F(snapRadius)).Append(',').Append(stickyOn ? "1" : "0").Append(',').Append(F(minGrindSpeed))
          .Append(',').Append(jumpOn ? "1" : "0").Append(',').Append(F(jumpMul)).Append(',').Append(noFragile ? "1" : "0").Append(',').Append(noDamage ? "1" : "0").Append(',').Append(noBails ? "1" : "0").Append('\n');
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
        try
        {
            FindPlayer(now);
            Snap();
            Sticky();
            Jump();
            Health();
            Speeds();
            GameSpeed();
        }
        catch (Exception e) { status = "Movement error: " + e.Message; }
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
        try { Sticky(); Jump(); } catch (Exception) { }
    }

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
        else if (savedGod >= 0) { SkateSingleton.GODMODE = savedGod; savedGod = -1; }
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
        float want = 1f;
        if (slowOn) want = slowSpeed;
        else if (trickSlowOn)
        {
            bool trick = false;
            try { trick = ThePlayer.AirborneOrTricking; } catch (Exception) { }
            if (trick) want = trickSlowSpeed;
        }
        // a short smooth change, not a jump
        curScale = Mathf.MoveTowards(curScale, want, Time.unscaledDeltaTime * 4f);
        TimeMan tm = null;
        try { tm = TimeMan.main; } catch (Exception) { }
        if (tm == null) return;
        if (curScale != 1f || speedTouched)
        {
            tm.debugGameSpeed = curScale;
            speedTouched = curScale != 1f;
        }
    }

    // ---------- menu ----------
    Rect Area(float w, float h) { return GUILayoutUtility.GetRect(w, h, GUI.skin.label, GUILayout.Width(w), GUILayout.Height(h)); }
    float Slider(string label, float v, float min, float max, string fmt, string unit)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(180));
        Rect r = Area(200, 20); r.y += 5;
        float n = GUI.HorizontalSlider(r, v, min, max);
        n = Mathf.Round(n * 20f) / 20f;
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
        GUILayout.Label("GRINDS");
        snapOn = Toggle(snapOn, " Snap onto rails: in the air above a rail, you are pulled onto it");
        if (snapOn) snapRadius = Slider("   Snap distance", snapRadius, 0.25f, 2f, "0.00", " m");
        stickyOn = Toggle(stickyOn, " Stay on the rail until you jump off (steering and slow speed do not end it)");
        if (stickyOn) minGrindSpeed = Slider("   Minimum grind speed", minGrindSpeed, 0f, 10f, "0.0", " m/s");
        GUILayout.Label("A grind still ends at the end of the rail. With a long snap distance, the board can also catch edges you did not aim for.");

        GUILayout.Space(12);
        GUILayout.Label("GAME SPEED");
        bool so = GUILayout.Toggle(slowOn, " Slow motion for the full game   (F7 turns it on and off while you play)");
        if (so != slowOn) { slowOn = so; status = ""; }
        slowSpeed = Slider("   Game speed", slowSpeed, 0.1f, 2f, "0.00", "x");
        trickSlowOn = Toggle(trickSlowOn, " Slow motion only in the air and during tricks");
        if (trickSlowOn) trickSlowSpeed = Slider("   Game speed in tricks", trickSlowSpeed, 0.1f, 1f, "0.00", "x");
        GUILayout.Label("Below 1x is slow motion. Above 1x is faster than normal. Sounds change speed with the game.");

        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.Space(12);
        if (GUILayout.Button("All movement back to the game settings", GUILayout.Width(300)))
        { pushOn = slideOn = slowOn = trickSlowOn = snapOn = stickyOn = jumpOn = noFragile = noDamage = noBails = false; dirty = true; status = "Movement is back to the game settings."; }
    }
}

---
name: monogame-weapon-combat
description: Per-weapon combat for procedurally animated characters in MonoGame — drawn-weapon stances as a pose overlay (sword+shield guard, two-handed axe grip with off-hand IK, dagger crouch, staff two-hand plant, bow nocked low), weapon-specific movement modifiers, three attacks per weapon (light/heavy/special on Q/R/C) built from curve helpers (Bump, Ramp, Whip with peak velocity at 65 %), trunk-driven power, root-motion steps, hit moments, cancel windows and input buffering for chaining, a bow "present" rotation, and the animation-research numbers behind them. Includes the NaN trap (Pow of a slightly-negative float poisons the whole skeleton) and a playtest method for judging attacks. Use when adding melee/ranged attacks, combat stances or weapon-specific movement to a code-animated character.
---

# Weapon stances, movement and attacks (code-driven)

Built for five characters: Knight (sword + shield), Barbarian (two-handed axe), Rogue (dual daggers),
Mage (staff), Ranger (bow). No animation assets; everything is curves over a normalised attack time `u`.

## 1. Research numbers that drove the design
From game-animation practice ([MoCap Online melee guide](https://mocaponline.com/blogs/mocap-news/sword-melee-animation-guide),
[weapon systems guide](https://mocaponline.com/blogs/mocap-news/weapon-animation-systems-guide)):
* Light attack: anticipation 0.1–0.2 s, swing 0.2–0.4 s, whole cycle 0.6–1.2 s. Heavy: wind-up 0.3–0.45 s,
  swing 0.4–0.8 s, cycle 1.5–2.6 s. Recovery is the attack's *cost*; a chain may start at ~70 % of the cycle.
* Peak weapon velocity at 60–70 % through the swing arc; the body keeps rotating past the arm, then re-centres.
* Power comes from the trunk: hips lead, shoulders follow. Two-handed weapons never release the grip.
* Dual wield alternates hands; the idle blade stays in guard, never hangs. Combos finish with both.
* Each weapon has its own idle: rapier point-forward, greatsword resting, axe at the hip, etc. The *same input*
  produces different body mechanics per weapon weight/reach/grip.
* Bows: nock → draw → hold → release ("explosive": string hand flies back) → watch the arrow.
  Staves: sweeping telegraphs, thrust for bolts, wide circular wind-up for AOE, the tip is the VFX point.
* Drawn-weapon locomotion: guard stays up, shorter steps, strafing; the off-hand must not compete visually.

## 2. Architecture
* `Combat.Stance(weapon, amount, speed, t, writer, skeleton)` — an **overlay** the `AnimationPlayer` invokes
  after evaluating the current clip (and the previous one during a blend), so stances sit on top of idle /
  walk / run / the speed-blended Move clip. `amount` fades with `DrawBlend` and drops to 0 while attacking.
* `Combat.Locomotion(weapon)` → (speed multiplier, stride factor): sword 0.85, axe 0.8, daggers 1.05, staff 0.8.
* `AttackDef(Name, Duration, CancelFrom, HitAt, RootAdvance, Pose(u, writer, skeleton))`; `Combat.Get(weapon, kind)`.
* `Character.Attack(kind)`: auto-draws first (queued), buffers a second press, chains when
  `progress ≥ CancelFrom`, fires `AttackHit` once at `HitAt`, and exposes `AttackAdvance` — root motion spread
  over the strike window (hit − 12 % … hit + 8 %) that the controller applies along the facing. Input movement
  and turning are suppressed during an attack; blend into an attack is 0.08 s, out is 0.25 s.
* Hotkeys: Q light, R heavy, C special (R only resets the camera in the overview).

## 3. Curve helpers (all in u ∈ [0,1])
* `Bump(u, a, peak, b)` smooth 0→1→0; `Ramp(u, a, b)` smoothstep held at 1;
  `Whip(u, a, b)`: `x<0.65 ? 0.5·(x/0.65)^2.2 : 0.5 + 0.5·(1 − (1 − (x−0.65)/0.35)^1.8)` — slow wind, fast strike.
* A strike is `s = Whip(...) * (1 − Ramp(recovery))`; wind-ups are Bumps or Ramps that die before the strike.
* Shared `Trunk(twist, lean, tilt)` distributes over hips/spine/chest (0.45/0.35/0.4 twist) with the neck/head
  counter-rotating so the gaze stays on target; `Legs(step, crouch)` gives the lunge/crouch footwork.
* Two-handed grips: `ArmIK(side, point-on-weapon)` every frame from the **weapon bone's current world matrix**
  (`w.PositionOf("weaponR") + TransformNormal(localOffset, w.WorldOf(weaponBone))`), so the off-hand rides the haft.

## 4. Per-weapon recipes (what made each read)
* **Sword + shield**: stance = sword low at the hip (arm 18°/18°, forearm 55°), shield arm raised across the
  chest (38°/42°, forearm 95°), chest 14° twist, head −16° back to square. Cut: trunk −28° wind → +38° through,
  wrist snap −35° twist at the strike. Overhead: arm raised to 150° behind, 22° forward lean, 8 cm drop.
  Shield bash: the *left* arm punches (60°) with a −30° trunk twist and a left-foot lunge.
* **Two-handed axe**: stance holds the axe across the waist; off-hand IK at −0.38 m along the haft. Sweep:
  −45° wind → +55° swing with a +12° overshoot bump. Smash: arm to 165° overhead, 30° lean, 12 cm drop, 1.5 s.
  Whirlwind: trunk-only (the root yaw is not spun — a 360° body turn without root motion read as a glitch).
* **Daggers**: stance is a 6 cm crouch, hips 6°, spine 10°, elbows out, blades reversed (hand twist −25°),
  breathing bob. Stabs alternate R then L (Bumps offset 0.3); Lunge does both with 0.9 m root advance; Flurry is
  R-L-R then both. The non-stabbing arm stays in the guard pose (`DaggerGuard`).
* **Staff**: two-hand IK at −0.45 m; Bolt = short wind then thrust with the free hand opening toward the target;
  Nova = a 1.6 s circular raise (`circle` phase drives twist/tilt) ending in a slam; Channel = a hold with a
  90 Hz micro-shake. `HitAt` marks the VFX moment.
* **Bow**: one pose function `BowPose(draw, raise)` — bow arm 20°→80° forward as it raises, string hand to the
  cheek (arm 10→60°, forearm 120→115°), chest 18→32° side-on, head −22→−34°. Release = a Bump on the string arm
  (+25° back) right after draw ends. Full draw adds a strain tremor (140 Hz, 1.2°). Volley = three short draws.
  **Present the bow**: the mesh is built hanging along the hand's −Y, so it lies along an extended arm; rotate
  the weapon bone −35° (stance) → −90° (arm raised) about its local X so the limbs stand vertical.

## 4b. Author attacks as hand paths, not arm angles (the fix for "janky")
The first version drove arm/forearm angles per attack. It looked wrong in every way that matters: the weapon
went behind the back, two-handed grips separated, swings had no arc. Swing arcs are what the *hand* does, so:
* `PoseWriter.WeaponIK(side, wristTarget, elbowHint, weaponDir, edgeDir, weight)` — ArmIK for the wrist, then
  the hand rotation is solved so the weapon bone's axis lies along `weaponDir` with its edge toward `edgeDir`
  (build two orthonormal bases and map one onto the other, then strip the hand's BindRotation).
* `PoseWriter.WeaponPoint(side, alongAxis)` returns a point on the weapon in character space — the off-hand of a
  two-handed weapon is `ArmIK(1, WeaponPoint(-1, 0.42))` **every frame**, so the grip never separates.
* An attack is then: a Catmull-Rom **path** of wrist positions + a path of weapon directions, `SwingArc(...)`
  = rest → path[0] over the wind-up, Whip along the path, path[^1] → rest during recovery. Trunk and Legs use
  the same phase values. ~12 numbers per attack, all in metres you can picture.
* **Measure the weapon axis before trusting it.** A `--wdir x,y,z` probe that poses the stance with a requested
  direction and prints the weapon bone's world ±Y proved the IK was right while the picture was "wrong": the
  axe *head* sits at the bone's −Y end. One sign flip. Also view probes from the front — from behind, "forward"
  hides the head behind the body and reads as a bug.
* Parallel edge/axis (asking for "up" with edge "up") made the basis degenerate → NaN; fall back to a cross with
  Backward when the cross with Up vanishes.

## 5. Gotchas (the expensive ones)
* **NaN poisons everything.** `MathF.Pow(1 − (x−0.65)/0.35, 1.8)` at x = 1 sees a *slightly negative* base
  (rounding) → NaN → every bone NaN → the character silently vanishes. Clamp the base. Keep a canary in the
  playtest log (`float.IsNaN(head.X)`) — it found the bug in one run; the sheet just showed an empty frame.
* Keep the follow-through spring safe too: renormalising a near-zero quaternion is NaN; snap to the target.
* A camera yaw that puts another character between lens and subject looks exactly like a broken pose. Use
  `cam` angles from the open side, or `--no-buildings --trees 0` and a known-clear yaw.
* Overview "Attack" mode: trigger a fresh attack whenever the previous one finishes, cycling light/heavy/special.
* Blend out of an attack at 0.25 s, not 0.4 s: the old cross-fade made a heavy's recovery look like a slump.

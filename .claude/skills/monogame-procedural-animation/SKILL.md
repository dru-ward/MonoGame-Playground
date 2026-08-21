---
name: monogame-procedural-animation
description: Code-driven skeletal animation for any rigged character (MonoGame or other engines) — pose/clip model with axis-convention helpers, mocap-style gait tables for walk/run, cyclic Catmull-Rom keyframes, C1-smooth curves, slerp cross-fades, a per-bone damped-spring follow-through layer, two-bone analytic arm IK, one-shot/looping/queued action state machine, weapon draw/sheathe via sockets with blended re-attachment, and third-person control with speed matched to stride. Use when a game needs believable character motion without animation assets, or to layer polish (IK, follow-through, blending) on top of existing clips.
---

# Procedural character animation

Methods that made code-generated motion read as fluid. All engine-agnostic; code is MonoGame C#.

## Pose model
* `Pose = Quaternion[] Rotations (local, per bone) + Vector3 RootOffset`. Clips are pure functions
  `(time, PoseWriter) -> void` that write into a pose; nothing is stored per frame.
* Hide axis conventions behind a writer so clips read like animator notes:
  * `Hang(bone, forward, outward, side, twist)` for limbs that point -Y in bind:
    `M = RotY(twist*side) * RotX(-forward) * RotZ(outward*side)` (row-vector order: twist about the bone's
    own axis first, then flex, then abduct). `side = +1` left, `-1` right flips abduction/twist.
  * `Upright(bone, lean, tilt, twist)` for spine/neck/head (+Y bones).
  * `Foot(bone, toeUp, side, roll, toeBend)` for +Z bones; drives the toe child too.
* Derive signs once by applying the matrix to the bind direction and write them in comments — every "arm bends
  the wrong way" bug came from a sign guess. E.g. for a -Y bone, `RotX(+θ)` swings it toward -Z (backwards).
* Build quaternions from matrices (`CreateFromRotationMatrix`) when composing several axes; `CreateFromYawPitchRoll`
  applies roll→pitch→yaw, which is not the order you want for "twist about own axis".

## Curves
* **Cyclic Catmull-Rom keyframes** `Key(u, (t,v)...)` with the last key duplicating the first at t=1. Continuous
  velocity through keys — smoothstep-between-keys produced visible stop-start at every key (the attack swing
  looked robotic until this changed).
* **Smooth max**: `Pos(x,k) = 0.5*(x + sqrt(x²+k²))` instead of `max(0,x)` anywhere a curve is gated by phase
  (knee bend during swing, toe-off, flight phase). `max` creates C0 kinks that read as hitches.
* `|cos|` style bounces → `0.5+0.5*cos(2φ)`; linear ramps → smoothstep.

## Gait (walk/run) as joint-angle tables
Drive each leg by stride phase `p ∈ [0,1)` (heel strike at 0, push-off ≈ 0.6), opposite leg at `p+0.5`, with
tables shaped like human gait data. Values in degrees:
```
Walk Hip   (0,24)(0.12,20)(0.3,6)(0.5,-11)(0.62,-9)(0.75,8)(0.88,21)(1,24)
Walk Knee  (0,6)(0.12,18)(0.3,8)(0.45,9)(0.55,32)(0.68,62)(0.8,44)(0.9,14)(1,6)
Walk Ankle (0,3)(0.08,-5)(0.3,5)(0.45,9)(0.56,-14)(0.66,-8)(0.82,4)(1,3)      (+ = toe up, relative to shin)
Walk Toe   (0,0)(0.4,6)(0.52,32)(0.62,14)(0.72,0)(1,0)
Run  Hip   (0,34)(0.15,26)(0.35,2)(0.5,-17)(0.6,-9)(0.75,22)(0.9,34)(1,34)
Run  Knee  (0,22)(0.12,42)(0.3,24)(0.45,38)(0.6,92)(0.72,108)(0.85,62)(1,22)
Run  Ankle (0,0)(0.1,-6)(0.3,9)(0.42,13)(0.52,-24)(0.62,-14)(0.8,2)(1,0)
```
Plus, per stride (`c1 = cos 2πu`, `s1 = sin 2πu`, `c2 = cos 4πu`):
* root bob `-0.5·Bob·c2` (lowest at double support, highest mid-stance; walk 0.022 m, run 0.05 m), lateral sway
  `Sway·s1` over the stance foot, run flight `Flight·Pos(-c2)`.
* pelvis yaw `-PelvisYaw·c1` (5–7°), pelvis drop toward the swing leg `-PelvisDrop·s1` (4–5°), spine/chest
  counter-rotation `+ShoulderYaw·c1` split across spine and chest, neck/head counter so the head stays level.
* forward lean: walk 2°, run 11°, split across spine/chest and fed 0.3× into hip flexion.
* arms: shoulder flexion `-(hip-6)·ArmSwing/24` (opposite the same-side leg), elbow = base + swing·Pos(forward)
  (walk 12+14, run 80+25).
* Rates: walk 1.25 Hz, run 2.2 Hz. Ground speed ≈ 2·(2·legLength·sin(stride))·Hz; tune clip Hz/stride and the
  controller speed together so feet don't slide (walk 1.5 m/s, run 4.4 m/s for a 0.86 m leg).
* Toe bones bending 30–40° at push-off are the single cheapest "alive" upgrade to a walk.

## Blending and follow-through
* Cross-fade: slerp previous→current over 0.4 s with **smootherstep** (`t³(t(6t-15)+10)`).
* Follow-through layer: after blending, run each *upper-body* bone through a damped second-order spring on the
  quaternion components (normalise after; fine for per-frame deltas):
  `acc = (target - x)·ω² - v·2ζω; v += acc·dt; x += v·dt`, ζ ≈ 0.72, clamp dt ≤ 1/30.
  ω = 2π·{hands 5.5, head 5, forearms 7, neck 7, upper arms 8.5, clavicles/chest/spine 9} Hz; legs ω = 0 so feet
  stay planted. Also spring the root offset (9 Hz). Extremities lag and settle — overlapping action for free — and
  clip transitions stop snapping. Too-low ω attenuates fast motions (a 2.5 Hz run with a 5 Hz hand spring keeps
  ~85 % amplitude; at 2 Hz it would halve).
* Prime the spring state on the first frame (copy target, zero velocity) or the first second explodes.

## Two-bone analytic IK (arms)
Inputs: shoulder (from parent world matrix under the pose so far), target (wrist), elbow hint, lengths l1/l2.
```
d = clamp(|T-S|, ε, l1+l2-ε); dir = (T-S)/|T-S|
a = acos((l1²+d²-l2²)/(2·l1·d))           // shoulder angle off the S→T line
flex = π - acos((l1²+l2²-d²)/(2·l1·l2))   // elbow flexion
perp = normalize(hint - dir·(hint·dir)); u = dir·cos a + perp·sin a      // upper-arm direction
elbow = S + u·l1; f = normalize(T - elbow)
upper-arm frame: Y = -u, Z = normalize(f - u(f·u)), X = Y×Z   → world matrix rows (X,Y,Z)
q_arm_local = FromMatrix(world · inverse(parentRotation)); q_fore_local = AxisAngle(X, -flex)
```
Needs `WorldOf(bone)` computed from the *pose being written* (walk up parents; depth ≤ 5 so recursion is cheap).
Blend by weight against FK rotations already written, so a reach can ramp in/out. Used for the wave (target beside
and above the head, wrist oscillating ±0.17 m in X, elbow hint outward-down) and for draw/sheathe reaches
(target = socket position from the same pose). Hand-authored FK for these looked like a salute or a broken elbow;
IK got them right first time once the target was placed correctly.

## Action state machine
* `Clip.Duration` 0 = loop, >0 = one-shot. Character holds `Locomotion` (idle/walk/run chosen from speed) and an
  optional `Action`; `Update` plays `Action ?? Locomotion`, expires one-shots by elapsed time and starts `Queued`.
* Movement cancels actions except the draw reach; pressing attack while holstered queues the attack behind the
  draw. `Play(clip, restart: true)` for one-shots so they start at t=0 regardless of the player's time offset.
* Give each character a `TimeOffset` so a line-up does not move in lockstep.

## Weapons, sockets, draw/sheathe
* Weapon mesh is rigid-weighted to a `weaponR/L` bone under the hand with a constant `BindRotation`
  (−58° about X turned the hanging bind-pose sword into a forward-down grip across the fist; staff/bow identity).
* Sheath sockets are extra bones under chest/hips with `BindRotation = RotX(180° flip?) · RotZ(tilt)` and an offset
  (sword/axe: chest + (−0.08, −0.02, −0.19), tilt −22°; daggers: hips ± (0.20, 0.03, 0.03), tilt ∓12°; staff/bow:
  chest diagonal −32°/+28°).
* Attachment after the pose update: `weapon.World = blend(socket.World, hand.World, DrawBlend)` (slerp rotation,
  lerp position) and rewrite that palette entry. `DrawBlend` eases toward `Drawn` at ~14/s; the `Drawn` flag flips
  at the midpoint of the reach clip so the hand is at the socket when the weapon changes parent — no popping.

## Third-person control
* Input is camera-relative on the ground plane: `fwd = (-sin yaw, 0, -cos yaw)`, `right = (-fwd.z, 0, fwd.x)`.
* Velocity eases toward `input·speed` with `1-exp(-dt·7)` (10 when stopping); yaw eases toward `atan2(v.x, v.z)`
  with `WrapAngle` and `1-exp(-dt·12)`.
* Locomotion clip from speed thresholds (idle < 0.2, run above the walk/run midpoint); camera target follows the
  character at 55 % height.
* Keep gameplay keys off whatever the viewer uses (move wireframe/debug toggles away from WASD).

## Reviewing motion without a video
Screenshots at several phases (`--warm 0.55/0.9/1.4`) from the side and 3/4 are enough to catch wrong signs,
hyper-extended joints and sliding; the first hand-authored wave and elbow-sign bug were both obvious in a single
frame. Verify each clip at least once from the side view where joint angles are unambiguous.

## Speed-driven locomotion (fixes "wobbles back and forth when moving")
Discrete Idle/Walk/Run clips selected by speed thresholds and cross-faded look fine in a demo but wobble under
player control: walk (1.25 Hz) and run (2.2 Hz) have different stride frequencies, so every cross-fade slerps
two out-of-phase cycles, and a smoothed velocity hovering near a threshold re-triggers the fade. Trunk springs
(spine/chest/clavicles with 9 Hz follow-through) then overshoot the counter-rotation every stride.

What fixed it:
* **One Move clip per character** parameterised by `Speed` (m/s, normalised by `Height / 1.8`):
  `amp = Smooth01(speed / 0.7)` blends Idle → gait, `r = Smooth01((speed − walkSpeed) / (runSpeed − walkSpeed))`
  blends the walk and run **gait definitions** (scalars lerped, joint curves evaluated in both tables and lerped)
  — never two clips at different phases.
* **One integrated stride phase**: `phase += dt × StrideHz(speed)` where `Hz = speed / strideLen`,
  `strideLen = lerp(1.2 m, 2.0 m, r)` (× 0.6…1 below walking pace), clamped 0.5–3.5 Hz. Feet stay planted at
  every speed and the cycle never jumps. When stopped, ease the phase to the nearest double-support (`round(phase·2)/2`).
* The Idle evaluation is written first, then `PoseWriter.BlendToward(locoPose, amp)` — a one-line pose blend.
* Springs only on arms, hands, head and neck (damping 0.82); trunk, pelvis and legs crisp.
* Gait tuning for a steady torso: sway 0.5 cm (was 1.2), pelvis drop 2.5° (was 4), spine tilt 0.6° (was 1.5),
  head pitch bob 0.5°. Measured head-vs-hips excursion over a run: lateral ~5 cm, fore/aft < 1 cm.
* Turn toward the **input** direction, not the smoothed velocity (which lags and makes the body hunt), with a
  9/s exponential approach capped at 540°/s.
* MonoGame fixed-step catch-up after a long load runs many `Update`s before the first `Draw`; headless tests
  that "hold a key" must assert it inside `Update`, not only in a warm-up loop, or the screenshot shows idle.

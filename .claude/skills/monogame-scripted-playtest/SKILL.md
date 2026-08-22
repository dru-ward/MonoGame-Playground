---
name: monogame-scripted-playtest
description: Automated play-testing of a MonoGame game from an agent/CLI with no one at the keyboard — a tiny input-script language ("w 1.5; w+shift 2; a+w 1; idle 1; q; cam 200 12 4; rain on") fed into Update as a synthetic KeyboardState so the real input code is exercised, non-key commands for camera/weather/focus, a PNG frame recorder with a contact-sheet stitcher for reviewing motion as one image, a per-frame CSV of gameplay metrics (position, speed, yaw, stride phase, head excursion, clip, allocations, CPU), and the bugs this found on first use (camera clipping through foliage, running on the spot against walls). Use when you need to verify movement, collision, camera or animation behaviour in motion, reproduce a gameplay bug deterministically, or build regression scenarios for a MonoGame game.
---

# Scripted play-testing for MonoGame

Single screenshots verify *looks*; they cannot verify *motion*: turning, acceleration, sliding along an
obstacle, a camera moving through a tree. You also cannot press keys into an SDL window from outside
(SendKeys/SendInput do not reach MonoGame DesktopGL reliably). The fix is to script input **inside** the game.

## 1. Input script → synthetic KeyboardState
```
--script "w 1.5; w+shift 2; a+w 1; idle 0.8; q; idle 1.4; rain on; wind 1.3; cam 200 12 4; s 1"
```
* `keys duration` holds a `+`-joined combination for *duration* seconds; no duration = a one-frame tap
  (so edge-triggered `Pressed()` logic fires exactly once). `idle t` holds nothing.
* Anything else is a command handled by the game when the step begins: `cam yaw pitch dist`, `wind s`,
  `rain on|off`, `focus n`, `light deg`, `shot label`, `deferred`, `forward`.
* The script replaces **only** `Keyboard.GetState()`:
  ```csharp
  var keys = _script != null ? _script.Advance(dt) : Keyboard.GetState();
  var mouse = _script != null ? _prevMouse : Mouse.GetState();   // scripts never move the mouse
  ```
  `new KeyboardState(params Keys[])` is public, so the rest of Update runs unchanged — the test exercises
  the real input → movement → collision → animation path.
* Advance in `Update` (fixed step → deterministic); the script's own clock is the sum of `dt`.
* Disable auto-orbit when a script is active; end the run ~0.3 s after the last step so the final state settles,
  save a `final` frame, print a one-line summary to stderr and `Exit()`.

## 2. Frame recorder + contact sheet
* Render into the same off-screen `RenderTarget2D` used for `--shot`; after the HUD, `SaveAsPng` every
  `--every` seconds into `--frames dir` as `f012_3.45s.png`. Saving costs a few ms, so Draw runs slower than
  Update (MonoGame catches up with extra fixed-step Updates) — the script time stays correct because it is
  advanced in Update, but per-frame logs sample at Draw rate.
* `tools/contact_sheet.py dir --cols 5 --width 400` crops the HUD bands, labels each frame with its time and
  tiles them into `sheet.png` (optionally `--gif`). One image = one playtest; review it like a flip-book.

## 3. Per-frame CSV (`--log file.csv`)
`t, step, x, z, speed, yaw_deg, stride_phase, head_lat_cm, head_fwd_cm, clip, alloc_bytes, cpu_ms` for the
controlled character. Numbers catch what eyes miss: a speed that stays at 4.4 while z is pinned at the world
edge, a stride phase that jumps, a head excursion spike during a blend, allocations appearing in one step.
`awk -F, 'NR>1 && $1>3 && $1<4.6 {print $1,$2,$4,$5}' log.csv` is enough to read it.

## 4. Scenario library (keep as regression tests)
| Scenario | Script | What to check |
|---|---|---|
| Locomotion sweep | `w 1.5; w+shift 2; a+w 1; idle 1; s 1` | yaw settles without hunting, speed ramps, stop settles in ~0.3 s, head excursion flat (~5 cm) |
| Combat while moving | `w+shift 1; q; idle 1.4; e; idle 1` | attack auto-draws, moving cancels, weapon re-attaches |
| Weather toggles | `n; t; t; idle 2` | particles stay pooled, alloc column stays 0 |
| Camera vs scenery | `cam 200 12 4; w+shift 3` through the trees | camera never inside foliage or trunk, pulls in and releases smoothly |
| Obstacle slide | sprint at a trunk / map edge, then strafe | position stalls, feet **stop** (animation from actual displacement), slide component matches sin(angle) |

## 5. What the first runs found (and the fixes)
* **Camera through tree crowns and trunks.** Fix: pull the orbit camera in along the target→camera ray against
  each tree's trunk (six stacked spheres, ~0.5 m apart — three left gaps the ray slipped through) and the *exact*
  foliage volumes the builder emitted (record every leaf blob / pine tier as (centre, radius) in tree-local
  space). Coarse "one big crown sphere" volumes either miss low blobs or swallow the walkable ground and pin
  the camera at minimum distance; an origin-inside-sphere test must return "no hit", not 0. Keep a readable
  minimum distance (1.2 m) and the eye ≥ 0.25 m above ground.
* **Foliage at head height.** Walking through a crown can only look bad; raise broadleaf/birch crowns
  (trunk 2.7–3.3 m, branches from 62 % of the trunk, pitch ≥ 0.45) so characters and camera pass underneath.
* **Running on the spot against the map edge / a trunk.** Feed the animation the speed of the displacement that
  actually happened after collision and clamping (`min(inputSpeed, |Δpos|/dt)` smoothed at 15/s), not the input velocity.
* **Stop quality.** Speed decays 1.6 → 0 in 0.3 s and the stride phase eases to the nearest double-support; the
  sheet confirmed no freeze mid-swing. The log's `clip` column showed `Draw` after `q` — the auto-draw works.

## 6. Gotchas
* Taps: hold for exactly one frame; two frames would double-fire toggles like `n`.
* `cam` snaps `_camDist` *and* its goal; otherwise the smoothing lerps back to the old value.
* Name frames by script time, not frame index, so sheets from different runs line up.
* The HUD "Controlling X" line is cached per focus — a `focus n` command must refresh the cache key.
* Put the per-frame `Log()` after the HUD so its allocations are counted (they should be 0: use a
  `StringBuilder` or `string.Create` only in the recorder, which is test-only code).

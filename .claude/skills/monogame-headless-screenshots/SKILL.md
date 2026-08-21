---
name: monogame-headless-screenshots
description: Iterate on a MonoGame (or any GPU) game's visuals from an agent/CLI without a human at the screen — command-line startup options for camera/animation/state, an in-app --shot flag that renders one MSAA frame to a RenderTarget2D, saves a PNG and exits, warm-up stepping of simulation time, and why screen-scraping or SendKeys to an SDL window fails. Use when verifying rendering or animation changes programmatically, producing README images, or driving a visual feedback loop.
---

# Headless screenshots for visual iteration

## What does not work (learned the hard way)
* **Screen capture of the window** (`Graphics.CopyFromScreen`, `GetWindowRect`) grabs whatever is on top at those
  pixels — another fullscreen game, an overlay, or a different monitor's content. Results were wrong three times in
  a row and looked plausible each time.
* **SendKeys / SendInput into the SDL2 window** does not reliably register key presses for MonoGame DesktopGL
  (keys never arrived even with the window foregrounded).
* Window size is not guaranteed (maximised by OS state), so pixel coordinates drift.

## What works: render offscreen inside the app
```csharp
// Program.cs: parse --key value pairs into a dictionary; flags without a value map to "1".
// Game1.Draw:
string? shot = Program.Options.TryGetValue("shot", out var p) ? p : null;
if (shot != null && _shotTarget == null)
    _shotTarget = new RenderTarget2D(gd, w, h, false, SurfaceFormat.Color, DepthFormat.Depth24, 8, RenderTargetUsage.DiscardContents);
gd.SetRenderTarget(_shotTarget);          // null when not capturing
// ... normal scene + HUD ...
if (_shotTarget != null) {
    gd.SetRenderTarget(null);
    if (++_frame >= 3) {                   // let RT contents settle / first-frame init pass
        using var fs = File.Create(shot);
        _shotTarget.SaveAsPng(fs, _shotTarget.Width, _shotTarget.Height);
        Exit();
    }
}
```
* `SaveAsPng` works on DesktopGL; 8× MSAA render targets work on HiDef.
* The window still appears briefly; the process exits itself, so no lingering instance locks `bin/*.exe` for the
  next `dotnet build` (kill stray instances first: `taskkill /IM App.exe /F`).
* Shell wrapper: launch, poll for the PNG (up to ~30 s), `ls -la` it. Then view the PNG with an image-capable reader.

## Env vars vs command-line options
monogame-project-setup / monogame-game-architecture use environment variables (`<PREFIX>_SCREENSHOT`,
`<PREFIX>_SHOT_DELAY`, `<PREFIX>_VIEW`, `<PREFIX>_BOT`) and save the back buffer with `GetBackBufferData`; this skill
uses `--key value` arguments and an MSAA render target. They are the same idea — pick one per project. Arguments are
easier to pass from `Start-Process`/scripts on Windows; env vars are easier from bash one-liners. The offscreen
render target is required when the back buffer is multisampled (`GetBackBufferData` cannot read an MSAA surface).

## Make state reachable from the command line
Expose everything the screenshot needs as options so captures are deterministic:
`--yaw --pitch --dist` camera, `--focus n --ty fraction` target a character, `--clip n` animation, `--varied`,
`--light deg`, `--no-orbit` (disable turntable), `--drawn`/`--draw s` weapon state, `--export dir` (OBJ dump),
and crucially **`--warm seconds`**: step the simulation with fixed `1/60` ticks before the first frame so an
animation is captured mid-motion (`--warm 0.55` vs `0.9` vs `1.4` shows three phases of a swing).

## Workflow that converged fast
1. Build (`dotnet build | grep -E " error |Build succeeded"`), capture 1–3 framings per change.
2. Look at the images for: exposure (double-gamma), inside-out primitives (view from behind), joint signs (side
   view), sliding/penetration, HUD overlap.
3. Fix, rebuild, recapture. Each loop is ~15 s; far cheaper than reasoning about signs in the abstract.
4. Keep the good captures in `docs/` for the README — they are already the right size and composition.

## Also useful
* Apply the same tone-map curve in C# to the clear colour so screenshots don't show a mismatched horizon.
* Print triangle/vertex/bone counts in the HUD; they show up in every capture and catch regressions.
* Honour a `--export` option in the same code path to dump meshes for inspection in a DCC.

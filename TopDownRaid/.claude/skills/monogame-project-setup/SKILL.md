---
name: monogame-project-setup
description: Scaffold, build, and headlessly verify a MonoGame 3.8.x DesktopGL project (templates, MGCB content, screenshot-based visual verification without a visible desktop). Use when creating a new MonoGame project or when you need to confirm rendering output from the CLI.
---

# MonoGame project setup & verification

## Scaffold (Windows/Linux/macOS, MonoGame 3.8.5)

```powershell
dotnet new mgdesktopgl -n Game1 -o .     # template gives csproj, Program.cs, Content/Content.mgcb, icons, .config/dotnet-tools.json
dotnet build                             # restores MonoGame.Framework.DesktopGL + Content.Builder.Task, runs MGCB, compiles
```

- Template targets `net9.0`; MonoGame packages `3.8.*`. `dotnet build` also builds every item in `Content/Content.mgcb`
  (via `MonoGame.Content.Builder.Task`) — the `.xnb` lands in `bin/<cfg>/<tfm>/Content/`.
- `Program.cs` uses `new Game1.Game1()` — keep `namespace Game1; public class Game1 : Game` or update Program.cs.
- Add `#nullable enable` at the top of Game1.cs if you use `?` annotations (the template csproj does not enable nullable).
- Set `GraphicsProfile = GraphicsProfile.HiDef` in the `GraphicsDeviceManager` for non-pow2 wrap, larger textures, ps_3_0.

## Registering content (text form of Content.mgcb)

```
#begin Shaders/Deferred.fx
/importer:EffectImporter
/processor:EffectProcessor
/processorParam:DebugMode=Auto
/build:Shaders/Deferred.fx

#begin Textures/floor_normal.png
/importer:TextureImporter
/processor:TextureProcessor
/processorParam:ColorKeyEnabled=False
/processorParam:GenerateMipmaps=True
/processorParam:PremultiplyAlpha=False      # MUST be False for normal maps
/processorParam:ResizeToPowerOfTwo=False
/processorParam:TextureFormat=Color         # no DXT for normal maps
/build:Textures/floor_normal.png
```

Load with `Content.Load<Effect>("Shaders/Deferred")` / `Content.Load<Texture2D>("Textures/floor_normal")`.
Rebuild content only: `dotnet mgcb /@:Content/Content.mgcb`.

Optional-art pattern — try the pipeline first, fall back to procedural so the project runs with no PNGs:

```csharp
private Texture2D LoadOrGenerate(string assetName, Func<Texture2D> generator)
{
    try   { return Content.Load<Texture2D>(assetName); }
    catch (ContentLoadException) { return generator(); }
}
```

## Headless visual verification (no visible desktop / lock screen)

Screen capture fails when the session is locked; instead read the back buffer from inside the game and exit.
Add to `Game1`:

```csharp
private readonly string? _autoShot = Environment.GetEnvironmentVariable("GAME1_SCREENSHOT");
private double _runTime;
// in Update: _runTime += gameTime.ElapsedGameTime.TotalSeconds; if (_autoShot != null && _runTime > 3.0) _shotRequested = true;
// at END of Draw (after base.Draw):
if (_shotRequested) { SaveScreenshot(_shotPath); _shotRequested = false; if (_autoShot != null) Exit(); }

private void SaveScreenshot(string path)
{
    var pp = GraphicsDevice.PresentationParameters;
    var data = new Color[pp.BackBufferWidth * pp.BackBufferHeight];
    GraphicsDevice.GetBackBufferData(data);
    using var tex = new Texture2D(GraphicsDevice, pp.BackBufferWidth, pp.BackBufferHeight);
    tex.SetData(data);
    using var fs = File.Create(path);
    tex.SaveAsPng(fs, pp.BackBufferWidth, pp.BackBufferHeight);
}
```

Then from bash: `GAME1_SCREENSHOT=/path/out.png GAME1_VIEW=3 timeout 30 dotnet bin/Debug/net9.0/Game1.dll` and open the
PNG with the Read tool. Pair with an env var that selects a debug view (albedo/normal/light/...) so each render pass can be
inspected individually — this is how a black composite was traced to unapplied shader defaults.

Smoke test without a screenshot: `timeout 12 dotnet bin/Debug/net9.0/Game1.dll; echo $?` → `124` means it ran until killed
(no crash); `0` with an auto-screenshot means it exited cleanly.

## Beyond one file
When the prototype grows, follow monogame-game-architecture (Core/Graphics/World/Entities/Combat/Items/UI folders,
`GameContext`, pipeline callbacks). Extra headless knobs used there: `GAME1_SHOT_DELAY`, `GAME1_ZOOM`, `GAME1_BOT=1`.

## Gotchas
- Bash heredocs containing large C# / Python bodies sometimes fail to parse in this harness ("unexpected EOF while looking for
  matching `'`"); write the script to the scratchpad with the Write tool and run `python script.py` instead.
- Keyboard toggles on letters clash with WASD movement — put debug toggles on F-keys.

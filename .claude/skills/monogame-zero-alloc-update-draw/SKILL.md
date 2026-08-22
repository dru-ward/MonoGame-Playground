---
name: monogame-zero-alloc-update-draw
description: Reach and keep zero per-frame managed allocations in a MonoGame (or any .NET) game loop — a 10-line measurement harness (GC.GetAllocatedBytesForCurrentThread delta per frame, collection counts, CPU ms, a --perf flag that prints and exits, live HUD readout), the catalogue of things that silently allocate in Update/Draw with their measured cost (string concatenation for bone/parameter names, interpolated HUD strings, params arrays, lambdas/closures, SetRenderTarget(s) binding arrays, Effect.Parameters[string] lookups, foreach over List<struct>, List.RemoveAt shifting), the fixes (name caches, StringBuilder + DrawString(StringBuilder) + allocation-free number formatting, static keyframe tables, cached delegates, cached RenderTargetBinding[], cached EffectParameter/EffectTechnique, pre-sized struct pools with swap-remove), and .NET GC settings. Use when a MonoGame game stutters, when adding per-frame systems (particles, HUD, animation), or when reviewing Update/Draw code for garbage.
---

# Zero-allocation Update/Draw in MonoGame

From taking a scene (5 skinned characters, 22 animated trees, 2600 rain particles, deferred lighting, HUD)
from ~3.5–7.7 KB allocated per frame to **0 bytes per frame**, measured, not guessed. Steady garbage is what
turns into gen-0 pauses "at seemingly random moments"; at 7 KB/frame a 256 KB gen-0 budget fills every ~0.6 s.

## 1. Measure first (10 lines)
```csharp
long _allocStart; Stopwatch _clock = new();
protected override void Update(GameTime gt) { _allocStart = GC.GetAllocatedBytesForCurrentThread(); _clock.Restart(); ... }
// at the very end of Draw (after HUD/SpriteBatch.End):
long allocFrame = GC.GetAllocatedBytesForCurrentThread() - _allocStart;   // bytes this frame, this thread
double cpuMs = _clock.Elapsed.TotalMilliseconds;                          // Update+Draw CPU (excludes Present)
// accumulate from frame 3 on (first frames are warm-up: JIT, lazy buffers), print GC.CollectionCount(0/1/2) deltas
```
* `--perf N` (or an env var) runs N frames headless, prints `avg B/frame, last frame B, GC gen0/1/2, CPU avg/max ms`
  and exits — run it per scenario (`--forward`, `--rain --walk`, each clip). Put `Alloc: N B/frame  GC: a/b/c` on the HUD too.
* Use **Release** (`dotnet run -c Release`); Debug keeps temporaries alive and changes JIT decisions.
* "last frame" vs "avg" separates warm-up growth (lists, lazy render targets) from steady-state garbage: a 3.3 KB
  average with a 0 B last frame just means pools grew during the first 20 frames — pre-size them.
* To localise a stubborn allocation, drop `Probe("name")` calls (delta since last probe → stderr) between the
  stages of a pass, behind a flag. One run told us the 64 B survivor was outside the renderer entirely.

## 2. What actually allocated here (and the fix)
| Source | Cost | Fix |
|---|---|---|
| `"arm" + L` bone names in animation clips (dozens per character per frame) | ~2 KB/frame | `BoneNames.Of("arm", L)`: `Dictionary<(string,string),string>` cache — value-tuple key, no concat |
| `bone.Replace("foot", "toe")` per evaluation | strings | same cache pattern keyed by the bone name |
| `Key(u, params (float,float)[] keys)` with inline tuples in a clip | 7 arrays/frame | hoist to `static readonly (float,float)[]` tables |
| Interpolated HUD strings `$"Animation: {x} ..."` | ~1.5 KB/frame | `StringBuilder` reused + `SpriteBatch.DrawString(font, StringBuilder, …)` overload; constant lines in `static readonly string[]`; lines that change on state rebuild only when a key changes |
| `float.ToString("0.00")` inside the builder | string | `AppendFixed(sb, v, decimals)` — integer maths into `Append(char)` |
| `new[] { "line", ... }` help text per frame | array | static arrays; one mutable slot for the "Controlling X" line refreshed on focus change |
| `() => DrawScene(false)` passed to the renderer every frame | 64 B/frame (delegate) | cache in a field: `_draw ??= () => DrawScene(false)` |
| `gd.SetRenderTargets(a, b, c)` (params) and `SetRenderTarget(single)` | binding array per call | keep `RenderTargetBinding[]` per target set; `SetRenderTargets(null)` for the back buffer |
| `new RasterizerState { FillMode = WireFrame }` in Draw | object + GPU state | static readonly state objects |
| `effect.Parameters["Name"]` / `Techniques["Name"]` | no GC, but a linear string compare per lookup × ~30 draws × 2 passes | cache `EffectParameter` / `EffectTechnique` fields once in the constructor (regex: `p\["(\w+)"\]` → `_p$1`) |
| `foreach (var d in _drops)` over `List<struct>` | copies each struct (no GC) | `for` + index; write back `_list[i] = d` |
| `List.RemoveAt(i)` mid-list for dead particles | O(n) shifting | swap-remove: `_list[i] = _list[^1]; _list.RemoveAt(_list.Count - 1)` while iterating backwards |
| Lists/arrays growing in the first frames | warm-up spikes | `new List<T>(Max)` and a vertex array sized for the worst case (`MaxDrops*6 + MaxSplashes*48 + MaxLeaves*12`) |

Result: 0 B/frame in forward and deferred paths, with rain and player movement; CPU 0.6–1.0 ms/frame.

## 3. Things that are fine (don't cargo-cult)
* `foreach` over `List<T>` and MonoGame's `EffectPassCollection` — both have struct enumerators.
* Local functions that capture locals — struct closure, no allocation unless converted to a delegate.
* `Keyboard.GetState()` / `Mouse.GetState()` — structs. `Vector3`/`Matrix`/`Color` maths — structs.
* `DrawUserPrimitives<T>` with a reused array — no managed allocation.
* Dictionary lookups with string or value-tuple keys — no allocation (boxing only with enum/struct keys lacking
  `IEquatable`, so prefer `Dictionary<int,T>` over `Dictionary<SomeEnum,T>`).
* A lambda that captures `this` but is created once (clip definitions, light `Follow` providers).

## 4. Pool pattern for per-frame objects
```csharp
sealed class Pool<T> where T : struct {           // dense array + count; dead items swap-removed
    public T[] Items; public int Count;
    public Pool(int cap) { Items = new T[cap]; }
    public ref T Add() { return ref Items[Count++]; }          // caller fills the struct
    public void RemoveAt(int i) { Items[i] = Items[--Count]; } // iterate backwards when removing
}
```
Structs for particles/bullets/effects; classes only for long-lived things. Reuse render buffers
(`DynamicVertexBuffer.SetData(..., SetDataOptions.Discard)`), reuse `StringBuilder`, reuse `List<T>` via `Clear()`.

## 5. Other rules of thumb
* No LINQ, `string.Format`, `ToString()`, boxing, `params`, `yield`, exceptions or reflection in the loop.
* Build strings only when the value changes; compare the inputs (an int "key") and cache the string.
* Load-time allocation is fine; call `GC.Collect()` once after loading (before the first frame), never per frame.
* Consider `GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency` in `Initialize`, and
  `<TieredPGO>`/`<TieredCompilation>` defaults in Release; `<ServerGarbageCollector>` is rarely a win for a game.
* Watch MonoGame itself: `Model.Draw` (enumerators), `SetRenderTarget` (binding arrays), `SpriteFont` with string
  (fine) vs `string.Concat` you did to feed it (not fine).

Sources: [Konrad Żaba — MonoGame/XNA performance cheat sheet](https://konradzaba.github.io/blog/tech/Monogame-and-XNA-performance-cheat-sheet-low-level/),
[MonoGame issue #2360 — every-frame allocation](https://github.com/MonoGame/MonoGame/issues/2360),
[BitBull — Optimising memory use in MonoGame](https://blog.bitbull.uk/2016/06/30/optimising-memory-use-in-monogame/),
[MonoGame community — GC.Collect best practice](https://community.monogame.net/t/any-best-practice-gc-collect/14151).

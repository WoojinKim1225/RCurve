# RCurve

Rational curve baking for Unity, with managed and Burst evaluators.

**Preserves animation-key values and each finite in/out tangent as boundary constraints; approximates between keys.** Steps, discontinuous derivatives and wrapping have explicit rules. This is an experimental **0.2.0** release, not a claim of bit-identical AnimationCurve evaluation everywhere.

[Open the interactive WebGL demo](https://woojinkim1225.github.io/RCurve/) (available after publishing the `gh-pages` branch).

## Install

Unity Package Manager → **Install package from Git URL**:

```text
https://github.com/WoojinKim1225/RCurve.git#v0.2.0
```

Package ID: `com.woojinkim1225.rcurve`. Requires Git and a Unity 6 editor. Do not install alongside an older `Assets/RCurve` source copy; back up/migrate that copy first to avoid duplicate assemblies/GUIDs.

## Use

```csharp
using RCurve;
using UnityEngine;

public class Motion : MonoBehaviour
{
    [SerializeField] private RationalCurve _curve = new RationalCurve();
    private int _intervalHint = -1;

    private void Update()
    {
        double value = _curve.Evaluate(Time.time); // Stateless/random access.
        // Or: _curve.Evaluate(Time.time, ref _intervalHint) for a playback stream.
    }
}
```

Edit the source in the Inspector. Expand its foldout for fitting and lookup settings. The Editor bakes after a short debounce. Defaults: **Minimax / High (5/5) / Auto lookup**.

Runtime fitting is also available for tools and user-authored curves:

```csharp
curve.Bake(); // Allocates and may be expensive; call after edits, not every frame.
```

## How it works

For every smooth source interval, RCurve converts Unity's weighted Hermite key data to a parametric cubic Bézier, samples it, then fits a positive-denominator rational Bernstein curve:

```text
P(u) = Σ pᵢ Bᵢ,ₙ(u)    Q(u) = Σ qᵢ Bᵢ,ₙ(u), qᵢ > 0    R(u) = P(u) / Q(u)
```

The endpoint numerator coefficients are determined by the key values and finite tangents. Least-squares or sampled minimax fitting chooses the remaining coefficients; intervals subdivide until sampled value and slope tolerances pass. Evaluation converts the Bernstein coefficients once to an allocation-free Horner form. See the [interactive graph](https://woojinkim1225.github.io/RCurve/) and [usage guide](Documentation~/usage.md).

- `Evaluate(time)` returns the value; `EvaluateSlope(time)` returns dy/dt.
- `Evaluate(time, ref intervalHint)` optionally reuses a caller-owned interval. Do not share mutable hints between playback streams/jobs.
- `curve.BakedCurve` exposes immutable managed data.
- `curve.Bake()` fits and stores the current source in Editor or player code.
- `NativeRationalCurve.Create(curve.BakedCurve, allocator)` makes a Job snapshot. Complete readers before disposing one owner; struct copies share allocations.
- Use Burst `FloatMode.Strict` / `FloatPrecision.High`. Calling a method directly does not make it Burst-compiled.
- Failed rebakes keep the last good result. An unbaked curve cannot be evaluated.

## Lookup strategies

Choose **Auto**, **BruteForce**, **LUT**, or **Magic** independently of fitting quality.

Auto uses a benchmark-calibrated cost model at bake time, with separate managed/Burst choices. LUT storage is limited to 1 KiB per curve. Magic uses a certified global index predictor plus at most one adjacent-interval correction. Binary search remains an internal candidate/fallback. Auto is an estimated policy, **not a guarantee of the fastest strategy on every CPU or access pattern**.

## Samples

In Package Manager, select **RCurve → Samples → Curve Comparison → Import**. Open the imported `Comparison.unity` scene.

- Blue: AnimationCurve. Orange: RationalCurve. Both receive the same time.
- Editable Canvas/uGUI dashboard: playback, reverse, seeking, optional hint, values and errors.
- Only the signed-error bar is magnified. Marker positions use the actual values.
- Stop Play Mode before editing/rebaking. The scene demonstrates accuracy, not reliable frame-time benchmarking.

Import **Runtime Web Demo** for an IMGUI graph whose key values can be changed and baked while running. It is the source used by the WebGL demo.

Sample scripts and their UI dependency are separate from the core runtime assemblies. The comparison sample uses Built-in unlit materials; URP/HDRP hosts may need replacement materials. Its supplied EventSystem uses the Legacy Input Manager; configure InputSystemUIInputModule/UI Actions in Input System-only hosts.

## Compatibility and limits

- API baseline: **Unity 6000.0+**. Unexecuted editor versions are not certified; see [compatibility notes](Documentation~/compatibility.md).
- Core `RCurve.Runtime` has no UnityEngine dependency. Unity serialization and fitting, Burst, Editor tooling, and sample UI are separate assemblies.
- Dependencies declare Burst 1.8.30, Mathematics 1.3.2 and uGUI 2.0.0. Newer Unity editors may resolve built-in newer versions.
- Weighted segments with stationary X are currently rejected. Fitting validates samples, not a continuous error proof.
- Some large weighted inputs can exceed the Minimax simplex iteration limit. LeastSquares is a separate explicit option, not a silent fallback.
- `EvaluateSlope` may return NaN at corners, steps and wrap seams with no common derivative. Input time is wrapped before evaluation.
- Unity JSON round-tripping can shift doubles by a few ULP. Input signatures use semantic binary data rather than editor-version-dependent AnimationCurve JSON.
- No IL2CPP/player-AOT or all-render-pipeline claim. Do not infer universal speedups from Editor measurements.

## Tests and documentation

See [testing](Documentation~/testing.md), [사용 가이드 (Korean)](Documentation~/usage.md), and [changelog](CHANGELOG.md).

Tests compare managed/Burst values and slopes, exact boundaries, weighted fits, serialization, hints and lookup strategies. Performance tests report measurements without flaky timing thresholds.

## License

[MIT](LICENSE.md). Copyright © 2026 WoojinKim1225.

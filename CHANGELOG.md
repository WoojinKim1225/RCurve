# Changelog

## 0.7.0

- **Breaking:** steps are segments. `BakedRationalCurve.Segments` never holds null; a step is a `RationalCurveSegment` with `IsStep`, holding its start value over `[Start, End)` (slope 0). Under the curve's `[start, end)` ownership a step needs no special case: the jump belongs to the next key, which owns `End`. Code that detected steps with `segment == null` must use `IsStep` (it still compiles, so check). Asking a key's in side with `Segments[i - 1].EvaluateSlope(keyTime)` no longer fails after a step. Serialized bakes, the input hash and the Burst evaluator are unchanged.
- The input hash is part of the contract: 1.x never changes it for an existing input, and a setting added in 1.x enters it only when not at its default. Pinned values in the tests guard it.

## 0.6.0

- **Breaking:** `EvaluateSlope` no longer returns NaN where the two sides of a time disagree. It returns the slope of the segment that owns the time, as the value does (each segment owns `[start, end)`, the last one also its end), times the wrap direction. At a key that is its out tangent (its in tangent at the last key, 0 on a step); at a wrap seam the first segment's start; at a ping-pong turnaround the owning segment's slope. Before, even a plain curve's first and last keys returned NaN under clamping when their tangents were not 0, and NaN spread silently through whatever used it. Managed and Burst evaluators agree. Unity's `AnimationCurve` has no slope API, so this is RCurve's rule; values already followed Unity's ownership (right-continuous at steps).

## 0.5.4

- **Supported editors are now Unity 6000.6+** (`package.json` declared 6000.0, which was never run). Earlier Unity 6 editors may work but are not supported.
- Checked a built Linux x64 Mono player: Burst compiled ahead of time ran every query, and 16 editor-baked (deserialized) and 20 player-baked curves across all four lookup strategies matched the managed evaluator bit for bit. The check is in `Documentation~/tools/PlayerCheck` (see Testing). IL2CPP players remain unverified.

## 0.5.3

- Magic's managed lookup now throws if it misses after its one correction, instead of silently falling back to binary search. The miss is unreachable while the predictor's certificate holds (monotone over the whole range, within 1 − 10⁻⁶ of every key's index, checked at bake and load); a new test queries 283 Magic curves at keys, adjacent doubles, midpoints, random and wrapped times without reaching it. The Burst lookup, which cannot rely on exceptions in release players, keeps the binary search there.
- README and usage guide: explained that Magic proves its one-interval search bound at bake time, the same proof-offline, constant-time-runtime idea as the error bounds, and stated exactly what happens when no predictor can be certified.

## 0.5.2

- Fixed: baking outside the Inspector (Undo-recorded edits, reimported assets, the open-scene scan at editor start) deferred its work with `EditorApplication.delayCall`, which never runs in batch mode, so command-line and CI imports left reimported curves unbaked. It now runs on the next `EditorApplication.update`.
- Clarified which edits are seen: those recorded with `Undo.RecordObject`. Unity raises no Undo modification event for `SerializedObject.ApplyModifiedProperties` called from a script; such tools call `CurveAutoBuild.Queue`.
- Added tests for the three paths (Undo-recorded edit of an undrawn field, an asset edited on disk and reimported, a stale curve in a scene as it opens) and for finding the curve an edited property belongs to.

## 0.5.1

- Removed the Runtime Web Demo sample and its hosted WebGL build (it still showed 0.3.1). The Curve Comparison sample remains.

## 0.5.0

Prepares the 1.0 contract (see README, Contract); no new fitting or evaluation features.

- **Breaking:** internal now (implementation, not API): `AnimationCurveBezierSegment`, `CurveLookupPlanner`, `CurveIndexPrediction`, `RationalCurveBuilder.FromSegments`, the `BakedRationalCurve` constructor and its `IndexPrediction`, `LookupTable` and `LookupScale`, the `RationalCurveSegment` constructor and its `Degree` and coefficient spans, `RationalCurve.StoreBuild`, `StoreBuildFailure`, `InputHash` and `NeedsBuild`, and `RationalCurveDrawer`. Build curves with `RationalCurveBuilder.Build` or `RationalCurve.Bake`.
- Each bake records its data format (1) and the RCurve version that made it; a bake in a newer format is refused instead of misread. Bakes written before this field read as format 0, the same layout, and keep working.
- Documented the error bounds' units and reference, the failure modes, determinism and the public surface.
- Added contract tests: format versioning, bit-identical rebakes, a 500-curve differential test against the exact source (asserted) and Unity's `AnimationCurve.Evaluate` (asserted on unweighted segments), and edge cases (single key, no keys, zero and full-length handles, out-of-range weights, steps, repeated and reversed key times).

## 0.4.2

- Each baked segment's value and slope errors are now proven over the whole interval instead of checked on a 129-point grid. In the Bezier parameter the source is polynomial, so each bound becomes the nonnegativity of a polynomial, tested by its Bernstein coefficients with de Casteljau refinement; a segment that cannot be proven is subdivided as before. On 300 random curves at default settings, the grid had passed 12 curves whose slope error exceeded the limit (up to 3.3x); they are now subdivided (+1.6% segments overall, value errors unchanged).

## 0.4.1

- Added `StartTime`, `EndTime`, `StartValue` and `EndValue` (the first and last keys) to `BakedRationalCurve` and `RationalCurve`; they throw for a curve with no keys, and on `RationalCurve` are checked like an evaluation.

## 0.4.0

- **Breaking:** `RationalCurve.Source` is removed; `CopySource()` returns a copy, and the constructor copies the curve it is given. The source changes only through serialization (the Inspector, the asset's file), so code cannot edit a baked curve into one its bake no longer matches. Editor tools change it through `SerializedProperty`; a player builds a new `RationalCurve` and bakes it.
- Curves now bake when they change, not only while an Inspector draws them: edits recorded through Undo on any field (folded or never shown) are debounced like Inspector edits; `.asset`/`.prefab` files reimported from disk (text edits, version control) are built and saved at once; open scenes are queued as they open and marked dirty.
- **Behavior change:** the first evaluation after a curve is loaded (or baked) checks the bake against its source and settings, and throws `InvalidOperationException` if they changed since (an editor tool without Undo, a text edit, a merge) or the latest bake failed. A failed bake still keeps the last good result in the asset and Inspector, but it is no longer evaluated against the changed source.

## 0.3.1

- Runtime Web Demo supports 2–5 equally spaced keys with independent incoming/outgoing slopes and weights, retaining the Ease/Wave/Overshoot presets.
- Added an animated 2D point driven by RCurve evaluation and an automatically scaled graph. Failed bakes retain both the last graph and the point's curve.
- Expanded sample checks for all key counts, weighted inputs, endpoint tangents and failure preservation.

## 0.3.0

- Added reproducible SVG documentation graphs for weighted-curve accuracy and step-by-step Magic interval prediction, correction and evaluation, with raw samples and generation checks.
- **Breaking:** removed the `SerializableRationalCurve` type migration and scalar `EvaluateDerivative` aliases; use `RationalCurve` and `EvaluateSlope`. Segment evaluation named arguments now use `time` instead of `x`.
- Clarified fitter fields, normalized-time sampling, editor debounce state and constant names; removed duplicate predicted-interval checks without changing fitting or lookup algorithms.
- Magic centers key-index residuals before monotonicity/error certification; explicit BruteForce/LUT builds skip predictor fitting. Added forced managed/Burst Magic and binary-fallback coverage at keys, adjacent doubles and random times.
- Web demo now starts only when attached to a scene object, caches graph samples, bakes on slider release and retains the last good graph after failure.

## 0.2.0

- Moved the fitter into the player-compatible Unity assembly and added explicit `RationalCurve.Bake()` runtime baking.
- Added an importable interactive runtime graph sample for WebGL demos.
- Documented the rational Bernstein fitting principle and runtime cost.

## 0.1.0

Initial experimental release.

- Editor-only Bernstein rational fitting, degrees 3–6, sampled LeastSquares/Minimax objectives and adaptive subdivision.
- Source-key value and finite in/out tangent constraints; explicit step and wrap semantics.
- Allocation-free Horner evaluation, optional interval hints and Burst snapshots.
- Auto / BruteForce / LUT / Magic lookup modes, bounded LUT storage and binary-search fallback.
- Serializable RationalCurve, debounced Inspector bake and semantic input signatures.
- Importable Canvas/uGUI comparison sample and Edit Mode correctness/performance tests.
- MIT license. Compatibility limitations are documented rather than assumed.

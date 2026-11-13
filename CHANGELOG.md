# Changelog

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

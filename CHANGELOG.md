# Changelog

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

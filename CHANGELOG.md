# Changelog

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

# Compatibility

## Target versus verified support

The supported baseline is **Unity 6000.6 (Unity 6.6)**, the line the package is developed and tested on. Up to 0.5.3 package.json declared 6000.0, the API baseline the code was written against; 6.0 to 6.5 were never run, so from 0.5.4 the declared minimum is the verified one. Earlier Unity 6 editors may still work but are not supported.

- Development editor: Unity 6000.6.0f1.
- Unity 6000.5.8f1 is used as a separate clean import/test host up to 0.3.1; Unity 6000.6.8f1 from 0.4.0.
- Other 6.x editors, render-pipeline hosts and player/browser configurations beyond those listed below require separate validation.

## Validation results

| Version / host | Installation | Results |
|---|---|---|
| 0.5.4 / Unity 6000.6.8f1 | Local UPM (`file:` working copy); Linux x64 **Mono player** built from the host | **Player check passed: Burst AOT ran every query; 16 editor-baked (deserialized) and 20 player-baked curves across all four lookup strategies, about 11,000 queries, values and slopes bit-identical to the managed evaluator** |
| 0.5.3 / Unity 6000.6.8f1 | Local UPM (`file:` working copy); Curve Comparison copied into Assets | **309/309 Edit Mode passed** |
| 0.5.2 / Unity 6000.6.8f1 | Local UPM (`file:` working copy); Curve Comparison copied into Assets | **308/308 Edit Mode passed** |
| 0.5.1 / Unity 6000.6.8f1 | Local UPM (`file:` working copy); Curve Comparison copied into Assets | **304/304 Edit Mode passed** |
| 0.5.0 / Unity 6000.6.8f1 | Local UPM (`file:` working copy); both samples copied into Assets | **304/304 Edit Mode passed, including the sample tests and the 500-curve contract tests** |
| 0.4.2 / Unity 6000.6.8f1 | Local UPM (`file:` working copy), sample not imported | **297/297 Edit Mode passed** |
| 0.4.1 / Unity 6000.6.8f1 | Local UPM (`file:` working copy), sample not imported | **294/294 Edit Mode passed** |
| 0.4.0 / Unity 6000.6.8f1 | Local UPM (`file:` working copy), sample not imported | **293/293 Edit Mode passed** |
| 0.3.1 / Unity 6000.5.8f1 | Local UPM + runtime web demo source | **292/292 Edit Mode passed; expanded sample checks passed; release WebGL IL2CPP build succeeded; Firefox 153 headless rendering/input smoke check passed** |
| 0.3.0 / Unity 6000.5.8f1 | Local UPM + runtime web demo source | **292/292 Edit Mode passed; WebGL IL2CPP build succeeded; Linux Mono headless player checks passed, including actual Burst execution** |
| 0.2.0 / Unity 6000.5.8f1 | Local UPM, sample not imported | **284/284 Edit Mode passed; URP 17.5.0 WebGL build succeeded** |
| 0.1.0 / Unity 6000.5.8f1 | Local UPM, sample not imported | **283/283 Edit Mode passed** |
| 0.1.0 / Unity 6000.5.8f1 | Local UPM + official Package Manager sample import | **285/285 Edit Mode passed** |
| 0.1.0 / Unity 6000.6.0f1 | Local UPM + official Package Manager sample import | **285/285 Edit Mode passed** |
| 0.1.0 / Unity 6000.6.0f1 | Public GitHub Git URL + official sample import | **285/285 Edit Mode passed** |

Each host had an empty Assets directory before installation (apart from the sample-import validation helper). Core tests do not depend on the optional sample. The numbered test counts are batch Edit Mode checks on Linux. For 0.3.0, separate CLI player checks also covered runtime baking, Magic correction, 1,025 wrapped queries, serialization and an actual Burst job in a Linux Mono player. For 0.3.1, a local release WebGL build was also exercised in Firefox 153 headless: graph rendering, point movement, switching to five and two keys, and slope/weight controls were checked. This is a smoke check, not browser/device-wide certification. From 0.4.0 to 0.5.3 the results are Edit Mode runs only. For 0.5.4 a Linux x64 Mono player was built and run headless with `Documentation~/tools/PlayerCheck` (see [testing](testing.md)). IL2CPP players have never been run: the Linux IL2CPP build module is not installed in the development environment.

## Dependencies

The manifest requests Burst 1.8.30, Mathematics 1.3.2, uGUI 2.0.0, and built-in IMGUI/JSONSerialize modules. Unity can resolve newer built-in versions:

| Editor | Burst | Mathematics | uGUI |
|---|---|---|---|
| 6000.5.8f1 | 1.8.30 | 1.4.0 | 2.5.0 |
| 6000.6.0f1 | 2.0.0 | 1.4.0 | 2.6.0 |

Burst 1.8.18 was tested and failed compilation against the newer TreeView editor API in 6.5. PackageCache code was not patched; the dependency was upgraded. Burst 1.8.30 declares Unity 6.0 as its minimum editor.

The comparison sample uses Canvas/uGUI and LegacyRuntime.ttf, not UI Toolkit. Configure its EventSystem for the host's input backend. Built-in unlit sample materials may need replacement in SRP hosts. Core evaluation has no render-pipeline or UI dependency.

## Serialization

Version 0.3.0 removes the `SerializableRationalCurve` → `RationalCurve` `MovedFrom` migration. Existing file GUIDs and serialized field names are unchanged, but automatic migration from the old type is no longer supported. Back up older assets before upgrading.

The scalar `EvaluateDerivative` aliases have also been removed; use `EvaluateSlope` on `RationalCurve`, `BakedRationalCurve`, `RationalCurveSegment` and `NativeRationalCurve`. Segment evaluation parameters are now named `time` rather than `x`; update named arguments. (`AnimationCurveBezierSegment` is internal from 0.5.0.)

Input signatures use versioned semantic binary values and SHA-256 rather than Unity's changing AnimationCurve JSON representation. Older bakes may be marked stale after upgrading and should be rebaked in the Editor. Runtime never repairs or fits stale data automatically; callers may explicitly replace it with `Bake()`.

Floating-point serialization/evaluation is not universally bit-identical. Keys and finite in/out tangents are boundary constraints; discontinuities, wrapping and JsonUtility's decimal round-trip limitations are documented in the usage guide.

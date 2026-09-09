# Compatibility

## Target versus verified support

The API baseline is **Unity 6000.0 (Unity 6.0)**. The package uses shared Unity 6 APIs, but a minimum version in package.json is not evidence of execution on every Unity release.

- Development editor: Unity 6000.6.0f1.
- Unity 6000.5.8f1 is used as a separate clean import/test host.
- Unity 6000.0 is not installed in the development environment; direct 6.0 execution remains unverified.
- Other 6.x editors, URP/HDRP hosts, player AOT/IL2CPP and live sample input/rendering require separate validation.

## Validation results

| Version / host | Installation | Results |
|---|---|---|
| 0.2.0 / Unity 6000.5.8f1 | Local UPM, sample not imported | **284/284 Edit Mode passed; URP 17.5.0 WebGL build succeeded** |
| 0.1.0 / Unity 6000.5.8f1 | Local UPM, sample not imported | **283/283 Edit Mode passed** |
| 0.1.0 / Unity 6000.5.8f1 | Local UPM + official Package Manager sample import | **285/285 Edit Mode passed** |
| 0.1.0 / Unity 6000.6.0f1 | Local UPM + official Package Manager sample import | **285/285 Edit Mode passed** |
| 0.1.0 / Unity 6000.6.0f1 | Public GitHub Git URL + official sample import | **285/285 Edit Mode passed** |

Each host had an empty Assets directory before installation (apart from the sample-import validation helper). Core tests do not depend on the optional sample. These were batch Edit Mode checks on Linux, not Play Mode or player-build checks.

## Dependencies

The manifest requests Burst 1.8.30, Mathematics 1.3.2, uGUI 2.0.0, and built-in IMGUI/JSONSerialize modules. Unity can resolve newer built-in versions:

| Editor | Burst | Mathematics | uGUI |
|---|---|---|---|
| 6000.5.8f1 | 1.8.30 | 1.4.0 | 2.5.0 |
| 6000.6.0f1 | 2.0.0 | 1.4.0 | 2.6.0 |

Burst 1.8.18 was tested and failed compilation against the newer TreeView editor API in 6.5. PackageCache code was not patched; the dependency was upgraded. Burst 1.8.30 declares Unity 6.0 as its minimum editor.

The comparison sample uses Canvas/uGUI and LegacyRuntime.ttf, not UI Toolkit. Configure its EventSystem for the host's input backend. Built-in unlit sample materials may need replacement in SRP hosts. Core evaluation has no render-pipeline or UI dependency.

## Serialization

Existing type/file GUIDs are preserved. SerializableRationalCurve was renamed to RationalCurve in the same assembly using MovedFrom; its sourceAssembly is deliberately null to avoid a rename-plus-assembly-move API Updater request.

Input signatures use versioned semantic binary values and SHA-256 rather than Unity's changing AnimationCurve JSON representation. Older bakes may be marked stale after upgrading and should be rebaked in the Editor. Runtime never repairs or fits stale data automatically; callers may explicitly replace it with `Bake()`.

Floating-point serialization/evaluation is not universally bit-identical. Keys and finite in/out tangents are boundary constraints; discontinuities, wrapping and JsonUtility's decimal round-trip limitations are documented in the usage guide.

# Testing

Use a clean Unity 6 host project with the package installed. Add the Unity Test Framework and mark the package testable in the host's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.woojinkim1225.rcurve": "https://github.com/WoojinKim1225/RCurve.git#v1.0.0",
    "com.unity.test-framework": "1.4.6"
  },
  "testables": ["com.woojinkim1225.rcurve"]
}
```

Keep any other dependencies your host needs. Newer editors may resolve a newer built-in Test Framework. To test a working copy, point the dependency at it instead (`"file:/path/to/working-copy"`, or a path relative to the host's `Packages` folder); Unity then writes `.meta` files for any untracked folders in the copy, which are not part of the package.

Run Edit Mode assemblies `RCurve.Editor.Tests` and `RCurve.Burst.Tests`. The serialization test host is test-only and does not depend on the optional comparison sample. After importing **Curve Comparison**, also run `RCurve.Demo.Tests`.

```sh
/path/to/Unity -batchmode -nographics -projectPath /path/to/host \
  -runTests -testPlatform EditMode \
  -assemblyNames 'RCurve.Editor.Tests;RCurve.Burst.Tests;RCurve.Demo.Tests' \
  -testResults /tmp/rcurve-tests.xml -logFile /tmp/rcurve-tests.log
```

Do not run this against a project currently open in another editor instance. Tests do not enter Play Mode. Sample scene tests inspect saved data, UI references, key constraints and approximation errors; they do not certify rendered appearance or live input.

Performance tests use synchronous real Burst execution markers, warmed repeated measurements, identical inputs, and correctness checks. They emit `RCURVE_BENCHMARK`, `RCURVE_LOOKUP_BENCHMARK`, and `RCURVE_HINT_BENCHMARK` records into test output. Timings are not pass/fail thresholds.

Contract tests (`ContractTests`, `SegmentCertificateTests`) check the promises in the README's Contract section: the error proof against dense sampling, a 500-curve differential run against the exact source (asserted) and Unity's `AnimationCurve.Evaluate` (asserted on unweighted segments; Unity's weighted solver is not an exact reference), bit-identical rebakes, format versioning and edge cases.

- Lookup calibration measures multiple strategies on identical curve data.
- Hint tests cover invalid/stale hints, boundaries and different playback patterns.
- Some dense benchmark fixtures explicitly use LeastSquares because Minimax can hit its iteration limit. They are not silent objective substitutions.
- Auto's calibrated cost model is deterministic at bake time; it is not a runtime profiler or universal fastest-path guarantee.

## Player check

Edit Mode tests run Burst inside the editor. To check a built player (Burst compiled ahead of time, curves deserialized from a build), copy `Documentation~/tools/PlayerCheck` into the host's `Assets`, then build and run:

```sh
/path/to/Unity -batchmode -nographics -projectPath /path/to/host -buildTarget Linux64 \
  -executeMethod BuildPlayerCheck.Build -logFile /tmp/rcurve-build.log
/path/to/host/Build/PlayerCheck.x86_64 -batchmode -nographics -rcurveResult /tmp/rcurve-player.txt
```

The build bakes 16 curves into an asset (all four lookup modes) and a scene that, in the player, evaluates them and 20 curves baked in the player through a Burst job, checks that Burst ran each query and that values and slopes match the managed evaluator, then writes `RESULT PASS` or `RESULT FAIL`. It uses the Mono backend; switch `ScriptingImplementation` in `BuildPlayerCheck` for IL2CPP if that module is installed.


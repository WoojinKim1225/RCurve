# Testing

Use a clean Unity 6 host project with the package installed. Add the Unity Test Framework and mark the package testable in the host's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.woojinkim1225.rcurve": "https://github.com/WoojinKim1225/RCurve.git#v0.3.1",
    "com.unity.test-framework": "1.4.6"
  },
  "testables": ["com.woojinkim1225.rcurve"]
}
```

Keep any other dependencies your host needs. Newer editors may resolve a newer built-in Test Framework.

Run Edit Mode assemblies `RCurve.Editor.Tests` and `RCurve.Burst.Tests`. The serialization test host is test-only and does not depend on the optional comparison sample. After importing **Curve Comparison**, also run `RCurve.Demo.Tests`.

```sh
/path/to/Unity -batchmode -nographics -projectPath /path/to/host \
  -runTests -testPlatform EditMode \
  -assemblyNames 'RCurve.Editor.Tests;RCurve.Burst.Tests;RCurve.Demo.Tests' \
  -testResults /tmp/rcurve-tests.xml -logFile /tmp/rcurve-tests.log
```

Do not run this against a project currently open in another editor instance. Tests do not enter Play Mode. Sample scene tests inspect saved data, UI references, key constraints and approximation errors; they do not certify rendered appearance or live input.

Performance tests use synchronous real Burst execution markers, warmed repeated measurements, identical inputs, and correctness checks. They emit `RCURVE_BENCHMARK`, `RCURVE_LOOKUP_BENCHMARK`, and `RCURVE_HINT_BENCHMARK` records into test output. Timings are not pass/fail thresholds.

- Lookup calibration measures multiple strategies on identical curve data.
- Hint tests cover invalid/stale hints, boundaries and different playback patterns.
- Some dense benchmark fixtures explicitly use LeastSquares because Minimax can hit its iteration limit. They are not silent objective substitutions.
- Auto's calibrated cost model is deterministic at bake time; it is not a runtime profiler or universal fastest-path guarantee.

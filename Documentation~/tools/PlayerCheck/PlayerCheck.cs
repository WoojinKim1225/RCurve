using System;
using System.IO;
using System.Text;
using RCurve;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

// Runs in the built player: evaluates editor-baked curves (deserialized) and curves baked in the player through a
// Burst job, compares with the managed evaluator, checks Burst actually ran, writes the result and quits.
public sealed class PlayerCheck : MonoBehaviour
{
    private StringBuilder _log = new StringBuilder();
    private bool _pass = true;

    private void Start()
    {
        try
        {
            _log.AppendLine($"Unity {Application.unityVersion}, platform {Application.platform}, burst enabled {BurstCompiler.IsEnabled}");
            var asset = Resources.Load<PlayerCheckCurves>("PlayerCheckCurves");
            Check(asset != null, "baked curves asset loaded");
            int index = 0;
            foreach (RationalCurve curve in asset.Curves)
            {
                Check(curve.HasBake && !curve.IsStale, $"editor-baked curve {index} is baked and current");
                Compare(curve.BakedCurve, $"editor-baked {index++} ({curve.BakedCurve.BurstLookup})");
            }
            var random = new System.Random(99);
            for (int c = 0; c < 20; c++)
            {
                var lookup = (CurveLookupMode)(c % 4);
                BakedRationalCurve baked;
                try { baked = RationalCurveBuilder.Build(RandomCurve(random), new CurveFitSettings(lookupMode: lookup)); }
                catch (InvalidOperationException) { _log.AppendLine($"player-baked {c}: not bakeable (documented)"); continue; }
                Compare(baked, $"player-baked {c} ({baked.BurstLookup})");
            }
        }
        catch (Exception exception)
        {
            _pass = false;
            _log.AppendLine("EXCEPTION " + exception);
        }
        _log.AppendLine(_pass ? "RESULT PASS" : "RESULT FAIL");
        string path = ResultPath();
        File.WriteAllText(path, _log.ToString());
        Application.Quit(_pass ? 0 : 1);
    }

    private void Compare(BakedRationalCurve curve, string label)
    {
        double[] keys = curve.KeyTimes.ToArray();
        double first = keys[0], span = Math.Max(1e-3, keys[keys.Length - 1] - first);
        var random = new System.Random(label.GetHashCode());
        var queries = new System.Collections.Generic.List<double>();
        foreach (double key in keys) { queries.Add(key); queries.Add(key + 1e-9); queries.Add(key - 1e-9); }
        for (int i = 0; i < 300; i++) queries.Add(first - span + random.NextDouble() * 3 * span);
        using var native = NativeRationalCurve.Create(curve, Allocator.TempJob);
        using var times = new NativeArray<double>(queries.ToArray(), Allocator.TempJob);
        using var values = new NativeArray<double>(queries.Count, Allocator.TempJob);
        using var slopes = new NativeArray<double>(queries.Count, Allocator.TempJob);
        using var burst = new NativeArray<byte>(queries.Count, Allocator.TempJob);
        new EvaluateJob { Curve = native, Times = times, Values = values, Slopes = slopes, Burst = burst }
            .Schedule(queries.Count, 32).Complete();
        int mismatches = 0, notBurst = 0;
        double worst = 0;
        for (int i = 0; i < queries.Count; i++)
        {
            if (burst[i] != 1) notBurst++;
            mismatches += Mismatch(values[i], curve.Evaluate(times[i]), ref worst);
            mismatches += Mismatch(slopes[i], curve.EvaluateSlope(times[i]), ref worst);
        }
        Check(notBurst == 0, $"{label}: Burst executed every query ({notBurst} managed)");
        Check(mismatches == 0, $"{label}: {queries.Count} queries match managed (worst relative {worst:E2}, {mismatches} over 1e-10)");
    }

    private static int Mismatch(double actual, double expected, ref double worst)
    {
        if (double.IsNaN(expected)) return double.IsNaN(actual) ? 0 : 1;
        double relative = Math.Abs(actual - expected) / Math.Max(1, Math.Abs(expected));
        worst = Math.Max(worst, relative);
        return relative <= 1e-10 ? 0 : 1;
    }

    private void Check(bool condition, string what)
    {
        _log.AppendLine((condition ? "ok   " : "FAIL ") + what);
        _pass &= condition;
    }

    private static string ResultPath()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "-rcurveResult") return args[i + 1];
        return Path.Combine(Application.persistentDataPath, "rcurve-player-check.txt");
    }

    public static AnimationCurve RandomCurve(System.Random random)
    {
        int count = 2 + random.Next(4);
        var keys = new Keyframe[count];
        float time = 0;
        for (int i = 0; i < count; i++)
        {
            float outTangent = random.Next(12) == 0 ? float.PositiveInfinity : Range(random, -5, 5);
            var key = new Keyframe(time, Range(random, -2, 2), Range(random, -5, 5), outTangent);
            WeightedMode mode = WeightedMode.None;
            if (random.Next(2) == 0) mode |= WeightedMode.In;
            if (random.Next(2) == 0) mode |= WeightedMode.Out;
            key.weightedMode = mode;
            key.inWeight = Range(random, 0.05f, 0.95f);
            key.outWeight = Range(random, 0.05f, 0.95f);
            keys[i] = key;
            time += Range(random, 0.2f, 2f);
        }
        return new AnimationCurve(keys) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.PingPong };
    }

    private static float Range(System.Random random, float min, float max) => (float)(min + random.NextDouble() * (max - min));

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
    private struct EvaluateJob : IJobParallelFor
    {
        public NativeRationalCurve Curve;
        [ReadOnly] public NativeArray<double> Times;
        [WriteOnly] public NativeArray<double> Values;
        [WriteOnly] public NativeArray<double> Slopes;
        [WriteOnly] public NativeArray<byte> Burst;

        public void Execute(int index)
        {
            bool isBurst = true;
            MarkManaged(ref isBurst);
            Burst[index] = (byte)(isBurst ? 1 : 0);
            Values[index] = Curve.Evaluate(Times[index]);
            Slopes[index] = Curve.EvaluateSlope(Times[index]);
        }

        [BurstDiscard]
        private static void MarkManaged(ref bool isBurst) { isBurst = false; }
    }
}

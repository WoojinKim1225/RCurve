using System;
using System.Diagnostics;
using NUnit.Framework;
using RCurve;
using RCurve.Editor;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public class CurveLookupStrategyTests
{
    [Serializable] private sealed class Row { public string Mode; public string ManagedStrategy; public string BurstStrategy; public int LutBytes; public double ManagedMs; public double BurstMs; }
    [Serializable] private sealed class Report { public int Keys; public int Segments; public bool Clustered; public int Evaluations; public Row[] Results; }

    [TestCase(4, false), TestCase(16, false), TestCase(64, false), TestCase(64, true), TestCase(256, false), Category("Performance")]
    public void StrategiesMatchAndCalibrateAuto(int keyCount, bool clustered)
    {
        const int count = 262144, rounds = 5;
        var keys = new Keyframe[keyCount];
        var random = new System.Random(4907);
        for (int i = 0; i < keyCount; i++)
        {
            double u = i / (double)(keyCount - 1);
            double time = clustered && i < keyCount - 1 ? u * 0.01 : (-0.1 + Math.Sqrt(0.01 + 3.6 * u)) / 1.8;
            keys[i] = new Keyframe((float)time, (float)Math.Sin(6 * time), (float)(6 * Math.Cos(6 * time)), (float)(6 * Math.Cos(6 * time)),
                (float)(0.2 + 0.4 * random.NextDouble()), (float)(0.2 + 0.4 * random.NextDouble())) { weightedMode = WeightedMode.Both };
        }
        var source = new AnimationCurve(keys) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.PingPong };
        var baked = RationalCurveBuilder.Build(source, new CurveFitSettings(CurveFitObjective.LeastSquares));
        double[] knotTimes = baked.KeyTimes.ToArray(), knotValues = baked.KeyValues.ToArray();
        var segments = baked.Segments.ToArray();
        var curves = new BakedRationalCurve[5];
        var native = new NativeRationalCurve[5];
        var queries = new double[count];
        var expected = new double[count];
        var output = new double[count];
        using var nativeQueries = new NativeArray<double>(count, Allocator.Persistent);
        using var nativeOutput = new NativeArray<double>(count, Allocator.Persistent);
        using var flag = new NativeArray<byte>(1, Allocator.Persistent);
        bool enabled = BurstCompiler.Options.EnableBurstCompilation, safety = BurstCompiler.Options.EnableBurstSafetyChecks;
        try
        {
            BurstCompiler.Options.EnableBurstCompilation = true;
            BurstCompiler.Options.EnableBurstSafetyChecks = true;
            curves[0] = new BakedRationalCurve(knotTimes, knotValues, segments, baked.PreWrapMode, baked.PostWrapMode);
            var modes = new[] { CurveLookupMode.BruteForce, CurveLookupMode.LUT, CurveLookupMode.Magic, CurveLookupMode.Auto };
            for (int i = 1; i < curves.Length; i++)
            {
                CurveLookupPlanner.Plan(knotTimes, baked.IndexPrediction, modes[i - 1], out var managed, out var burst, out var table, out double scale);
                curves[i] = new BakedRationalCurve(knotTimes, knotValues, segments, baked.PreWrapMode, baked.PostWrapMode,
                    baked.IndexPrediction, modes[i - 1], managed, burst, table, scale);
            }
            for (int i = 0; i < count; i++) queries[i] = (float)(i % 10 == 0 ? -2 + 5 * random.NextDouble() : random.NextDouble());
            // Exact keys and their representable neighbours, not only random points.
            for (int i = 0; i < knotTimes.Length; i++)
            {
                double t = knotTimes[i]; long bits = BitConverter.DoubleToInt64Bits(t);
                queries[3 * i] = t;
                queries[3 * i + 1] = t == 0 ? -double.Epsilon : BitConverter.Int64BitsToDouble(bits - 1);
                queries[3 * i + 2] = BitConverter.Int64BitsToDouble(bits + 1);
            }
            nativeQueries.CopyFrom(queries);
            for (int i = 0; i < count; i++) expected[i] = curves[0].Evaluate(queries[i]);
            var report = new Report { Keys = keyCount, Segments = baked.SegmentCount, Clustered = clustered, Evaluations = count, Results = new Row[5] };
            var managedTimes = new double[5][]; var burstTimes = new double[5][];
            for (int mode = 0; mode < 5; mode++)
            {
                native[mode] = NativeRationalCurve.Create(curves[mode], Allocator.Persistent);
                new EvaluateJob { Curve = native[mode], Times = nativeQueries, Values = nativeOutput, Flag = flag }.Run();
                Assert.That(flag[0], Is.EqualTo(1));
                double maximum = 0;
                for (int i = 0; i < count; i++) maximum = Math.Max(maximum, Math.Max(Math.Abs(curves[mode].Evaluate(queries[i]) - expected[i]), Math.Abs(nativeOutput[i] - expected[i])));
                Assert.That(maximum, Is.EqualTo(0), $"mode={mode}");
                new EvaluateJob { Curve = native[mode], Times = nativeQueries, Values = nativeOutput, Flag = flag, Slopes = true }.Run();
                Assert.That(flag[0], Is.EqualTo(1));
                for (int i = 0; i < Math.Max(1024, 3 * knotTimes.Length); i++)
                {
                    double slope = curves[0].EvaluateSlope(queries[i]);
                    Assert.That(curves[mode].EvaluateSlope(queries[i]), Is.EqualTo(slope));
                    Assert.That(nativeOutput[i], Is.EqualTo(slope));
                }
                var wrapper = new RationalCurve(source, new CurveFitSettings(lookupMode: curves[mode].LookupMode));
                wrapper.StoreBuild(curves[mode], wrapper.InputHash);
                var restored = JsonUtility.FromJson<RationalCurve>(JsonUtility.ToJson(wrapper)).BakedCurve;
                Assert.That(restored.ManagedLookup, Is.EqualTo(curves[mode].ManagedLookup));
                Assert.That(restored.BurstLookup, Is.EqualTo(curves[mode].BurstLookup));
                Assert.That(restored.LookupTable.ToArray(), Is.EqualTo(curves[mode].LookupTable.ToArray()));
                Assert.That(curves[mode].LookupTable.Length * sizeof(int), Is.LessThanOrEqualTo(CurveLookupPlanner.MaximumLutBytes));
                managedTimes[mode] = new double[rounds]; burstTimes[mode] = new double[rounds];
            }
            var watch = new Stopwatch();
            for (int round = 0; round < rounds; round++)
                for (int order = 0; order < 5; order++)
                {
                    int mode = (round + order) % 5;
                    watch.Restart(); for (int i = 0; i < count; i++) output[i] = curves[mode].Evaluate(queries[i]); watch.Stop();
                    managedTimes[mode][round] = watch.Elapsed.TotalMilliseconds;
                    watch.Restart(); new EvaluateJob { Curve = native[mode], Times = nativeQueries, Values = nativeOutput, Flag = flag }.Run(); watch.Stop();
                    burstTimes[mode][round] = watch.Elapsed.TotalMilliseconds;
                }
            for (int mode = 0; mode < 5; mode++)
            {
                Array.Sort(managedTimes[mode]); Array.Sort(burstTimes[mode]);
                report.Results[mode] = new Row { Mode = mode == 0 ? "BinarySearch" : modes[mode - 1].ToString(),
                    ManagedStrategy = curves[mode].ManagedLookup.ToString(), BurstStrategy = curves[mode].BurstLookup.ToString(),
                    LutBytes = curves[mode].LookupTable.Length * sizeof(int), ManagedMs = managedTimes[mode][2], BurstMs = burstTimes[mode][2] };
            }
            TestContext.WriteLine("RCURVE_LOOKUP_BENCHMARK " + JsonUtility.ToJson(report));
        }
        finally
        {
            foreach (var curve in native) if (curve.IsCreated) curve.Dispose();
            BurstCompiler.Options.EnableBurstSafetyChecks = safety; BurstCompiler.Options.EnableBurstCompilation = enabled;
        }
    }

    [Test]
    public void LutValidatesIndicesAndFallsBackWhenCellDataDoesNotBracketTime()
    {
        double[] times = { 0, 0.2, 0.8, 1 }, values = { 0, 1, 2, 3 };
        var segments = new RationalCurveSegment[3];
        Assert.Throws<ArgumentException>(() => new BakedRationalCurve(times, values, segments,
            managedLookup: CurveLookupStrategy.LUT, lookupTable: new[] { 2, 0 }, lookupScale: 1));
        Assert.Throws<ArgumentException>(() => new BakedRationalCurve(times, values, segments,
            managedLookup: CurveLookupStrategy.LUT, lookupTable: new[] { 0, 9 }, lookupScale: 1));
        var wrongBounds = new BakedRationalCurve(times, values, segments, managedLookup: CurveLookupStrategy.LUT,
            lookupTable: new[] { 0, 0 }, lookupScale: 1);
        using var native = NativeRationalCurve.Create(wrongBounds, Allocator.TempJob);
        Assert.That(wrongBounds.Evaluate(0.9), Is.EqualTo(2));
        Assert.That(native.Evaluate(0.9), Is.EqualTo(2));
        Assert.That(new CurveFitSettings().LookupMode, Is.EqualTo(CurveLookupMode.Auto));
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
    private struct EvaluateJob : IJob
    {
        public NativeRationalCurve Curve;
        public bool Slopes;
        [ReadOnly] public NativeArray<double> Times;
        [WriteOnly] public NativeArray<double> Values;
        [WriteOnly] public NativeArray<byte> Flag;
        public void Execute()
        {
            bool burst = true; MarkManaged(ref burst); Flag[0] = (byte)(burst ? 1 : 0);
            if (Slopes)
                for (int i = 0; i < Times.Length; i++) Values[i] = Curve.EvaluateSlope(Times[i]);
            else
                for (int i = 0; i < Times.Length; i++) Values[i] = Curve.Evaluate(Times[i]);
        }
        [BurstDiscard] private static void MarkManaged(ref bool value) { value = false; }
    }
}

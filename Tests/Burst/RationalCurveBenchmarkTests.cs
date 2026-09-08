using System;
using System.Diagnostics;
using NUnit.Framework;
using RCurve;
using RCurve.Editor;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Random = System.Random;

public class RationalCurveBenchmarkTests
{
    [Serializable]
    private sealed class Row
    {
        public string Name;
        public double MedianMilliseconds;
        public double NanosecondsPerEvaluate;
        public double SpeedupVsUnity;
        public long MedianManagedAllocationBytes;
    }

    [Serializable]
    private sealed class Report
    {
        public string UnityVersion;
        public string BurstVersion;
        public string Cpu;
        public string Conditions;
        public int Seed;
        public int Curves;
        public int PointsPerCurve;
        public int EvaluationsPerRound;
        public int Rounds;
        public int SourceKeys;
        public int BakedSegments;
        public bool DenseNonuniform;
        public string FitObjective;
        public int PredictedCurves;
        public int CubicPredictedCurves;
        public double MaximumErrorVsBinarySearch;
        public double BuildAndStoreMilliseconds;
        public double NativeSetupMilliseconds;
        public double BurstWarmupMilliseconds;
        public double MaximumErrorVsUnity;
        public double MaximumBurstErrorVsManaged;
        public Row[] Results;
    }

    [TestCase(false), TestCase(true), Category("Performance")]
    public void WeightedRandomPointStressAndEvaluateBenchmark(bool denseNonuniform)
    {
        const int curveCount = 16;
        const int pointsPerCurve = 32768;
        const int rounds = 5;
        const int seed = 913507;
        var random = new Random(seed);
        var sources = new AnimationCurve[curveCount];
        var curves = new RationalCurve[curveCount];
        var binaryCurves = new RationalCurve[curveCount];
        var binaryValues = new double[curveCount][];
        var binaryNative = new NativeRationalCurve[curveCount];
        var queries = new double[curveCount][];
        var unityValues = new double[curveCount][];
        var managedValues = new double[curveCount][];
        var native = new NativeRationalCurve[curveCount];
        var nativeQueries = new NativeArray<double>[curveCount];
        var nativeValues = new NativeArray<double>[curveCount];
        var burstFlags = new NativeArray<byte>[curveCount];
        var report = new Report
        {
            UnityVersion = Application.unityVersion,
            BurstVersion = Array.Find(UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages(), package => package.name == "com.unity.burst")?.version ?? "unknown",
            Cpu = SystemInfo.processorType,
            Conditions = "Editor; single-thread IJob.Run; Strict/High; Burst safety checks enabled; warmed median of 5; Evaluate only; identical float-representable times and double outputs; no parallel speedup",
            DenseNonuniform = denseNonuniform,
            FitObjective = denseNonuniform ? "LeastSquares" : "Minimax",
            Seed = seed, Curves = curveCount, PointsPerCurve = pointsPerCurve,
            EvaluationsPerRound = curveCount * pointsPerCurve, Rounds = rounds
        };
        bool enabled = BurstCompiler.Options.EnableBurstCompilation;
        bool safety = BurstCompiler.Options.EnableBurstSafetyChecks;
        try
        {
            BurstCompiler.Options.EnableBurstCompilation = true;
            BurstCompiler.Options.EnableBurstSafetyChecks = true;
            for (int c = 0; c < curveCount; c++)
            {
                int keyCount = denseNonuniform ? 33 + 16 * (c % 4) : 3 + c % 4;
                var keys = new Keyframe[keyCount];
                for (int k = 0; k < keyCount; k++)
                {
                    keys[k] = new Keyframe(-2 + 5f * k / (keyCount - 1),
                        (float)(2 * random.NextDouble() - 1),
                        (float)(4 * random.NextDouble() - 2), (float)(4 * random.NextDouble() - 2),
                        (float)(0.15 + 0.5 * random.NextDouble()), (float)(0.15 + 0.5 * random.NextDouble()))
                        { weightedMode = WeightedMode.Both };
                    if (denseNonuniform)
                    {
                        double u = (-0.1 + Math.Sqrt(0.01 + 3.6 * k / (keyCount - 1))) / 1.8;
                        keys[k].time = (float)(-2 + 5 * u);
                        keys[k].value = (float)Math.Sin(6 * u) + 0.02f * keys[k].value;
                        keys[k].inTangent = (float)(1.2 * Math.Cos(6 * u)) + 0.2f * keys[k].inTangent;
                        keys[k].outTangent = (float)(1.2 * Math.Cos(6 * u)) + 0.2f * keys[k].outTangent;
                    }
                }
                sources[c] = new AnimationCurve(keys)
                {
                    preWrapMode = c % 3 == 0 ? WrapMode.Loop : c % 3 == 1 ? WrapMode.PingPong : WrapMode.ClampForever,
                    postWrapMode = c % 2 == 0 ? WrapMode.Loop : WrapMode.PingPong
                };
                report.SourceKeys += keyCount;
                queries[c] = new double[pointsPerCurve];
                unityValues[c] = new double[pointsPerCurve];
                managedValues[c] = new double[pointsPerCurve];
                binaryValues[c] = new double[pointsPerCurve];
                for (int i = 0; i < pointsPerCurve; i++)
                    queries[c][i] = (float)(i % 10 == 0 ? -12 + 25 * random.NextDouble() : -2 + 5 * random.NextDouble());
                queries[c][0] = -2;
                queries[c][1] = 3;
            }
            var watch = Stopwatch.StartNew();
            for (int c = 0; c < curveCount; c++)
            {
                // The dense fixture exposes an existing Minimax simplex iteration limit, before index fitting.
                // Use LS explicitly here to isolate lookup performance; the original fixture remains Minimax.
                curves[c] = new RationalCurve(sources[c], new CurveFitSettings(
                    denseNonuniform ? CurveFitObjective.LeastSquares : CurveFitObjective.Minimax, lookupMode: CurveLookupMode.Magic));
                curves[c].StoreBuild(RationalCurveBuilder.Build(sources[c], curves[c].Settings), curves[c].InputHash);
                report.BakedSegments += curves[c].BakedCurve.SegmentCount;
                var prediction = curves[c].BakedCurve.IndexPrediction;
                if (prediction.IsEnabled) report.PredictedCurves++;
                if (prediction.IsEnabled && (prediction.Cubic != 0 || prediction.Quadratic != 0)) report.CubicPredictedCurves++;
            }
            watch.Stop();
            report.BuildAndStoreMilliseconds = watch.Elapsed.TotalMilliseconds;
            for (int c = 0; c < curveCount; c++)
            {
                var baked = curves[c].BakedCurve;
                binaryCurves[c] = new RationalCurve(sources[c]);
                binaryCurves[c].StoreBuild(new BakedRationalCurve(baked.KeyTimes.ToArray(), baked.KeyValues.ToArray(),
                    baked.Segments.ToArray(), baked.PreWrapMode, baked.PostWrapMode), binaryCurves[c].InputHash);
            }
            watch.Restart();
            for (int c = 0; c < curveCount; c++)
            {
                native[c] = NativeRationalCurve.Create(curves[c].BakedCurve, Allocator.Persistent);
                binaryNative[c] = NativeRationalCurve.Create(binaryCurves[c].BakedCurve, Allocator.Persistent);
                nativeQueries[c] = new NativeArray<double>(queries[c], Allocator.Persistent);
                nativeValues[c] = new NativeArray<double>(pointsPerCurve, Allocator.Persistent);
                burstFlags[c] = new NativeArray<byte>(1, Allocator.Persistent);
            }
            watch.Stop();
            report.NativeSetupMilliseconds = watch.Elapsed.TotalMilliseconds;

            Action[] evaluate =
            {
                () =>
                {
                    for (int c = 0; c < curveCount; c++)
                        for (int i = 0; i < pointsPerCurve; i++) unityValues[c][i] = sources[c].Evaluate((float)queries[c][i]);
                },
                () =>
                {
                    for (int c = 0; c < curveCount; c++)
                        for (int i = 0; i < pointsPerCurve; i++) managedValues[c][i] = curves[c].Evaluate(queries[c][i]);
                },
                () =>
                {
                    for (int c = 0; c < curveCount; c++)
                        new EvaluateJob { Curve = native[c], Times = nativeQueries[c], Values = nativeValues[c], BurstExecuted = burstFlags[c] }.Run();
                },
                () =>
                {
                    for (int c = 0; c < curveCount; c++)
                        for (int i = 0; i < pointsPerCurve; i++) binaryValues[c][i] = binaryCurves[c].Evaluate(queries[c][i]);
                },
                () =>
                {
                    for (int c = 0; c < curveCount; c++)
                        new EvaluateJob { Curve = binaryNative[c], Times = nativeQueries[c], Values = nativeValues[c], BurstExecuted = burstFlags[c] }.Run();
                }
            };
            evaluate[0]();
            evaluate[1]();
            watch.Restart();
            evaluate[2](); // Synchronous Burst compilation and the first batch are outside measured rounds.
            watch.Stop();
            report.BurstWarmupMilliseconds = watch.Elapsed.TotalMilliseconds;
            for (int c = 0; c < curveCount; c++)
            {
                Assert.That(burstFlags[c][0], Is.EqualTo(1), "Managed fallback is not a Burst benchmark.");
                for (int i = 0; i < pointsPerCurve; i++)
                {
                    double value = managedValues[c][i];
                    Assert.That(double.IsNaN(value) || double.IsInfinity(value), Is.False);
                    report.MaximumErrorVsUnity = Math.Max(report.MaximumErrorVsUnity, Math.Abs(value - unityValues[c][i]));
                    report.MaximumBurstErrorVsManaged = Math.Max(report.MaximumBurstErrorVsManaged, Math.Abs(value - nativeValues[c][i]));
                }
            }
            evaluate[3]();
            evaluate[4]();
            for (int c = 0; c < curveCount; c++)
            {
                Assert.That(burstFlags[c][0], Is.EqualTo(1));
                for (int i = 0; i < pointsPerCurve; i++)
                {
                    report.MaximumErrorVsBinarySearch = Math.Max(report.MaximumErrorVsBinarySearch,
                        Math.Max(Math.Abs(managedValues[c][i] - binaryValues[c][i]), Math.Abs(managedValues[c][i] - nativeValues[c][i])));
                }
            }
            var elapsed = new double[5][];
            var allocations = new long[5][];
            for (int method = 0; method < 5; method++) { elapsed[method] = new double[rounds]; allocations[method] = new long[rounds]; }
            for (int round = 0; round < rounds; round++)
                for (int order = 0; order < 5; order++)
                {
                    int method = (round + order) % 5; // Rotate order to reduce fixed-order bias.
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    watch.Restart();
                    evaluate[method]();
                    watch.Stop();
                    allocations[method][round] = GC.GetAllocatedBytesForCurrentThread() - before;
                    elapsed[method][round] = watch.Elapsed.TotalMilliseconds;
                }
            report.Results = new Row[5];
            string[] names = { "AnimationCurve.Evaluate", "RationalCurve.Evaluate (index prediction)", "NativeRationalCurve.Evaluate (Burst index prediction)",
                "RationalCurve.Evaluate (binary search control)", "NativeRationalCurve.Evaluate (Burst binary search control)" };
            for (int method = 0; method < 5; method++)
            {
                Array.Sort(elapsed[method]);
                Array.Sort(allocations[method]);
                double median = elapsed[method][rounds / 2];
                report.Results[method] = new Row
                {
                    Name = names[method], MedianMilliseconds = median,
                    NanosecondsPerEvaluate = median * 1e6 / report.EvaluationsPerRound,
                    SpeedupVsUnity = elapsed[0][rounds / 2] / median,
                    MedianManagedAllocationBytes = allocations[method][rounds / 2]
                };
            }
            TestContext.WriteLine("RCURVE_BENCHMARK " + JsonUtility.ToJson(report));
            Assert.That(report.MaximumErrorVsUnity, Is.LessThanOrEqualTo(0.0011), "Random-point accuracy check, not a continuous error proof.");
            Assert.That(report.MaximumBurstErrorVsManaged, Is.LessThanOrEqualTo(1e-9));
            Assert.That(report.MaximumErrorVsBinarySearch, Is.EqualTo(0));
            // No timing pass/fail threshold: machine load and Editor instrumentation affect performance.
        }
        finally
        {
            for (int c = 0; c < curveCount; c++)
            {
                if (native[c].IsCreated) native[c].Dispose();
                if (binaryNative[c].IsCreated) binaryNative[c].Dispose();
                if (nativeQueries[c].IsCreated) nativeQueries[c].Dispose();
                if (nativeValues[c].IsCreated) nativeValues[c].Dispose();
                if (burstFlags[c].IsCreated) burstFlags[c].Dispose();
            }
            BurstCompiler.Options.EnableBurstSafetyChecks = safety;
            BurstCompiler.Options.EnableBurstCompilation = enabled;
        }
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
    private struct EvaluateJob : IJob
    {
        public NativeRationalCurve Curve;
        [ReadOnly] public NativeArray<double> Times;
        [WriteOnly] public NativeArray<double> Values;
        [WriteOnly] public NativeArray<byte> BurstExecuted;
        public void Execute()
        {
            bool burst = true;
            MarkManaged(ref burst);
            BurstExecuted[0] = (byte)(burst ? 1 : 0);
            for (int i = 0; i < Times.Length; i++) Values[i] = Curve.Evaluate(Times[i]);
        }
        [BurstDiscard] private static void MarkManaged(ref bool burst) { burst = false; }
    }
}

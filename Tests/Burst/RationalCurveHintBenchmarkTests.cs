using System;
using System.Diagnostics;
using NUnit.Framework;
using RCurve;
using RCurve.Editor;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public class RationalCurveHintBenchmarkTests
{
    [Serializable] private sealed class Row
    {
        public string Pattern;
        public double UnityMilliseconds;
        public double ManagedMilliseconds;
        public double ManagedHintMilliseconds;
        public double BurstMilliseconds;
        public double BurstHintMilliseconds;
    }
    [Serializable] private sealed class Report
    {
        public string UnityVersion;
        public string Cpu;
        public string Conditions;
        public bool Dense;
        public int Curves;
        public int Segments;
        public int Evaluations;
        public int Rounds;
        public double MaximumHintError;
        public double MaximumUnityError;
        public Row[] Results;
    }

    [TestCase(false), TestCase(true), Category("Performance")]
    public void OptionalIntervalHintSequentialStressBenchmark(bool dense)
    {
        const int curveCount = 16, count = 32768, rounds = 5;
        var sources = new AnimationCurve[curveCount];
        var curves = new BakedRationalCurve[curveCount];
        var native = new NativeRationalCurve[curveCount];
        var times = new double[count];
        var output = new double[curveCount][];
        var nativeTimes = new NativeArray<double>(count, Allocator.Persistent);
        var nativeValues = new NativeArray<double>(count, Allocator.Persistent);
        var flag = new NativeArray<byte>(1, Allocator.Persistent);
        var report = new Report
        {
            UnityVersion = Application.unityVersion, Cpu = SystemInfo.processorType, Dense = dense,
            Conditions = "LeastSquares/High; single-thread; Burst Strict/High and safety checks ON; setup/compilation excluded; 256 samples/traversal; median 5 with rotating order; hints reset once per curve/batch, not per sample",
            Curves = curveCount, Evaluations = curveCount * count, Rounds = rounds, Results = new Row[5]
        };
        bool enabled = BurstCompiler.Options.EnableBurstCompilation;
        bool safety = BurstCompiler.Options.EnableBurstSafetyChecks;
        try
        {
            BurstCompiler.Options.EnableBurstCompilation = true;
            BurstCompiler.Options.EnableBurstSafetyChecks = true;
            var random = new System.Random(8719);
            for (int c = 0; c < curveCount; c++)
            {
                int keyCount = dense ? 33 + 16 * (c % 4) : 3 + c % 4;
                var keys = new Keyframe[keyCount];
                for (int k = 0; k < keyCount; k++)
                {
                    double u = (-0.1 + Math.Sqrt(0.01 + 3.6 * k / (keyCount - 1))) / 1.8;
                    double phase = 6 * u + c * 0.1;
                    keys[k] = new Keyframe((float)(-2 + 5 * u), (float)(Math.Sin(phase) + 0.01 * random.NextDouble()),
                        (float)(1.2 * Math.Cos(phase)), (float)(1.2 * Math.Cos(phase)),
                        (float)(0.15 + 0.5 * random.NextDouble()), (float)(0.15 + 0.5 * random.NextDouble()))
                        { weightedMode = WeightedMode.Both };
                }
                sources[c] = new AnimationCurve(keys) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.Loop };
                curves[c] = RationalCurveBuilder.Build(sources[c], new CurveFitSettings(CurveFitObjective.LeastSquares, lookupMode: CurveLookupMode.Magic));
                native[c] = NativeRationalCurve.Create(curves[c], Allocator.Persistent);
                output[c] = new double[count];
                report.Segments += curves[c].SegmentCount;
            }
            string[] names = { "forward", "reverse", "loop", "seek-1-percent", "random" };
            var watch = new Stopwatch();
            Action[] actions =
            {
                () => { for (int c = 0; c < curveCount; c++) for (int i = 0; i < count; i++) output[c][i] = sources[c].Evaluate((float)times[i]); },
                () => RunManaged(curves, times, output, false),
                () => RunManaged(curves, times, output, true),
                () => RunBurst(native, nativeTimes, nativeValues, flag, false),
                () => RunBurst(native, nativeTimes, nativeValues, flag, true)
            };
            for (int pattern = 0; pattern < names.Length; pattern++)
            {
                Fill(times, pattern);
                nativeTimes.CopyFrom(times);
                // Compare every input before timing. All paths use identical float-representable times.
                for (int c = 0; c < curveCount; c++)
                {
                    int hint = -1;
                    for (int i = 0; i < count; i++)
                    {
                        output[c][i] = curves[c].Evaluate(times[i]);
                        report.MaximumHintError = Math.Max(report.MaximumHintError, Math.Abs(output[c][i] - curves[c].Evaluate(times[i], ref hint)));
                        report.MaximumUnityError = Math.Max(report.MaximumUnityError, Math.Abs(output[c][i] - sources[c].Evaluate((float)times[i])));
                    }
                    for (int mode = 0; mode < 2; mode++)
                    {
                        new EvaluateJob { Curve = native[c], Times = nativeTimes, Values = nativeValues, BurstExecuted = flag, Hinted = mode == 1 }.Run();
                        Assert.That(flag[0], Is.EqualTo(1), "Managed fallback is not a Burst benchmark.");
                        for (int i = 0; i < count; i++)
                            report.MaximumHintError = Math.Max(report.MaximumHintError, Math.Abs(output[c][i] - nativeValues[i]));
                    }
                }
                foreach (var action in actions) action();
                var elapsed = new double[5][];
                for (int i = 0; i < 5; i++) elapsed[i] = new double[rounds];
                for (int round = 0; round < rounds; round++)
                    for (int order = 0; order < 5; order++)
                    {
                        int method = (round + order) % 5;
                        watch.Restart(); actions[method](); watch.Stop();
                        elapsed[method][round] = watch.Elapsed.TotalMilliseconds;
                    }
                foreach (var measurements in elapsed) Array.Sort(measurements);
                report.Results[pattern] = new Row
                {
                    Pattern = names[pattern], UnityMilliseconds = elapsed[0][2], ManagedMilliseconds = elapsed[1][2],
                    ManagedHintMilliseconds = elapsed[2][2], BurstMilliseconds = elapsed[3][2], BurstHintMilliseconds = elapsed[4][2]
                };
            }
            TestContext.WriteLine("RCURVE_HINT_BENCHMARK " + JsonUtility.ToJson(report));
            Assert.That(report.MaximumHintError, Is.EqualTo(0));
            Assert.That(report.MaximumUnityError, Is.LessThanOrEqualTo(0.0011));
        }
        finally
        {
            for (int i = 0; i < native.Length; i++) if (native[i].IsCreated) native[i].Dispose();
            nativeTimes.Dispose(); nativeValues.Dispose(); flag.Dispose();
            BurstCompiler.Options.EnableBurstSafetyChecks = safety;
            BurstCompiler.Options.EnableBurstCompilation = enabled;
        }
    }

    private static void Fill(double[] times, int pattern)
    {
        var random = new System.Random(1927);
        double cursor = -2;
        for (int i = 0; i < times.Length; i++)
        {
            double time;
            if (pattern == 0) time = -2 + 5 * (i % 256) / 255.0;
            else if (pattern == 1) time = 3 - 5 * (i % 256) / 255.0;
            else if (pattern == 2) time = -2 + 5 * i / 256.0;
            else if (pattern == 3)
            {
                cursor = i % 100 == 0 ? -12 + 25 * random.NextDouble() : cursor + 5.0 / 256;
                time = cursor;
            }
            else time = i % 10 == 0 ? -12 + 25 * random.NextDouble() : -2 + 5 * random.NextDouble();
            times[i] = (float)time;
        }
    }

    private static void RunManaged(BakedRationalCurve[] curves, double[] times, double[][] output, bool hinted)
    {
        for (int c = 0; c < curves.Length; c++)
        {
            int hint = -1;
            if (hinted)
                for (int i = 0; i < times.Length; i++) output[c][i] = curves[c].Evaluate(times[i], ref hint);
            else
                for (int i = 0; i < times.Length; i++) output[c][i] = curves[c].Evaluate(times[i]);
        }
    }

    private static void RunBurst(NativeRationalCurve[] curves, NativeArray<double> times, NativeArray<double> values,
        NativeArray<byte> flag, bool hinted)
    {
        for (int c = 0; c < curves.Length; c++)
            new EvaluateJob { Curve = curves[c], Times = times, Values = values, BurstExecuted = flag, Hinted = hinted }.Run();
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
    private struct EvaluateJob : IJob
    {
        public NativeRationalCurve Curve;
        [ReadOnly] public NativeArray<double> Times;
        [WriteOnly] public NativeArray<double> Values;
        [WriteOnly] public NativeArray<byte> BurstExecuted;
        public bool Hinted;
        public void Execute()
        {
            bool burst = true; MarkManaged(ref burst); BurstExecuted[0] = (byte)(burst ? 1 : 0);
            int hint = -1;
            if (Hinted)
                for (int i = 0; i < Times.Length; i++) Values[i] = Curve.Evaluate(Times[i], ref hint);
            else
                for (int i = 0; i < Times.Length; i++) Values[i] = Curve.Evaluate(Times[i]);
        }
        [BurstDiscard] private static void MarkManaged(ref bool burst) { burst = false; }
    }
}

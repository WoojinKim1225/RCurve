using System;
using System.Collections.Generic;
using NUnit.Framework;
using RCurve;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public class NativeRationalCurveTests
{
    private bool _wasBurstEnabled;

    [SetUp]
    public void EnableBurst()
    {
        _wasBurstEnabled = BurstCompiler.Options.EnableBurstCompilation;
        BurstCompiler.Options.EnableBurstCompilation = true;
    }

    [TearDown]
    public void RestoreBurstSetting()
    {
        BurstCompiler.Options.EnableBurstCompilation = _wasBurstEnabled;
    }

    [Test]
    public void BurstMatchesManagedAcrossDegreesAndIndependentWrapModes(
        [Values(3, 4, 5, 6)] int degree, [Values] CurveWrapMode pre, [Values] CurveWrapMode post)
    {
        double[] times = { -2, 1, 2, 5 };
        double[] values = { -3, 4, -2, 3 };
        var segments = new[]
        {
            Segment(-2, 1, -3, 4, degree),
            null, // Internal discontinuity: verifies a Constant entry between rational coefficient blocks.
            Segment(2, 5, -2, 3, degree)
        };
        AssertBurstParity(new BakedRationalCurve(times, values, segments, pre, post), BoundaryQueries());
    }

    [TestCase(0), TestCase(1), TestCase(2)]
    public void ExplicitMagicMatchesBinaryInBurstAtKeysAndAdjacentDoubles(int shape)
    {
        const int count = 65;
        var keys = new Keyframe[count];
        for (int i = 0; i < count; i++)
        {
            double u = i / (double)(count - 1);
            double time = shape == 1 ? (-0.1 + Math.Sqrt(0.01 + 3.6 * u)) / 1.8 :
                shape == 2 && i < count - 1 ? u * 0.01 : u;
            keys[i] = new Keyframe((float)(-2 + 3 * time), i, float.PositiveInfinity, float.PositiveInfinity);
        }
        var source = new AnimationCurve(keys) { preWrapMode = WrapMode.PingPong, postWrapMode = WrapMode.Loop };
        var curve = RationalCurveBuilder.FromSegments(source, new RationalCurveSegment[count - 1], CurveLookupMode.Magic);
        var expected = shape == 2 ? CurveLookupStrategy.BinarySearch : CurveLookupStrategy.Magic;
        Assert.That(curve.ManagedLookup, Is.EqualTo(expected));
        Assert.That(curve.BurstLookup, Is.EqualTo(expected));
        var binary = new BakedRationalCurve(curve.KeyTimes.ToArray(), curve.KeyValues.ToArray(), curve.Segments.ToArray(),
            curve.PreWrapMode, curve.PostWrapMode);
        var queries = new List<double>(BoundaryQueries());
        foreach (double time in curve.KeyTimes)
        {
            long bits = BitConverter.DoubleToInt64Bits(time);
            queries.Add(time);
            queries.Add(time == 0 ? -double.Epsilon : BitConverter.Int64BitsToDouble(bits + (time > 0 ? -1 : 1)));
            queries.Add(time == 0 ? double.Epsilon : BitConverter.Int64BitsToDouble(bits + (time > 0 ? 1 : -1)));
        }
        var random = new System.Random(829 + shape);
        for (int i = 0; i < 10000; i++) queries.Add(-3 + 6 * random.NextDouble());
        foreach (double time in queries)
            if (!double.IsNaN(time) && !double.IsInfinity(time))
            {
                AssertNumber(curve.Evaluate(time), binary.Evaluate(time), time);
                AssertNumber(curve.EvaluateSlope(time), binary.EvaluateSlope(time), time);
            }
        AssertBurstParity(curve, queries.ToArray());
    }

    [Test]
    public void MixedDegreesUseIndependentOffsetsInTheFlatCoefficientBuffer()
    {
        double[] times = { 0, 1, 2, 3, 4 };
        double[] values = { 0, 1, -1, 2, 0 };
        var segments = new RationalCurveSegment[4];
        for (int i = 0; i < segments.Length; i++)
            segments[i] = Segment(times[i], times[i + 1], values[i], values[i + 1], i + 3);
        AssertBurstParity(new BakedRationalCurve(times, values, segments, CurveWrapMode.PingPong, CurveWrapMode.Loop), BoundaryQueries());
    }

    [Test]
    public void EmptyAndSingleKeyCurvesStillHaveValidNativeOwners()
    {
        AssertBurstParity(new BakedRationalCurve(Array.Empty<double>(), Array.Empty<double>(), Array.Empty<RationalCurveSegment>()), BoundaryQueries());
        AssertBurstParity(new BakedRationalCurve(new[] { 2.0 }, new[] { -3.0 }, Array.Empty<RationalCurveSegment>()), BoundaryQueries());
    }

    [Test]
    public void SmallPositiveDenominatorsStayConsistent([Values(3, 4, 5, 6)] int degree)
    {
        var q = new double[degree + 1];
        Array.Fill(q, 1e-6);
        q[0] = 1;
        var interior = new double[degree - 3];
        Array.Fill(interior, 2e-6);
        var segment = new RationalCurveSegment(0, 1, 2, 2, 0, 0, q, interior);
        AssertBurstParity(new BakedRationalCurve(new[] { 0.0, 1 }, new[] { 2.0, 2 }, new[] { segment },
            CurveWrapMode.PingPong, CurveWrapMode.Loop), BoundaryQueries());
    }

    [Test]
    public void SmoothSeamsAndReflectionsPreserveFiniteDerivatives()
    {
        double[] q = { 1, 1, 1, 1 };
        var segment = new RationalCurveSegment(0, 1, 0, 0, 1, 1, q, Array.Empty<double>());
        AssertBurstParity(new BakedRationalCurve(new[] { 0.0, 1 }, new[] { 0.0, 0 }, new[] { segment },
            CurveWrapMode.Loop, CurveWrapMode.Loop), BoundaryQueries());
        segment = new RationalCurveSegment(0, 1, 0, 1, 0, 0, q, Array.Empty<double>());
        AssertBurstParity(new BakedRationalCurve(new[] { 0.0, 1 }, new[] { 0.0, 1 }, new[] { segment },
            CurveWrapMode.PingPong, CurveWrapMode.PingPong), BoundaryQueries());
    }

    [Test]
    public void CreationAndOwnerLifetimeAreExplicit()
    {
        var source = new BakedRationalCurve(new[] { 0.0, 1 }, new[] { 2.0, 5 }, new RationalCurveSegment[1]);
        Assert.Throws<ArgumentNullException>(() => NativeRationalCurve.Create(null, Allocator.TempJob));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeRationalCurve.Create(source, Allocator.None));
        NativeRationalCurve uncreated = default;
        Assert.That(uncreated.IsCreated, Is.False);
        Assert.That(double.IsNaN(uncreated.Evaluate(0)), Is.True);
        var owner = NativeRationalCurve.Create(source, Allocator.Persistent);
        try
        {
            Assert.That(owner.IsCreated, Is.True);
            Assert.That(owner.SegmentCount, Is.EqualTo(1));
            Assert.That(owner.Evaluate(0.5), Is.EqualTo(2));
        }
        finally { owner.Dispose(); }
        Assert.That(owner.IsCreated, Is.False);
        Assert.That(double.IsNaN(owner.Evaluate(0)), Is.True);
        Assert.That(double.IsNaN(owner.EvaluateSlope(0)), Is.True);
        owner.Dispose(); // Only the same cleared owner can be disposed again, not an aliased struct copy.
    }

    private static RationalCurveSegment Segment(double start, double end, double firstValue, double lastValue, int degree)
    {
        var q = new double[degree + 1];
        q[0] = 1;
        for (int i = 1; i <= degree; i++) q[i] = 0.25 + i * 0.2;
        var interior = new double[degree - 3];
        for (int i = 0; i < interior.Length; i++) interior[i] = 0.3 * (i + 1);
        return new RationalCurveSegment(start, end, firstValue, lastValue, 1.5, -0.75, q, interior);
    }

    private static double[] BoundaryQueries()
    {
        var times = new List<double>();
        for (int i = -120; i <= 120; i++)
        {
            double time = i * 0.25;
            times.Add(time - 1.0 / 1024);
            times.Add(time);
            times.Add(time + 1.0 / 1024);
        }
        times.Add(double.MaxValue);
        times.Add(double.MinValue);
        times.Add(double.NaN);
        times.Add(double.PositiveInfinity);
        times.Add(double.NegativeInfinity);
        return times.ToArray();
    }

    private static void AssertBurstParity(BakedRationalCurve source, double[] queries)
    {
        using var curve = NativeRationalCurve.Create(source, Allocator.TempJob);
        using var times = new NativeArray<double>(queries, Allocator.TempJob);
        using var values = new NativeArray<double>(queries.Length, Allocator.TempJob);
        using var derivatives = new NativeArray<double>(queries.Length, Allocator.TempJob);
        using var burstExecuted = new NativeArray<byte>(queries.Length, Allocator.TempJob);
        Assert.That(curve.IsCreated, Is.True);
        Assert.That(curve.PreWrapMode, Is.EqualTo(source.PreWrapMode));
        Assert.That(curve.PostWrapMode, Is.EqualTo(source.PostWrapMode));
        Assert.That(curve.LookupStrategy, Is.EqualTo(source.BurstLookup));
        new EvaluateJob
        {
            Curve = curve, Times = times, Values = values, Derivatives = derivatives, BurstExecuted = burstExecuted
        }.Schedule(queries.Length, 32).Complete();

        for (int i = 0; i < queries.Length; i++)
        {
            Assert.That(burstExecuted[i], Is.EqualTo(1), "Managed fallback is not a Burst test pass.");
            double time = queries[i];
            if (double.IsNaN(time) || double.IsInfinity(time))
            {
                Assert.That(double.IsNaN(values[i]), Is.True);
                Assert.That(double.IsNaN(derivatives[i]), Is.True);
                continue;
            }
            AssertNumber(values[i], source.Evaluate(time), time);
            AssertNumber(derivatives[i], source.EvaluateSlope(time), time);
        }
    }

    private static void AssertNumber(double actual, double expected, double time)
    {
        if (double.IsNaN(expected)) Assert.That(double.IsNaN(actual), Is.True, $"Expected undefined derivative at {time:R}");
        else Assert.That(actual, Is.EqualTo(expected).Within(1e-10 * Math.Max(1, Math.Abs(expected))), $"time={time:R}");
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
    private struct EvaluateJob : IJobParallelFor
    {
        public NativeRationalCurve Curve;
        [ReadOnly] public NativeArray<double> Times;
        [WriteOnly] public NativeArray<double> Values;
        [WriteOnly] public NativeArray<double> Derivatives;
        [WriteOnly] public NativeArray<byte> BurstExecuted;

        public void Execute(int index)
        {
            bool isBurst = true;
            MarkManaged(ref isBurst);
            BurstExecuted[index] = (byte)(isBurst ? 1 : 0);
            Values[index] = Curve.Evaluate(Times[index]);
            Derivatives[index] = Curve.EvaluateSlope(Times[index]);
        }

        [BurstDiscard]
        private static void MarkManaged(ref bool isBurst) { isBurst = false; }
    }
}

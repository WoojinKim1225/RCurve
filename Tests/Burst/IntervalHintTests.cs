using System;
using System.Collections.Generic;
using NUnit.Framework;
using RCurve;
using Unity.Collections;

public class IntervalHintTests
{
    [Test]
    public void OptionalHintsPreserveValuesAndRejectStaleIndices(
        [Values(0, 1, 4)] int keyCount,
        [Values(CurveWrapMode.Default, CurveWrapMode.Once, CurveWrapMode.ClampForever, CurveWrapMode.Loop, CurveWrapMode.PingPong)] CurveWrapMode wrap)
    {
        double[] times = keyCount == 0 ? Array.Empty<double>() : keyCount == 1 ? new double[] { 0 } : new double[] { -2, -0.75, 1.5, 3 };
        double[] values = keyCount == 0 ? Array.Empty<double>() : keyCount == 1 ? new double[] { 7 } : new double[] { 0, 1, 2, 3 };
        var segments = new RationalCurveSegment[Math.Max(0, keyCount - 1)];
        if (keyCount == 4)
        {
            segments[0] = new RationalCurveSegment(-2, -0.75, 0, 1, 1, 0, new double[] { 1, 1, 1, 1 }, Array.Empty<double>());
            segments[2] = new RationalCurveSegment(1.5, 3, 2, 3, -1, 0.5, new double[] { 1, 1, 1, 1 }, Array.Empty<double>());
        }
        var prediction = keyCount == 4 ? new CurveIndexPrediction { Linear = 3, InverseLength = 0.2 } : default;
        var curve = new BakedRationalCurve(times, values, segments, wrap, wrap, prediction);
        using var native = NativeRationalCurve.Create(curve, Allocator.TempJob);
        var queries = new List<double> { -1e200, 1e200, -12, 13, 0, -double.Epsilon, double.Epsilon };
        foreach (double time in times)
        {
            queries.Add(time);
            long bits = BitConverter.DoubleToInt64Bits(time);
            queries.Add(time == 0 ? -double.Epsilon : BitConverter.Int64BitsToDouble(bits - 1));
            queries.Add(BitConverter.Int64BitsToDouble(bits + 1));
        }
        var random = new Random(197);
        for (int i = 0; i < 100; i++) queries.Add(-12 + 25 * random.NextDouble());
        foreach (int initial in new[] { -1, 0, 1, int.MinValue, int.MaxValue })
        {
            int hint = initial, nativeHint = initial;
            foreach (double time in queries)
            {
                Assert.That(curve.Evaluate(time, ref hint), Is.EqualTo(curve.Evaluate(time)));
                Assert.That(native.Evaluate(time, ref nativeHint), Is.EqualTo(native.Evaluate(time)));
                Assert.That(nativeHint, Is.EqualTo(hint));
                if (keyCount < 2) Assert.That(hint, Is.EqualTo(-1));
                else Assert.That(hint, Is.InRange(0, keyCount - 2));
            }
        }
        int invalidHint = int.MaxValue;
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.Evaluate(double.NaN, ref invalidHint));
        Assert.That(double.IsNaN(native.Evaluate(double.NaN, ref invalidHint)), Is.True);
        Assert.That(invalidHint, Is.EqualTo(-1));
    }

    [Test]
    public void UncreatedNativeResetsHintWithoutAccessingBuffers()
    {
        var native = default(NativeRationalCurve);
        int hint = int.MaxValue;
        Assert.That(double.IsNaN(native.Evaluate(0, ref hint)), Is.True);
        Assert.That(hint, Is.EqualTo(-1));
    }
}

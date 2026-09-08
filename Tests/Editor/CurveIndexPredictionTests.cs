using System;
using NUnit.Framework;
using RCurve;
using RCurve.Editor;
using UnityEngine;

public class CurveIndexPredictionTests
{
    [TestCase(2, 0), TestCase(65, 0), TestCase(65, 1), TestCase(257, 1), TestCase(65, 2)]
    public void PredictedSearchMatchesBinaryAtKeysAdjacentDoublesAndRandomTimes(int count, int shape)
    {
        var keys = new Keyframe[count];
        for (int i = 0; i < count; i++)
        {
            double u = i / (double)(count - 1);
            double time = shape == 1 ? (-0.1 + Math.Sqrt(0.01 + 3.6 * u)) / 1.8 :
                shape == 2 && i < count - 1 ? u * 1e-6 : u;
            keys[i] = new Keyframe((float)time, i, float.PositiveInfinity, float.PositiveInfinity);
        }
        var source = new AnimationCurve(keys) { preWrapMode = WrapMode.PingPong, postWrapMode = WrapMode.Loop };
        var curve = RationalCurveBuilder.FromSegments(source, new RationalCurveSegment[count - 1]);
        Assert.That(curve.IndexPrediction.IsEnabled, Is.EqualTo(shape != 2));
        if (shape == 1) Assert.That(Math.Abs(curve.IndexPrediction.Quadratic), Is.GreaterThan(1));
        var binary = new BakedRationalCurve(curve.KeyTimes.ToArray(), curve.KeyValues.ToArray(), curve.Segments.ToArray(),
            curve.PreWrapMode, curve.PostWrapMode);
        var wrapper = new RationalCurve(source);
        wrapper.StoreBuild(curve, wrapper.InputHash);
        var restored = JsonUtility.FromJson<RationalCurve>(JsonUtility.ToJson(wrapper));
        var restoredCurve = restored.BakedCurve;
        Assert.That(restoredCurve.IndexPrediction.IsEnabled, Is.EqualTo(curve.IndexPrediction.IsEnabled));
        for (int i = 0; i <= 100; i++)
            Assert.That(restoredCurve.IndexPrediction.Evaluate(i / 100.0),
                Is.EqualTo(curve.IndexPrediction.Evaluate(i / 100.0)).Within(1e-10));
        // JsonUtility decimal output can round doubles. Compare search paths on identical restored knots.
        var restoredBinary = new BakedRationalCurve(restoredCurve.KeyTimes.ToArray(), restoredCurve.KeyValues.ToArray(),
            restoredCurve.Segments.ToArray(), restoredCurve.PreWrapMode, restoredCurve.PostWrapMode);
        foreach (double time in curve.KeyTimes)
        {
            Check(curve, binary, time);
            Check(restoredCurve, restoredBinary, time);
            long bits = BitConverter.DoubleToInt64Bits(time);
            Check(curve, binary, time == 0 ? -double.Epsilon : BitConverter.Int64BitsToDouble(bits - 1));
            Check(curve, binary, BitConverter.Int64BitsToDouble(bits + 1));
        }
        var random = new System.Random(829 + shape);
        for (int i = 0; i < 10000; i++) Check(curve, binary, -0.5 + 2 * random.NextDouble());
    }

    [Test]
    public void RejectsNonmonotoneAndNonfiniteSerializedPredictors()
    {
        // Matches both end indices, but folds back between them. Endpoint-only validation is insufficient.
        var folded = new CurveIndexPrediction { Cubic = 10, Quadratic = -15, Linear = 6, InverseLength = 1 };
        Assert.That(folded.IsValidFor(new double[] { 0, 1 }), Is.False);
        Assert.Throws<ArgumentException>(() => new BakedRationalCurve(new double[] { 0, 1 }, new double[] { 0, 1 },
            new RationalCurveSegment[1], indexPrediction: folded));
        folded = new CurveIndexPrediction { Linear = double.NaN, InverseLength = 1 };
        Assert.That(folded.IsValidFor(new double[] { 0, 1 }), Is.False);
    }

    private static void Check(BakedRationalCurve curve, BakedRationalCurve binary, double time)
    {
        Assert.That(curve.Evaluate(time), Is.EqualTo(binary.Evaluate(time)), $"time={time:R}");
        double slope = binary.EvaluateSlope(time);
        if (double.IsNaN(slope)) Assert.That(double.IsNaN(curve.EvaluateSlope(time)), Is.True);
        else Assert.That(curve.EvaluateSlope(time), Is.EqualTo(slope));
    }
}

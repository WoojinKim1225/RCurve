using System;
using NUnit.Framework;
using RCurve;
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
                shape == 2 && i < count - 1 ? u * 0.01 : u;
            keys[i] = new Keyframe((float)(-2 + 3 * time), i, float.PositiveInfinity, float.PositiveInfinity);
        }
        var source = new AnimationCurve(keys) { preWrapMode = WrapMode.PingPong, postWrapMode = WrapMode.Loop };
        var curve = RationalCurveBuilder.FromSegments(source, new RationalCurveSegment[count - 1], CurveLookupMode.Magic);
        var expectedStrategy = shape == 2 ? CurveLookupStrategy.BinarySearch : CurveLookupStrategy.Magic;
        Assert.That(curve.ManagedLookup, Is.EqualTo(expectedStrategy));
        Assert.That(curve.BurstLookup, Is.EqualTo(expectedStrategy));
        Assert.That(curve.IndexPrediction.IsEnabled, Is.EqualTo(shape != 2));
        if (shape == 1) Assert.That(Math.Abs(curve.IndexPrediction.Quadratic), Is.GreaterThan(1));
        var binary = new BakedRationalCurve(curve.KeyTimes.ToArray(), curve.KeyValues.ToArray(), curve.Segments.ToArray(),
            curve.PreWrapMode, curve.PostWrapMode);
        var wrapper = new RationalCurve(source, new CurveFitSettings(lookupMode: CurveLookupMode.Magic));
        wrapper.StoreBuild(curve, wrapper.InputHash);
        var restored = JsonUtility.FromJson<RationalCurve>(JsonUtility.ToJson(wrapper));
        var restoredCurve = restored.BakedCurve;
        Assert.That(restoredCurve.IndexPrediction.IsEnabled, Is.EqualTo(curve.IndexPrediction.IsEnabled));
        Assert.That(restoredCurve.ManagedLookup, Is.EqualTo(expectedStrategy));
        Assert.That(restoredCurve.BurstLookup, Is.EqualTo(expectedStrategy));
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
            Check(curve, binary, time == 0 ? -double.Epsilon : BitConverter.Int64BitsToDouble(bits + (time > 0 ? -1 : 1)));
            Check(curve, binary, time == 0 ? double.Epsilon : BitConverter.Int64BitsToDouble(bits + (time > 0 ? 1 : -1)));
        }
        var random = new System.Random(829 + shape);
        for (int i = 0; i < 10000; i++) Check(curve, binary, -3 + 6 * random.NextDouble());
    }

    [Test]
    public void CenteringResidualsCertifiesAnOtherwiseRejectedLinearFit()
    {
        float[] times = { 0, 0.69f, 0.72f, 0.91f, 1 };
        var keys = new Keyframe[times.Length];
        for (int i = 0; i < times.Length; i++)
            keys[i] = new Keyframe(times[i], i, float.PositiveInfinity, float.PositiveInfinity);
        var curve = RationalCurveBuilder.FromSegments(new AnimationCurve(keys),
            new RationalCurveSegment[times.Length - 1], CurveLookupMode.Magic);
        var prediction = curve.IndexPrediction;
        Assert.That(curve.ManagedLookup, Is.EqualTo(CurveLookupStrategy.Magic));
        Assert.That(prediction.Cubic, Is.Zero);
        Assert.That(prediction.Quadratic, Is.Zero);
        Assert.That(prediction.IsValidFor(curve.KeyTimes), Is.True);

        double minResidual = double.PositiveInfinity, maxResidual = double.NegativeInfinity, meanResidual = 0;
        for (int i = 0; i < times.Length; i++)
        {
            double residual = prediction.Evaluate(times[i]) - i;
            minResidual = Math.Min(minResidual, residual);
            maxResidual = Math.Max(maxResidual, residual);
            meanResidual += residual / times.Length;
        }
        Assert.That(minResidual + maxResidual, Is.EqualTo(0).Within(1e-12));
        Assert.That(maxResidual, Is.LessThan(0.95));
        // Restore the zero-mean least-squares intercept: its maximum error exceeds one.
        prediction.Constant -= meanResidual;
        Assert.That(prediction.IsValidFor(curve.KeyTimes), Is.False);
    }

    [TestCase(CurveLookupMode.Auto), TestCase(CurveLookupMode.Magic),
     TestCase(CurveLookupMode.BruteForce), TestCase(CurveLookupMode.LUT)]
    public void OnlyAutoAndMagicFitAnIndexPredictor(CurveLookupMode mode)
    {
        var source = AnimationCurve.Linear(0, 0, 1, 1);
        var curve = RationalCurveBuilder.Build(source, new CurveFitSettings(lookupMode: mode));
        Assert.That(curve.IndexPrediction.IsEnabled, Is.EqualTo(mode == CurveLookupMode.Auto || mode == CurveLookupMode.Magic));
        if (mode == CurveLookupMode.BruteForce || mode == CurveLookupMode.LUT)
        {
            var expected = mode == CurveLookupMode.LUT ? CurveLookupStrategy.LUT : CurveLookupStrategy.BruteForce;
            Assert.That(curve.ManagedLookup, Is.EqualTo(expected));
            Assert.That(curve.BurstLookup, Is.EqualTo(expected));
        }
        Assert.That(curve.Evaluate(0.5), Is.EqualTo(0.5).Within(1e-12));
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

using System;
using NUnit.Framework;
using RCurve.Editor;
using Unity.Mathematics;
using UnityEngine;

public class AnimationCurveBezierSegmentTests
{
    [TestCase(WeightedMode.None)]
    [TestCase(WeightedMode.In)]
    [TestCase(WeightedMode.Out)]
    [TestCase(WeightedMode.Both)]
    public void MatchesUnityForWeightedAndUnweightedTangents(WeightedMode mode)
    {
        var start = new Keyframe(-2, -3, 0, 1.5f, 0.7f, 0.18f) { weightedMode = mode };
        var end = new Keyframe(5, 4, -0.75f, 0, 0.62f, 0.2f) { weightedMode = mode };
        var curve = new AnimationCurve(start, end);
        var segment = AnimationCurveBezierSegment.FromCurve(curve)[0];

        Assert.That(segment.IsStep, Is.False);
        Assert.That(segment.HasStationaryX, Is.False);
        Assert.That(segment.StartTime, Is.EqualTo(-2));
        Assert.That(segment.EndTime, Is.EqualTo(5));
        Assert.That(segment.P0.x, Is.EqualTo(0));
        Assert.That(segment.P3.x, Is.EqualTo(1));
        for (int i = 0; i <= 256; i++)
        {
            double2 point = segment.Evaluate(i / 256.0);
            float time = (float)(segment.StartTime + point.x * (segment.EndTime - segment.StartTime));
            Assert.That(point.y, Is.EqualTo(curve.Evaluate(time)).Within(2e-4), $"sample {i}, mode {mode}");
        }

        double2 firstDerivative = segment.EvaluateDerivative(0);
        double2 lastDerivative = segment.EvaluateDerivative(1);
        Assert.That(firstDerivative.y / (7 * firstDerivative.x), Is.EqualTo(start.outTangent).Within(1e-12));
        Assert.That(lastDerivative.y / (7 * lastDerivative.x), Is.EqualTo(end.inTangent).Within(1e-12));
    }

    [Test]
    public void EqualEndpointHeightsRetainInteriorShape()
    {
        var start = new Keyframe(3, -2, 0, 4, 0, 0.2f) { weightedMode = WeightedMode.Out };
        var end = new Keyframe(8, -2, -3, 0, 0.4f, 0) { weightedMode = WeightedMode.In };
        var curve = new AnimationCurve(start, end);
        var segment = AnimationCurveBezierSegment.FromCurve(curve)[0];
        Assert.That(segment.Evaluate(0).y, Is.EqualTo(-2));
        Assert.That(segment.Evaluate(1).y, Is.EqualTo(-2));
        Assert.That(segment.Evaluate(0.5).y, Is.GreaterThan(-2));
        for (int i = 0; i <= 128; i++)
        {
            double2 point = segment.Evaluate(i / 128.0);
            Assert.That(point.y, Is.EqualTo(curve.Evaluate((float)(3 + 5 * point.x))).Within(2e-4));
        }
    }

    [Test]
    public void DisabledWeightsUseOneThirdAndIgnoreUnusedTangents()
    {
        var start = new Keyframe(0, 0, float.NaN, 2, 0, 0.9f) { weightedMode = WeightedMode.In };
        var end = new Keyframe(2, 1, -1, float.NaN, 0.8f, 0) { weightedMode = WeightedMode.Out };
        var segment = new AnimationCurveBezierSegment(start, end);
        Assert.That(segment.P1.x, Is.EqualTo(1.0 / 3));
        Assert.That(segment.P2.x, Is.EqualTo(2.0 / 3).Within(1e-15));
        Assert.That(segment.P1.y, Is.EqualTo(4.0 / 3));
        Assert.That(segment.P2.y, Is.EqualTo(5.0 / 3).Within(1e-15));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void InfiniteTangentProducesStepNotBezier(bool outgoing)
    {
        var start = new Keyframe(-1, 2, 0, outgoing ? float.PositiveInfinity : 0);
        var end = new Keyframe(3, -4, outgoing ? 0 : float.PositiveInfinity, 0);
        var curve = new AnimationCurve(start, end);
        var segment = AnimationCurveBezierSegment.FromCurve(curve)[0];
        Assert.That(segment.IsStep, Is.True);
        for (int i = 0; i <= 32; i++)
        {
            double2 point = segment.Evaluate(i / 32.0);
            Assert.That(point.y, Is.EqualTo(curve.Evaluate((float)(-1 + 4 * point.x))));
        }
        Assert.Throws<InvalidOperationException>(() => segment.EvaluateDerivative(0.5));
    }

    [TestCase(0f, 0.5f)]
    [TestCase(0.5f, 0f)]
    [TestCase(1f, 1f)]
    public void FlagsStationaryXForSeparateFittingTreatment(float outWeight, float inWeight)
    {
        var start = new Keyframe(0, 0, 0, 0, 0, outWeight) { weightedMode = WeightedMode.Out };
        var end = new Keyframe(1, 1, 0, 0, inWeight, 0) { weightedMode = WeightedMode.In };
        var segment = new AnimationCurveBezierSegment(start, end);
        Assert.That(segment.HasStationaryX, Is.True);
        double t = outWeight == 0 ? 0 : inWeight == 0 ? 1 : 0.5;
        Assert.That(segment.EvaluateDerivative(t).x, Is.EqualTo(0).Within(1e-15));
        if (t == 0.5)
            Assert.That(segment.EvaluateDerivative(t).y, Is.GreaterThan(0), "Interior vertical tangent");
    }

    [Test]
    public void CrossingControlHandlesDoNotMeanNonmonotonicX()
    {
        var start = new Keyframe(0, 0, 0, 1, 0, 0.9f) { weightedMode = WeightedMode.Out };
        var end = new Keyframe(1, 2, 1, 0, 0.8f, 0) { weightedMode = WeightedMode.In };
        var segment = new AnimationCurveBezierSegment(start, end);
        Assert.That(segment.P1.x, Is.GreaterThan(segment.P2.x));
        Assert.That(segment.HasStationaryX, Is.False);
        for (int i = 0; i <= 128; i++)
            Assert.That(segment.EvaluateDerivative(i / 128.0).x, Is.GreaterThan(0));
    }

    [Test]
    public void ParametricDerivativeMatchesCentralDifference()
    {
        var start = new Keyframe(-3, 2, 0, -1, 0, 0.2f) { weightedMode = WeightedMode.Out };
        var end = new Keyframe(7, -5, 3, 0, 0.6f, 0) { weightedMode = WeightedMode.In };
        var segment = new AnimationCurveBezierSegment(start, end);
        const double step = 1e-5;
        for (int i = 1; i < 100; i++)
        {
            double t = i / 100.0;
            double2 numerical = (segment.Evaluate(t + step) - segment.Evaluate(t - step)) / (2 * step);
            Assert.That(math.distance(segment.EvaluateDerivative(t), numerical), Is.LessThan(1e-7));
        }
    }

    [Test]
    public void ConversionHandlesEmptySingleKeyAndMultipleIntervalsWithoutMutatingCurve()
    {
        Assert.That(AnimationCurveBezierSegment.FromCurve(new AnimationCurve()), Is.Empty);
        Assert.That(AnimationCurveBezierSegment.FromCurve(new AnimationCurve(new Keyframe(2, 3))), Is.Empty);
        var curve = new AnimationCurve(new Keyframe(-2, 3), new Keyframe(1, -4), new Keyframe(7, 2));
        curve.preWrapMode = WrapMode.Loop;
        curve.postWrapMode = WrapMode.PingPong;
        Keyframe[] originalKeys = curve.keys;
        var segments = AnimationCurveBezierSegment.FromCurve(curve);
        Assert.That(segments.Length, Is.EqualTo(2));
        Assert.That(segments[0].EndTime, Is.EqualTo(segments[1].StartTime));
        Assert.That(segments[0].P3.y, Is.EqualTo(segments[1].P0.y));
        Assert.That(curve.keys, Is.EqualTo(originalKeys));
        Assert.That(curve.preWrapMode, Is.EqualTo(WrapMode.Loop));
        Assert.That(curve.postWrapMode, Is.EqualTo(WrapMode.PingPong));
        curve.MoveKey(0, new Keyframe(-2, 100));
        Assert.That(segments[0].P0.y, Is.EqualTo(3));
    }

    [TestCase(-0.1f)]
    [TestCase(1.1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void RejectsInvalidActiveWeightsRatherThanClamping(float weight)
    {
        var start = new Keyframe(0, 0, 0, 1, 0, weight) { weightedMode = WeightedMode.Out };
        var end = new Keyframe(1, 1, 1, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationCurveBezierSegment(start, end));
        start.weightedMode = WeightedMode.None;
        end.inWeight = weight;
        end.weightedMode = WeightedMode.In;
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationCurveBezierSegment(start, end));
    }

    [Test]
    public void RejectsInvalidKeysNullCurveAndExtrapolation()
    {
        var start = new Keyframe(0, 0);
        var end = new Keyframe(1, 1);
        Assert.Throws<ArgumentNullException>(() => AnimationCurveBezierSegment.FromCurve(null));
        Assert.Throws<ArgumentException>(() => new AnimationCurveBezierSegment(start, start));
        Assert.Throws<ArgumentException>(() => new AnimationCurveBezierSegment(end, start));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationCurveBezierSegment(new Keyframe(float.NaN, 0), end));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationCurveBezierSegment(start, new Keyframe(1, float.PositiveInfinity)));
        Assert.Throws<ArgumentException>(() => new AnimationCurveBezierSegment(new Keyframe(0, 0, 0, float.NaN), end));
        var segment = new AnimationCurveBezierSegment(start, end);
        Assert.Throws<ArgumentOutOfRangeException>(() => segment.Evaluate(-0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => segment.Evaluate(1.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => segment.EvaluateDerivative(double.NaN));
    }
}

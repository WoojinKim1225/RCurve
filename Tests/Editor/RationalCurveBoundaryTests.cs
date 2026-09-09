using System;
using NUnit.Framework;
using RCurve;
using UnityEngine;

public class RationalCurveBoundaryTests
{
    [Test]
    public void IndependentWrapModesMatchUnityAtAndAroundBoundaries(
        [Values(WrapMode.Default, WrapMode.Once, WrapMode.ClampForever, WrapMode.Loop, WrapMode.PingPong)] WrapMode pre,
        [Values(WrapMode.Default, WrapMode.Once, WrapMode.ClampForever, WrapMode.Loop, WrapMode.PingPong)] WrapMode post,
        [Values(false, true)] bool steps)
    {
        var source = new AnimationCurve(
            new Keyframe(-2, 2, 0, steps ? float.PositiveInfinity : 1),
            new Keyframe(1, 5, steps ? float.PositiveInfinity : -0.5f, steps ? float.PositiveInfinity : -1),
            new Keyframe(5, -3, steps ? float.PositiveInfinity : 0.75f, 0));
        source.preWrapMode = pre;
        source.postWrapMode = post;
        BakedRationalCurve curve = FromUnweightedCurve(source);
        const float epsilon = 1f / 1024;
        for (int cycle = -4; cycle <= 4; cycle++)
        {
            // Includes negative periods, original keys, exact loop seams, and reflected keys.
            foreach (float offset in new[] { 0f, 3f, 4f, 7f })
            {
                float boundary = -2 + 7 * cycle + offset;
                foreach (float time in new[] { boundary - epsilon, boundary, boundary + epsilon })
                    Assert.That(curve.Evaluate(time), Is.EqualTo(source.Evaluate(time)).Within(2e-5),
                        $"pre={pre}, post={post}, steps={steps}, time={time:R}");
            }
        }
    }

    [TestCase(WrapMode.Default, 2)]
    [TestCase(WrapMode.Loop, 2)]
    [TestCase(WrapMode.Once, -3)]
    [TestCase(WrapMode.ClampForever, -3)]
    [TestCase(WrapMode.PingPong, -3)]
    public void ExactFinalTimeDependsOnPostWrapMode(WrapMode post, double expected)
    {
        var source = AnimationCurve.Linear(-2, 2, 5, -3);
        source.postWrapMode = post;
        Assert.That(source.Evaluate(5), Is.EqualTo(expected));
        Assert.That(FromUnweightedCurve(source).Evaluate(5), Is.EqualTo(expected));
    }

    [Test]
    public void StepsOwnLeftClosedRightOpenIntervalsRegardlessOfQueryOrder()
    {
        var source = new AnimationCurve(new Keyframe(0, 2, 0, float.PositiveInfinity),
            new Keyframe(1, 5, float.PositiveInfinity, float.PositiveInfinity),
            new Keyframe(3, -1, float.PositiveInfinity, 0));
        var curve = FromUnweightedCurve(source);
        float[] times = { 1, 0.999f, 1.001f, 3, 2.999f, 0, 2, 1, 3.001f, -1 };
        double[] values = { 5, 2, 5, -1, 5, 2, 5, 5, -1, 2 };
        for (int i = 0; i < times.Length; i++)
        {
            Assert.That(source.Evaluate(times[i]), Is.EqualTo(values[i]));
            Assert.That(curve.Evaluate(times[i]), Is.EqualTo(values[i]));
        }
        Array.Reverse(times);
        foreach (float time in times)
            Assert.That(curve.Evaluate(time), Is.EqualTo(source.Evaluate(time)));
    }

    [Test]
    public void EmptyAndSingleKeyCurvesMatchUnity([Values] CurveWrapMode mode)
    {
        var empty = new BakedRationalCurve(Array.Empty<double>(), Array.Empty<double>(), Array.Empty<RationalCurveSegment>(), mode, mode);
        var single = new BakedRationalCurve(new[] { 2.0 }, new[] { -3.0 }, Array.Empty<RationalCurveSegment>(), mode, mode);
        var sourceEmpty = new AnimationCurve();
        var sourceSingle = new AnimationCurve(new Keyframe(2, -3));
        foreach (double time in new[] { -100.0, 0, 2, 100 })
        {
            Assert.That(empty.Evaluate(time), Is.EqualTo(sourceEmpty.Evaluate((float)time)));
            Assert.That(single.Evaluate(time), Is.EqualTo(sourceSingle.Evaluate((float)time)));
            Assert.That(empty.EvaluateDerivative(time), Is.EqualTo(0));
            Assert.That(single.EvaluateDerivative(time), Is.EqualTo(0));
        }
        Assert.That(RationalCurveBuilder.FromSegments(sourceEmpty, Array.Empty<RationalCurveSegment>()).Evaluate(0), Is.EqualTo(0));
        Assert.That(RationalCurveBuilder.FromSegments(sourceSingle, Array.Empty<RationalCurveSegment>()).Evaluate(-100), Is.EqualTo(-3));
    }

    [Test]
    public void CapturesWrapModesAndKeysWithoutRetainingMutableInputs()
    {
        var source = AnimationCurve.Linear(0, 2, 1, 5);
        source.preWrapMode = WrapMode.Loop;
        source.postWrapMode = WrapMode.PingPong;
        BakedRationalCurve curve = FromUnweightedCurve(source);
        double before = curve.Evaluate(-0.25);
        source.preWrapMode = WrapMode.ClampForever;
        source.postWrapMode = WrapMode.Once;
        source.MoveKey(0, new Keyframe(0, 100));
        Assert.That(curve.PreWrapMode, Is.EqualTo(CurveWrapMode.Loop));
        Assert.That(curve.PostWrapMode, Is.EqualTo(CurveWrapMode.PingPong));
        Assert.That(curve.Evaluate(-0.25), Is.EqualTo(before));
        Assert.That(curve.Evaluate(0), Is.EqualTo(2));

        double[] times = { 0, 1 };
        double[] values = { 2, 5 };
        var segments = new RationalCurveSegment[1];
        var step = new BakedRationalCurve(times, values, segments);
        times[0] = -10;
        values[0] = 100;
        segments[0] = new RationalCurveSegment(0, 1, 2, 5, 3, 3, new[] { 1.0, 1, 1, 1 }, Array.Empty<double>());
        Assert.That(step.Evaluate(0.5), Is.EqualTo(2));
    }

    [Test]
    public void DerivativesClampReverseAndDetectNonDifferentiableBoundaries()
    {
        var source = AnimationCurve.Linear(-2, 3, 2, 11);
        var clamped = FromUnweightedCurve(source);
        Assert.That(clamped.EvaluateDerivative(-3), Is.EqualTo(0));
        Assert.That(clamped.EvaluateDerivative(3), Is.EqualTo(0));
        Assert.That(clamped.EvaluateDerivative(0), Is.EqualTo(2).Within(1e-12));
        Assert.That(double.IsNaN(clamped.EvaluateDerivative(-2)), Is.True);
        Assert.That(double.IsNaN(clamped.EvaluateDerivative(2)), Is.True);

        source.preWrapMode = source.postWrapMode = WrapMode.PingPong;
        var mirrored = FromUnweightedCurve(source);
        Assert.That(mirrored.EvaluateDerivative(-3), Is.EqualTo(-2).Within(1e-12));
        Assert.That(mirrored.EvaluateDerivative(3), Is.EqualTo(-2).Within(1e-12));
        Assert.That(mirrored.EvaluateDerivative(7), Is.EqualTo(2).Within(1e-12));
        foreach (double time in new[] { -6.0, -2, 2, 6 })
            Assert.That(double.IsNaN(mirrored.EvaluateDerivative(time)), Is.True);

        source.preWrapMode = source.postWrapMode = WrapMode.Loop;
        var looped = FromUnweightedCurve(source);
        Assert.That(looped.EvaluateDerivative(3), Is.EqualTo(2).Within(1e-12));
        Assert.That(double.IsNaN(looped.EvaluateDerivative(6)), Is.True);
    }

    [Test]
    public void SmoothJoinsHaveDerivativesButStepsAndCornersDoNot()
    {
        var source = new AnimationCurve(new Keyframe(0, 0, 1, 1), new Keyframe(1, 0, 1, 1));
        source.preWrapMode = source.postWrapMode = WrapMode.Loop;
        var loop = FromUnweightedCurve(source);
        foreach (double time in new[] { -1.0, 0, 1, 2 })
            Assert.That(loop.EvaluateDerivative(time), Is.EqualTo(1).Within(1e-12));

        source = AnimationCurve.EaseInOut(0, 0, 1, 1);
        source.preWrapMode = source.postWrapMode = WrapMode.PingPong;
        var mirror = FromUnweightedCurve(source);
        foreach (double time in new[] { -1.0, 0, 1, 2 })
            Assert.That(mirror.EvaluateDerivative(time), Is.EqualTo(0).Within(1e-12));

        var corner = FromUnweightedCurve(new AnimationCurve(new Keyframe(0, 0, 0, 1),
            new Keyframe(1, 1, 1, -1), new Keyframe(2, 0, -1, 0)));
        Assert.That(double.IsNaN(corner.EvaluateDerivative(1)), Is.True);
        var stepSource = new AnimationCurve(new Keyframe(0, 2, 0, float.PositiveInfinity), new Keyframe(1, 5, float.PositiveInfinity, 0));
        stepSource.postWrapMode = WrapMode.PingPong;
        var step = FromUnweightedCurve(stepSource);
        Assert.That(step.EvaluateDerivative(0.5), Is.EqualTo(0));
        Assert.That(double.IsNaN(step.EvaluateDerivative(1)), Is.True);
        Assert.That(double.IsNaN(step.EvaluateDerivative(3)), Is.True);
        Assert.That(step.EvaluateDerivative(2), Is.EqualTo(0));
    }

    [Test]
    public void RejectsMissingFitsInvalidKeysAndNonFiniteQueries()
    {
        var source = AnimationCurve.Linear(0, 0, 1, 1);
        Assert.Throws<ArgumentNullException>(() => RationalCurveBuilder.FromSegments(null, Array.Empty<RationalCurveSegment>()));
        Assert.Throws<ArgumentNullException>(() => RationalCurveBuilder.FromSegments(source, null));
        Assert.Throws<ArgumentException>(() => RationalCurveBuilder.FromSegments(source, Array.Empty<RationalCurveSegment>()));
        Assert.Throws<ArgumentException>(() => RationalCurveBuilder.FromSegments(source, new RationalCurveSegment[1]));
        Assert.Throws<ArgumentException>(() => new BakedRationalCurve(new[] { 1.0, 0 }, new[] { 0.0, 1 }, new RationalCurveSegment[1]));
        Assert.Throws<ArgumentException>(() => new BakedRationalCurve(new[] { 0.0 }, Array.Empty<double>(), Array.Empty<RationalCurveSegment>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BakedRationalCurve(new[] { double.NaN }, new[] { 0.0 }, Array.Empty<RationalCurveSegment>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BakedRationalCurve(Array.Empty<double>(), Array.Empty<double>(), Array.Empty<RationalCurveSegment>(), (CurveWrapMode)99));
        var curve = FromUnweightedCurve(source);
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.Evaluate(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.EvaluateDerivative(double.PositiveInfinity));
    }

    private static BakedRationalCurve FromUnweightedCurve(AnimationCurve source)
    {
        Keyframe[] keys = source.keys;
        var segments = new RationalCurveSegment[Math.Max(0, keys.Length - 1)];
        for (int i = 0; i < segments.Length; i++)
        {
            if (float.IsInfinity(keys[i].outTangent) || float.IsInfinity(keys[i + 1].inTangent)) continue;
            // Exact cubic Hermite representation; tests do not depend on the unfinished fitting solver.
            segments[i] = new RationalCurveSegment(keys[i].time, keys[i + 1].time, keys[i].value, keys[i + 1].value,
                keys[i].outTangent, keys[i + 1].inTangent, new[] { 1.0, 1, 1, 1 }, Array.Empty<double>());
        }
        return RationalCurveBuilder.FromSegments(source, segments);
    }
}

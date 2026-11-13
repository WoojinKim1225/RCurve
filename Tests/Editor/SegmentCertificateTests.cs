using System;
using NUnit.Framework;
using RCurve;
using UnityEngine;

public sealed class SegmentCertificateTests
{
    [Test]
    public void NonNegativeRefinesNegativeCoefficientsAndFindsRealDips()
    {
        // (s - 1/2)^2 + c in the degree-2 Bernstein basis: c + 1/4, c - 1/4, c + 1/4
        Assert.That(SegmentCertificate.NonNegative(new[] { 0.251, -0.249, 0.251 }), Is.True, "Positive with a negative coefficient.");
        Assert.That(SegmentCertificate.NonNegative(new[] { 0.249, -0.251, 0.249 }), Is.False, "Dips below zero at s = 1/2.");
        Assert.That(SegmentCertificate.NonNegative(new[] { 1.0, 2.0, 0.5 }), Is.True);
        Assert.That(SegmentCertificate.NonNegative(new[] { -1e-9, 2.0, 0.5 }), Is.False);
    }

    [Test]
    public void ComposeAndRestrictMatchDirectEvaluation()
    {
        double[] f = { 0.3, -1.2, 2.5, 0.7, 1.1 };
        double[] g = { 0.1, 0.9, 0.2, 0.8 };
        double[] composed = SegmentCertificate.Compose(f, g);
        double[] restricted = SegmentCertificate.Restrict(f, 0.25, 0.6);
        for (int i = 0; i <= 20; i++)
        {
            double s = i / 20.0;
            Assert.That(Evaluate(composed, s), Is.EqualTo(Evaluate(f, Evaluate(g, s))).Within(1e-12));
            Assert.That(Evaluate(restricted, s), Is.EqualTo(Evaluate(f, 0.25 + 0.35 * s)).Within(1e-12));
        }
    }

    [Test]
    public void HoldsJustAboveTheTrueErrorAndNotBelowIt()
    {
        Keyframe[] keys = Weighted().keys;
        var source = new AnimationCurveBezierSegment(keys[0], keys[1]);
        BakedRationalCurve baked = RationalCurveBuilder.Build(Weighted(), new CurveFitSettings(
            CurveFitObjective.Minimax, CurveFitQuality.Low, maxValueError: 0.05, maxSlopeError: 5));
        Assert.That(baked.SegmentCount, Is.EqualTo(1), "One segment over the whole key interval.");
        RationalCurveSegment segment = baked.Segments[0];

        // densely in the Bezier parameter, where the source is exact
        double valueError = 0, slopeError = 0;
        double length = source.EndTime - source.StartTime;
        for (int i = 0; i <= 20000; i++)
        {
            double s = i / 20000.0;
            var point = source.Evaluate(s);
            var derivative = source.EvaluateDerivative(s);
            double time = Math.Min(source.EndTime, source.StartTime + length * point.x);
            valueError = Math.Max(valueError, Math.Abs(point.y - segment.Evaluate(time)));
            slopeError = Math.Max(slopeError, Math.Abs(derivative.y / (derivative.x * length) - segment.EvaluateSlope(time)));
        }
        Assert.That(valueError, Is.GreaterThan(1e-6), "The low-degree fit must have a measurable error.");

        Assert.That(SegmentCertificate.Holds(source, 0, 1, 0, 1, segment, valueError * 1.05, slopeError * 1.05), Is.True);
        Assert.That(SegmentCertificate.Holds(source, 0, 1, 0, 1, segment, valueError * 0.95, slopeError * 1.05), Is.False);
        Assert.That(SegmentCertificate.Holds(source, 0, 1, 0, 1, segment, valueError * 1.05, slopeError * 0.95), Is.False);
    }

    private static AnimationCurve Weighted()
    {
        return new AnimationCurve(
            new Keyframe(0, 0, 0, 3, 0, 0.15f) { weightedMode = WeightedMode.Out },
            new Keyframe(1, 1, -0.7f, 0, 0.65f, 0) { weightedMode = WeightedMode.In });
    }

    private static double Evaluate(double[] f, double s)
    {
        var work = (double[])f.Clone();
        for (int level = 1; level < work.Length; level++)
            for (int i = 0; i < work.Length - level; i++) work[i] += (work[i + 1] - work[i]) * s;
        return work[0];
    }
}

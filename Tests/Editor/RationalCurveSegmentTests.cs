using System;
using NUnit.Framework;
using RCurve;
using UnityEngine;

public class RationalCurveSegmentTests
{
    [Test]
    public void PreservesEndpointValuesAndSlopes(
        [Values(3, 4, 5, 6)] int degree, [Values(false, true)] bool equalEndpoints)
    {
        double startValue = equalEndpoints ? 2 : -3;
        double endValue = equalEndpoints ? 2 : 7;
        var segment = new RationalCurveSegment(
            -2, 5, startValue, endValue, -1.5, 2.25,
            Denominator(degree), new double[degree - 3]);

        Assert.That(segment.Degree, Is.EqualTo(degree));
        Assert.That(segment.Evaluate(-2), Is.EqualTo(startValue).Within(1e-12));
        Assert.That(segment.Evaluate(5), Is.EqualTo(endValue).Within(1e-12));
        Assert.That(segment.EvaluateSlope(-2), Is.EqualTo(-1.5).Within(1e-12));
        Assert.That(segment.EvaluateSlope(5), Is.EqualTo(2.25).Within(1e-12));
    }

    [Test]
    public void UnitDenominatorReproducesUnweightedAnimationCurve([Values(3, 4, 5, 6)] int degree)
    {
        const double length = 7;
        double[] p = { -3, -3 + length * 1.5 / 3, 4 - length * -0.75 / 3, 4 };
        // Degree elevation provides independently known Bernstein coefficients for the same cubic.
        for (int n = 3; n < degree; n++)
        {
            var elevated = new double[n + 2];
            elevated[0] = p[0];
            elevated[n + 1] = p[n];
            for (int i = 1; i <= n; i++)
            {
                double weight = (double)i / (n + 1);
                elevated[i] = weight * p[i - 1] + (1 - weight) * p[i];
            }
            p = elevated;
        }
        var interior = new double[degree - 3];
        Array.Copy(p, 2, interior, 0, interior.Length);
        var q = new double[degree + 1];
        Array.Fill(q, 1.0);
        var segment = new RationalCurveSegment(-2, 5, -3, 4, 1.5, -0.75, q, interior);
        var curve = new AnimationCurve(
            new Keyframe(-2, -3, 0, 1.5f), new Keyframe(5, 4, -0.75f, 0));

        for (int i = 0; i <= 256; i++)
        {
            double x = -2 + length * i / 256;
            Assert.That(segment.Evaluate(x), Is.EqualTo(curve.Evaluate((float)x)).Within(1e-5));
        }
    }

    [Test]
    public void DerivativeMatchesCentralDifference([Values(3, 4, 5, 6)] int degree)
    {
        var segment = new RationalCurveSegment(-2, 5, -3, 7, -1.5, 2.25,
            Denominator(degree), new double[degree - 3]);
        const double step = 1e-5;
        for (int i = 1; i < 100; i++)
        {
            double x = -2 + 7.0 * i / 100;
            double derivative = (segment.Evaluate(x + step) - segment.Evaluate(x - step)) / (2 * step);
            Assert.That(segment.EvaluateSlope(x), Is.EqualTo(derivative).Within(1e-7));
        }
    }

    [Test]
    public void PositiveDenominatorAtMinimumRemainsFinite([Values(3, 4, 5, 6)] int degree)
    {
        const double minimum = 1e-6;
        var q = new double[degree + 1];
        Array.Fill(q, minimum);
        q[0] = 1;
        var interior = new double[degree - 3];
        Array.Fill(interior, 2 * minimum);
        var segment = new RationalCurveSegment(0, 1, 2, 2, 0, 0, q, interior);
        // P = 2Q: the result must stay constant even when the denominator is small.
        for (int i = 0; i <= 1000; i++)
        {
            double x = i / 1000.0;
            Assert.That(segment.Evaluate(x), Is.EqualTo(2).Within(1e-12));
            Assert.That(segment.EvaluateSlope(x), Is.EqualTo(0).Within(1e-8));
        }
    }

    [TestCase(0.0)]
    [TestCase(-1.0)]
    [TestCase(1e-7)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void RejectsInvalidDenominatorCoefficients(double value)
    {
        for (int i = 1; i <= 6; i++)
        {
            double[] q = Denominator(6);
            q[i] = value;
            Assert.Throws<ArgumentOutOfRangeException>(() => new RationalCurveSegment(
                0, 1, 0, 1, 0, 0, q, new double[3]));
        }
    }

    [Test]
    public void RejectsInvalidCoefficientShapesAndKeepsPrivateCopies()
    {
        Assert.Throws<ArgumentNullException>(() => new RationalCurveSegment(0, 1, 0, 1, 0, 0, null, new double[0]));
        Assert.Throws<ArgumentNullException>(() => new RationalCurveSegment(0, 1, 0, 1, 0, 0, Denominator(3), null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RationalCurveSegment(0, 1, 0, 1, 0, 0, Denominator(2), new double[0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RationalCurveSegment(0, 1, 0, 1, 0, 0, Denominator(7), new double[4]));
        Assert.Throws<ArgumentException>(() => new RationalCurveSegment(0, 1, 0, 1, 0, 0, Denominator(4), new double[0]));
        var q = Denominator(4);
        q[0] = 2;
        Assert.Throws<ArgumentException>(() => new RationalCurveSegment(0, 1, 0, 1, 0, 0, q, new double[1]));
        q[0] = 1;
        double[] interior = { 0.5 };
        var segment = new RationalCurveSegment(0, 1, 0, 1, 0, 0, q, interior);
        double before = segment.Evaluate(0.5);
        q[1] = -100;
        interior[0] = 100;
        Assert.That(segment.Evaluate(0.5), Is.EqualTo(before));
    }

    [Test]
    public void RejectsDegenerateIntervalsNonFiniteInputsAndExtrapolation()
    {
        var q = Denominator(4);
        double[] interior = { 0.5 };
        Assert.Throws<ArgumentOutOfRangeException>(() => new RationalCurveSegment(1, 1, 0, 1, 0, 0, q, interior));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RationalCurveSegment(1, 0, 0, 1, 0, 0, q, interior));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RationalCurveSegment(0, 1, 0, 1, double.PositiveInfinity, 0, q, interior));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RationalCurveSegment(0, 1, 0, 1, 0, 0, q, new[] { double.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RationalCurveSegment(0, 1, 0, 1, 0, 0, q, interior, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RationalCurveSegment(0, 1, 0, 1, 0, 0, q, interior, 2));
        var segment = new RationalCurveSegment(0, 1, 0, 1, 0, 0, q, interior);
        Assert.Throws<ArgumentOutOfRangeException>(() => segment.Evaluate(-0.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => segment.EvaluateSlope(1.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => segment.Evaluate(double.NaN));
    }

    private static double[] Denominator(int degree)
    {
        var q = new double[degree + 1];
        q[0] = 1;
        for (int i = 1; i <= degree; i++)
            q[i] = 0.4 + 0.3 * i;
        return q;
    }
}

public class CurveFitSettingsTests
{
    [Test]
    public void ObjectiveAndQualityAreIndependent([Values] CurveFitObjective objective, [Values] CurveFitQuality quality)
    {
        var settings = new CurveFitSettings(objective, quality);
        Assert.That(settings.Objective, Is.EqualTo(objective));
        Assert.That(settings.Quality, Is.EqualTo(quality));
        Assert.That(settings.Degree, Is.EqualTo((int)quality));
        var q = new double[settings.Degree + 1];
        Array.Fill(q, 1.0);
        var segment = new RationalCurveSegment(0, 1, 0, 1, 0, 0, q, new double[settings.Degree - 3]);
        Assert.That(segment.Degree, Is.EqualTo(settings.Degree));
    }

    [Test]
    public void DefaultsAndInvalidSettingsAreExplicit()
    {
        var settings = new CurveFitSettings();
        Assert.That(settings.Objective, Is.EqualTo(CurveFitObjective.Minimax));
        Assert.That(settings.Quality, Is.EqualTo(CurveFitQuality.High));
        Assert.That(settings.Degree, Is.EqualTo(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CurveFitSettings((CurveFitObjective)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CurveFitSettings(quality: (CurveFitQuality)2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CurveFitSettings(quality: (CurveFitQuality)7));
    }
}

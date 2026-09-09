using System;
using NUnit.Framework;
using RCurve;

public class RationalCurveHornerTests
{
    [Test]
    public void HornerMatchesIndependentBernsteinEvaluation([Values(3, 4, 5, 6)] int degree)
    {
        var random = new Random(9107 + degree);
        for (int curve = 0; curve < 24; curve++)
        {
            var q = new double[degree + 1];
            q[0] = 1;
            for (int i = 1; i <= degree; i++) q[i] = Math.Pow(10, -6 + 8 * random.NextDouble());
            if (curve % 2 == 0) q[degree] = 1e-6;
            var interior = new double[degree - 3];
            for (int i = 0; i < interior.Length; i++) interior[i] = 4 * random.NextDouble() - 2;
            var segment = new RationalCurveSegment(-2, 5, -1, 2, 1.5, -0.75, q, interior);
            double[] p = segment.NumeratorCoefficients.ToArray();
            var dp = new double[degree];
            var dq = new double[degree];
            for (int i = 0; i < degree; i++)
            {
                dp[i] = degree * (p[i + 1] - p[i]);
                dq[i] = degree * (q[i + 1] - q[i]);
            }
            for (int i = 0; i < 256; i++)
            {
                double u = i == 0 ? 0.5 - 1e-12 : i == 1 ? 0.5 + 1e-12 : random.NextDouble();
                double x = -2 + 7 * u;
                double denominator = Bernstein(q, u);
                double value = Bernstein(p, u) / denominator;
                double slope = (Bernstein(dp, u) - value * Bernstein(dq, u)) / denominator / 7;
                Assert.That(segment.Evaluate(x), Is.EqualTo(value).Within(1e-9 * Math.Max(1, Math.Abs(value))));
                Assert.That(segment.EvaluateSlope(x), Is.EqualTo(slope).Within(1e-7 * Math.Max(1, Math.Abs(slope))));
            }
            Assert.That(segment.Evaluate(-2), Is.EqualTo(-1));
            Assert.That(segment.Evaluate(5), Is.EqualTo(2));
            Assert.That(segment.EvaluateSlope(-2), Is.EqualTo(1.5));
            Assert.That(segment.EvaluateSlope(5), Is.EqualTo(-0.75));
        }
    }

    private static double Bernstein(double[] coefficients, double u)
    {
        var work = (double[])coefficients.Clone();
        for (int count = work.Length - 1; count > 0; count--)
            for (int i = 0; i < count; i++) work[i] = (1 - u) * work[i] + u * work[i + 1];
        return work[0];
    }
}

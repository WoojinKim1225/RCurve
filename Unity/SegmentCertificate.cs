using System;

namespace RCurve
{
    // Proves a fitted segment's value and slope errors against its Bezier source over the whole interval, not at
    // samples. In the Bezier parameter s the source is polynomial (x(s), y(s)) and the segment's normalized time
    // u(s) is the affine image of x(s), so after multiplying out the positive factors each bound becomes
    // "a polynomial in s is nonnegative on [0,1]":
    //   value  |y - P(u)/Q(u)| <= d          <=>  d Q(u) -+ (y Q(u) - P(u)) >= 0
    //   slope  |y'/(L h u') - W(u)/(L h Q(u)^2)| <= e
    //                                        <=>  e L h u' Q(u)^2 -+ (y' Q(u)^2 - W(u) u') >= 0
    // with W = P'Q - PQ', L the key interval's length, h the segment's share of it, and ' the derivative in s (or
    // in u for P, Q). A polynomial whose Bernstein coefficients are all nonnegative is nonnegative (convex hull);
    // where they are not, de Casteljau halves the interval until they are, an end value is negative (a real
    // violation), or the refinement budget runs out (treated as not proven).
    internal static class SegmentCertificate
    {
        private const int MaxDepth = 24;
        private const int MaxNodes = 4096;

        // sLeft/sRight: the Bezier parameters where the segment starts and ends; left/right: the same in the key
        // interval's normalized time.
        internal static bool Holds(AnimationCurveBezierSegment source, double sLeft, double sRight, double left, double right,
            RationalCurveSegment segment, double maxValueError, double maxSlopeError)
        {
            double[] x = Restrict(new[] { source.P0.x, source.P1.x, source.P2.x, source.P3.x }, sLeft, sRight);
            double[] y = Restrict(new[] { source.P0.y, source.P1.y, source.P2.y, source.P3.y }, sLeft, sRight);
            double share = right - left;
            var u = new double[x.Length];
            for (int i = 0; i < u.Length; i++) u[i] = (x[i] - left) / share;

            double[] p = segment.NumeratorCoefficients.ToArray();
            double[] q = segment.DenominatorCoefficients.ToArray();
            double[] pu = Compose(p, u);
            double[] qu = Compose(q, u);

            // value: d Q(u) -+ (y Q(u) - P(u))
            double[] error = Subtract(Multiply(y, qu), Elevate(pu, y.Length - 1));
            double[] bound = Scale(Elevate(qu, y.Length - 1), maxValueError);
            if (!NonNegative(Subtract(bound, error)) || !NonNegative(Add(bound, error))) return false;

            // slope: e L h u' Q(u)^2 -+ (y' Q(u)^2 - W(u) u')
            double[] dy = Derivative(y);
            double[] du = Derivative(u);
            double[] qu2 = Multiply(qu, qu);
            double[] w = Compose(Subtract(Multiply(Derivative(p), q), Multiply(p, Derivative(q))), u);
            double[] lhs = Multiply(dy, qu2);
            double[] wdu = Multiply(w, du);
            double[] slopeError = Subtract(lhs, Elevate(wdu, lhs.Length - wdu.Length));
            double[] slopeBound = Scale(Multiply(du, qu2), maxSlopeError * (source.EndTime - source.StartTime) * share);
            return NonNegative(Subtract(slopeBound, slopeError)) && NonNegative(Add(slopeBound, slopeError));
        }

        internal static bool NonNegative(double[] f)
        {
            int nodes = MaxNodes;
            return NonNegative(f, 0, ref nodes);
        }

        private static bool NonNegative(double[] f, int depth, ref int nodes)
        {
            bool all = true;
            foreach (double c in f)
                if (!(c >= 0)) { all = false; break; }
            if (all) return true;
            if (!(f[0] >= 0) || !(f[f.Length - 1] >= 0)) return false;
            if (depth >= MaxDepth || --nodes < 0) return false;
            Split(f, 0.5, out double[] low, out double[] high);
            return NonNegative(low, depth + 1, ref nodes) && NonNegative(high, depth + 1, ref nodes);
        }

        // Bernstein coefficients on [0,1] of f restricted to [a,b] (reparameterized to [0,1]).
        internal static double[] Restrict(double[] f, double a, double b)
        {
            if (a == 0 && b == 1) return (double[])f.Clone();
            Split(f, b, out double[] toB, out _);
            if (a == 0) return toB;
            Split(toB, a / b, out _, out double[] fromA);
            return fromA;
        }

        // de Casteljau at t: the coefficients on [0,t] and on [t,1].
        internal static void Split(double[] f, double t, out double[] low, out double[] high)
        {
            int n = f.Length - 1;
            var work = (double[])f.Clone();
            low = new double[n + 1];
            high = new double[n + 1];
            low[0] = work[0];
            high[n] = work[n];
            for (int level = 1; level <= n; level++)
            {
                for (int i = 0; i <= n - level; i++) work[i] = work[i] + (work[i + 1] - work[i]) * t;
                low[level] = work[0];
                high[n - level] = work[n - level];
            }
        }

        // product of two Bernstein polynomials, in the Bernstein basis of the summed degree
        internal static double[] Multiply(double[] a, double[] b)
        {
            int m = a.Length - 1, r = b.Length - 1;
            var c = new double[m + r + 1];
            for (int i = 0; i <= m; i++)
            for (int j = 0; j <= r; j++)
                c[i + j] += Binomial(m, i) * Binomial(r, j) / Binomial(m + r, i + j) * a[i] * b[j];
            return c;
        }

        // f(g(s)) for f in the Bernstein basis of [0,1] and g's Bernstein coefficients: sum f_k C(n,k) g^k (1-g)^(n-k)
        internal static double[] Compose(double[] f, double[] g)
        {
            int n = f.Length - 1;
            var h = new double[g.Length];
            for (int i = 0; i < g.Length; i++) h[i] = 1 - g[i];
            var gPower = new double[n + 1][];
            var hPower = new double[n + 1][];
            gPower[0] = hPower[0] = new[] { 1.0 };
            for (int k = 1; k <= n; k++)
            {
                gPower[k] = Multiply(gPower[k - 1], g);
                hPower[k] = Multiply(hPower[k - 1], h);
            }
            var result = new double[n * (g.Length - 1) + 1];
            for (int k = 0; k <= n; k++)
            {
                double[] term = Multiply(gPower[k], hPower[n - k]);
                double weight = f[k] * Binomial(n, k);
                for (int i = 0; i < result.Length; i++) result[i] += weight * term[i];
            }
            return result;
        }

        internal static double[] Elevate(double[] f, int by)
        {
            for (int step = 0; step < by; step++)
            {
                int n = f.Length - 1;
                var raised = new double[n + 2];
                raised[0] = f[0];
                raised[n + 1] = f[n];
                for (int i = 1; i <= n; i++)
                {
                    double t = (double)i / (n + 1);
                    raised[i] = t * f[i - 1] + (1 - t) * f[i];
                }
                f = raised;
            }
            return f;
        }

        internal static double[] Derivative(double[] f)
        {
            int n = f.Length - 1;
            if (n == 0) return new[] { 0.0 };
            var d = new double[n];
            for (int i = 0; i < n; i++) d[i] = n * (f[i + 1] - f[i]);
            return d;
        }

        private static double[] Add(double[] a, double[] b)
        {
            Match(ref a, ref b);
            var c = new double[a.Length];
            for (int i = 0; i < c.Length; i++) c[i] = a[i] + b[i];
            return c;
        }

        private static double[] Subtract(double[] a, double[] b)
        {
            Match(ref a, ref b);
            var c = new double[a.Length];
            for (int i = 0; i < c.Length; i++) c[i] = a[i] - b[i];
            return c;
        }

        private static double[] Scale(double[] a, double s)
        {
            var c = new double[a.Length];
            for (int i = 0; i < c.Length; i++) c[i] = a[i] * s;
            return c;
        }

        // raises the lower degree to the higher
        private static void Match(ref double[] a, ref double[] b)
        {
            if (a.Length < b.Length) a = Elevate(a, b.Length - a.Length);
            else if (b.Length < a.Length) b = Elevate(b, a.Length - b.Length);
        }

        private static double Binomial(int n, int k)
        {
            if (k < 0 || k > n) return 0;
            if (k > n - k) k = n - k;
            double c = 1;
            for (int i = 1; i <= k; i++) c = c * (n - k + i) / i;
            return Math.Round(c);
        }
    }
}

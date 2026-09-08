using System;

namespace RCurve
{
    // A single global polynomial, not a lookup table. Default preserves legacy binary search.
    [Serializable]
    public struct CurveIndexPrediction
    {
        public double Cubic;
        public double Quadratic;
        public double Linear;
        public double Constant;
        public double InverseLength;

        public readonly bool IsEnabled => InverseLength > 0;

        public readonly double Evaluate(double timeSinceStart)
        {
            double u = timeSinceStart * InverseLength;
            return ((Cubic * u + Quadratic) * u + Linear) * u + Constant;
        }

        // Validation only, never fitting. Monotonicity plus bounds at every key imply
        // floor(f(x)) is at most one interval away throughout every open interval.
        public bool IsValidFor(ReadOnlySpan<double> times)
        {
            if (!Finite(Cubic) || !Finite(Quadratic) || !Finite(Linear) || !Finite(Constant) ||
                !Finite(InverseLength) || InverseLength < 0) return false;
            if (!IsEnabled) return true;
            if (times.Length < 2) return false;
            double end = (times[times.Length - 1] - times[0]) * InverseLength;
            if (!Finite(end) || !(end > 0)) return false;
            double minimum = Math.Min(Linear, (3 * Cubic * end + 2 * Quadratic) * end + Linear);
            if (Cubic > 0)
            {
                double vertex = -Quadratic / (3 * Cubic);
                if (vertex > 0 && vertex < end)
                    minimum = Math.Min(minimum, (3 * Cubic * vertex + 2 * Quadratic) * vertex + Linear);
            }
            if (!Finite(minimum) || minimum < 0) return false;
            for (int i = 0; i < times.Length; i++)
                if (!(Math.Abs(Evaluate(times[i] - times[0]) - i) < 1 - 1e-6)) return false;
            return true;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

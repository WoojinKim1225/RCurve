using System;

namespace RCurve
{
    // Source coefficients use Bernstein basis; evaluation uses a positive-denominator Horner form.
    public sealed class RationalCurveSegment
    {
        private readonly double _start;
        private readonly double _end;
        private readonly double _inverseLength;
        private readonly double[] _numerator;
        private readonly double[] _denominator;
        private readonly double[] _hornerNumerator;
        private readonly double[] _hornerDenominator;
        private readonly double _startValue;
        private readonly double _endValue;
        private readonly double _startSlope;
        private readonly double _endSlope;

        public int Degree => _denominator.Length - 1;
        public double Start => _start;
        public double End => _end;
        public ReadOnlySpan<double> NumeratorCoefficients => _numerator;
        public ReadOnlySpan<double> DenominatorCoefficients => _denominator;

        // denominatorCoefficients includes q0 = 1; interior coefficients are p2 ... p(n-2).
        // Endpoint value/slope constraints determine p0, p1, p(n-1), pn for every degree.
        public RationalCurveSegment(
            double start, double end,
            double startValue, double endValue,
            double startSlope, double endSlope,
            double[] denominatorCoefficients, double[] interiorNumeratorCoefficients,
            double minimumDenominator = 1e-6)
        {
            if (denominatorCoefficients == null)
                throw new ArgumentNullException(nameof(denominatorCoefficients));
            if (interiorNumeratorCoefficients == null)
                throw new ArgumentNullException(nameof(interiorNumeratorCoefficients));
            int degree = denominatorCoefficients.Length - 1;
            if (degree < 3 || degree > 6)
                throw new ArgumentOutOfRangeException(nameof(denominatorCoefficients), "Supported degrees are 3 through 6.");
            if (interiorNumeratorCoefficients.Length != degree - 3)
                throw new ArgumentException("Expected degree - 3 interior numerator coefficients.", nameof(interiorNumeratorCoefficients));
            if (denominatorCoefficients[0] != 1)
                throw new ArgumentException("q0 must be 1 to fix the common coefficient scale.", nameof(denominatorCoefficients));

            RequireFinite(start, nameof(start));
            RequireFinite(end, nameof(end));
            RequireFinite(startValue, nameof(startValue));
            RequireFinite(endValue, nameof(endValue));
            RequireFinite(startSlope, nameof(startSlope));
            RequireFinite(endSlope, nameof(endSlope));
            RequireFinite(minimumDenominator, nameof(minimumDenominator));
            if (minimumDenominator <= 0 || minimumDenominator > 1)
                throw new ArgumentOutOfRangeException(nameof(minimumDenominator));

            double length = end - start;
            RequireFinite(length, nameof(end));
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(end));
            _inverseLength = 1 / length;
            RequireFinite(_inverseLength, nameof(end));
            foreach (double coefficient in denominatorCoefficients)
            {
                RequireFinite(coefficient, nameof(denominatorCoefficients));
                if (coefficient < minimumDenominator)
                    throw new ArgumentOutOfRangeException(nameof(denominatorCoefficients));
            }

            _start = start;
            _end = end;
            // Do not allow later edits to solver input arrays to invalidate the positivity guarantee.
            _denominator = (double[])denominatorCoefficients.Clone();
            _numerator = new double[degree + 1];
            Array.Copy(interiorNumeratorCoefficients, 0, _numerator, 2, degree - 3);
            _numerator[0] = startValue;
            _numerator[1] = startValue * _denominator[1] + length * startSlope / degree;
            _numerator[degree - 1] = endValue * _denominator[degree - 1] - length * endSlope * _denominator[degree] / degree;
            _numerator[degree] = endValue * _denominator[degree];
            foreach (double coefficient in _numerator)
                RequireFinite(coefficient, nameof(interiorNumeratorCoefficients));
            _startValue = startValue;
            _endValue = endValue;
            _startSlope = startSlope;
            _endSlope = endSlope;
            _hornerNumerator = new double[degree + 1];
            _hornerDenominator = new double[degree + 1];
            double binomial = 1;
            for (int i = 0; i <= degree; i++)
            {
                _hornerNumerator[i] = binomial * _numerator[i];
                _hornerDenominator[i] = binomial * _denominator[i];
                RequireFinite(_hornerNumerator[i], nameof(interiorNumeratorCoefficients));
                RequireFinite(_hornerDenominator[i], nameof(denominatorCoefficients));
                binomial = binomial * (degree - i) / (i + 1);
            }
        }

        public double Evaluate(double time)
        {
            double u = Normalize(time);
            if (time == _start) return _startValue;
            if (time == _end) return _endValue;
            bool reverse = u > 0.5;
            double t = reverse ? (1 - u) / u : u / (1 - u);
            int index = reverse ? 0 : Degree;
            int step = reverse ? 1 : -1;
            double p = _hornerNumerator[index];
            double q = _hornerDenominator[index];
            for (int i = 0; i < Degree; i++)
            {
                index += step;
                p = p * t + _hornerNumerator[index];
                q = q * t + _hornerDenominator[index];
            }
            return p / q;
        }

        public double EvaluateSlope(double time)
        {
            double u = Normalize(time);
            // Preserve authored endpoint slopes without cancellation in the quotient rule.
            if (time == _start) return _startSlope;
            if (time == _end) return _endSlope;
            bool reverse = u > 0.5;
            double denominator = reverse ? u : 1 - u;
            double t = (reverse ? 1 - u : u) / denominator;
            int index = reverse ? 0 : Degree;
            int step = reverse ? 1 : -1;
            double p = _hornerNumerator[index], q = _hornerDenominator[index];
            double dp = 0, dq = 0;
            for (int i = 0; i < Degree; i++)
            {
                index += step;
                dp = dp * t + p;
                dq = dq * t + q;
                p = p * t + _hornerNumerator[index];
                q = q * t + _hornerDenominator[index];
            }
            return (dp - (p / q) * dq) / q * (reverse ? -1 : 1) / (denominator * denominator) * _inverseLength;
        }

        private double Normalize(double time)
        {
            RequireFinite(time, nameof(time));
            // Positivity is guaranteed only inside this interval; do not extrapolate.
            if (time < _start || time > _end)
                throw new ArgumentOutOfRangeException(nameof(time));
            return Math.Max(0, Math.Min(1, (time - _start) * _inverseLength));
        }

        // With t=u/(1-u), the common (1-u)^n factor cancels in P/Q. For u>1/2
        // use t=(1-u)/u and read coefficients in reverse. Thus t stays in [0,1],
        // and Q's Horner coefficients binomial(n,i)*q_i stay positive (no pole-producing
        // cancellation from conversion to alternating power coefficients in u).

        private static void RequireFinite(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameterName, "Value must be finite.");
        }
    }
}

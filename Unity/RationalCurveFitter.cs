using System;

namespace RCurve
{
    // Small bake-time solvers. P and Q are affine in the free Bernstein coefficients.
    // LS is damped Gauss-Newton (local); minimax is sampled LP feasibility + bisection.
    internal sealed class RationalCurveFitter
    {
        internal const int SampleCount = 33;
        private const double MinimumDenominator = 1e-6;
        private readonly int _degree;
        private readonly int _variableCount;
        private readonly double[,] _numeratorBasis;
        private readonly double[,] _denominatorBasis;
        private readonly double[] _numeratorConstant;
        private readonly double[] _denominatorConstant;
        private readonly double[] _normalizedValues;
        private readonly double[] _initialCoefficients;
        private readonly double _valueOffset;
        private readonly double _valueScale;

        internal RationalCurveFitter(int degree, double length, double[] values, double startSlope, double endSlope)
        {
            _degree = degree;
            _variableCount = 2 * degree - 3;
            _valueOffset = values[0];
            double scale = Math.Max(1e-9, Math.Max(Math.Abs(length * startSlope), Math.Abs(length * endSlope)));
            foreach (double value in values) scale = Math.Max(scale, Math.Abs(value - _valueOffset));
            _valueScale = scale;
            _normalizedValues = new double[values.Length];
            for (int i = 0; i < values.Length; i++) _normalizedValues[i] = (values[i] - _valueOffset) / scale;
            startSlope /= scale;
            endSlope /= scale;
            double last = _normalizedValues[_normalizedValues.Length - 1];
            _numeratorBasis = new double[values.Length, _variableCount];
            _denominatorBasis = new double[values.Length, _variableCount];
            _numeratorConstant = new double[values.Length];
            _denominatorConstant = new double[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                double u = (double)i / (values.Length - 1);
                var basis = new double[degree + 1];
                basis[0] = 1;
                for (int n = 1; n <= degree; n++)
                {
                    for (int j = n; j > 0; j--) basis[j] = (1 - u) * basis[j] + u * basis[j - 1];
                    basis[0] *= 1 - u;
                }
                _numeratorConstant[i] = length * startSlope / degree * basis[1];
                _denominatorConstant[i] = basis[0];
                for (int j = 0; j < degree; j++) _denominatorBasis[i, j] = basis[j + 1];
                _numeratorBasis[i, degree - 2] = last * basis[degree - 1];
                _numeratorBasis[i, degree - 1] = last * basis[degree] - length * endSlope / degree * basis[degree - 1];
                for (int j = degree; j < _variableCount; j++) _numeratorBasis[i, j] = basis[j - degree + 2];
            }
            double[] cubic = { 0, length * startSlope / 3, last - length * endSlope / 3, last };
            for (int n = 3; n < degree; n++)
            {
                var elevated = new double[n + 2];
                elevated[0] = cubic[0];
                elevated[n + 1] = cubic[n];
                for (int j = 1; j <= n; j++)
                {
                    double weight = (double)j / (n + 1);
                    elevated[j] = weight * cubic[j - 1] + (1 - weight) * cubic[j];
                }
                cubic = elevated;
            }
            _initialCoefficients = new double[_variableCount];
            for (int j = 0; j < degree; j++) _initialCoefficients[j] = 1;
            for (int j = degree; j < _variableCount; j++) _initialCoefficients[j] = cubic[j - degree + 2];
        }

        internal RationalCurveSegment Fit(double start, double end, double startValue, double endValue,
            double startSlope, double endSlope, CurveFitObjective objective)
        {
            double[] coefficients = objective == CurveFitObjective.Minimax ? Minimax() : LeastSquares();
            var denominator = new double[_degree + 1];
            denominator[0] = 1;
            Array.Copy(coefficients, 0, denominator, 1, _degree);
            var interior = new double[_degree - 3];
            for (int i = 0; i < interior.Length; i++)
                interior[i] = _valueOffset * denominator[i + 2] + _valueScale * coefficients[_degree + i];
            return new RationalCurveSegment(start, end, startValue, endValue, startSlope, endSlope, denominator, interior, MinimumDenominator);
        }

        private void Evaluate(double[] coefficients, int sample, out double p, out double q)
        {
            p = _numeratorConstant[sample];
            q = _denominatorConstant[sample];
            for (int j = 0; j < _variableCount; j++)
            {
                p += _numeratorBasis[sample, j] * coefficients[j];
                q += _denominatorBasis[sample, j] * coefficients[j];
            }
        }

        private double Error(double[] coefficients, bool maximum)
        {
            double result = 0;
            for (int i = 0; i < _normalizedValues.Length; i++)
            {
                Evaluate(coefficients, i, out double p, out double q);
                double error = Math.Abs(p / q - _normalizedValues[i]);
                if (double.IsNaN(error) || double.IsInfinity(error)) return double.PositiveInfinity;
                result = maximum ? Math.Max(result, error) : result + error * error;
            }
            return result;
        }

        private double[] LeastSquares()
        {
            var coefficients = (double[])_initialCoefficients.Clone();
            double score = Error(coefficients, false);
            double damping = 1e-3;
            var jacobian = new double[_variableCount];
            for (int iteration = 0; iteration < 80 && score > 1e-24; iteration++)
            {
                var normal = new double[_variableCount, _variableCount];
                var rhs = new double[_variableCount];
                for (int i = 0; i < _normalizedValues.Length; i++)
                {
                    Evaluate(coefficients, i, out double p, out double q);
                    double value = p / q;
                    for (int j = 0; j < _variableCount; j++) jacobian[j] = (_numeratorBasis[i, j] - value * _denominatorBasis[i, j]) / q;
                    for (int j = 0; j < _variableCount; j++)
                    {
                        rhs[j] -= jacobian[j] * (value - _normalizedValues[i]);
                        for (int k = 0; k < _variableCount; k++) normal[j, k] += jacobian[j] * jacobian[k];
                    }
                }
                for (int j = 0; j < _variableCount; j++) normal[j, j] += damping * (normal[j, j] + 1e-12);
                if (!Solve(normal, rhs)) { damping *= 10; continue; }
                var candidate = new double[_variableCount];
                for (int j = 0; j < _variableCount; j++)
                    candidate[j] = j < _degree ? Math.Max(MinimumDenominator, coefficients[j] + rhs[j]) : coefficients[j] + rhs[j];
                double next = Error(candidate, false);
                if (next < score)
                {
                    double improvement = score - next;
                    coefficients = candidate;
                    score = next;
                    damping = Math.Max(1e-12, damping * 0.3);
                    if (improvement < 1e-15) break;
                }
                else damping *= 10;
            }
            return coefficients;
        }

        private double[] Minimax()
        {
            var best = (double[])_initialCoefficients.Clone();
            double high = Error(best, true);
            if (high < 1e-12) return best;
            double bestError = high;
            double low = 0;
            for (int iteration = 0; iteration < 28; iteration++)
            {
                double bound = (low + high) * 0.5;
                if (TryFeasible(bound, out double[] candidate) && Error(candidate, true) <= bound + 1e-8)
                {
                    high = bound;
                    double error = Error(candidate, true);
                    if (error < bestError) { best = candidate; bestError = error; }
                }
                else low = bound;
            }
            return best;
        }

        private bool TryFeasible(double error, out double[] coefficients)
        {
            // q_i = epsilon + z_i, z_i >= 0. Free numerator coefficients use z+ - z-.
            int variables = _degree + 2 * (_degree - 3);
            var a = new double[2 * _normalizedValues.Length, variables];
            var b = new double[2 * _normalizedValues.Length];
            for (int i = 0; i < _normalizedValues.Length; i++)
            {
                double p0 = _numeratorConstant[i];
                double q0 = _denominatorConstant[i];
                for (int j = 0; j < _degree; j++)
                {
                    p0 += MinimumDenominator * _numeratorBasis[i, j];
                    q0 += MinimumDenominator * _denominatorBasis[i, j];
                }
                for (int side = 0; side < 2; side++)
                {
                    double sign = side == 0 ? 1 : -1;
                    double target = _normalizedValues[i] + sign * error;
                    int row = 2 * i + side;
                    b[row] = sign * (target * q0 - p0);
                    for (int j = 0; j < _degree; j++) a[row, j] = sign * (_numeratorBasis[i, j] - target * _denominatorBasis[i, j]);
                    for (int j = 0; j < _degree - 3; j++)
                    {
                        a[row, _degree + 2 * j] = sign * _numeratorBasis[i, _degree + j];
                        a[row, _degree + 2 * j + 1] = -a[row, _degree + 2 * j];
                    }
                }
            }
            coefficients = null;
            if (!new LinearFeasibility(a, b).Solve(out double[] z)) return false;
            coefficients = new double[_variableCount];
            for (int j = 0; j < _degree; j++) coefficients[j] = MinimumDenominator + z[j];
            for (int j = 0; j < _degree - 3; j++) coefficients[_degree + j] = z[_degree + 2 * j] - z[_degree + 2 * j + 1];
            return true;
        }

        private static bool Solve(double[,] a, double[] b)
        {
            int n = b.Length;
            for (int k = 0; k < n; k++)
            {
                int pivot = k;
                for (int i = k + 1; i < n; i++) if (Math.Abs(a[i, k]) > Math.Abs(a[pivot, k])) pivot = i;
                if (Math.Abs(a[pivot, k]) < 1e-24) return false;
                for (int j = k; j < n; j++) (a[k, j], a[pivot, j]) = (a[pivot, j], a[k, j]);
                (b[k], b[pivot]) = (b[pivot], b[k]);
                for (int i = k + 1; i < n; i++)
                {
                    double factor = a[i, k] / a[k, k];
                    for (int j = k; j < n; j++) a[i, j] -= factor * a[k, j];
                    b[i] -= factor * b[k];
                }
            }
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = i + 1; j < n; j++) b[i] -= a[i, j] * b[j];
                b[i] /= a[i, i];
            }
            return true;
        }

        // Phase-I simplex for A*z <= b, z >= 0. No external solver or runtime dependency.
        private sealed class LinearFeasibility
        {
            private const double Epsilon = 1e-10;
            private readonly int _rows;
            private readonly int _columns;
            private readonly double[,] _table;
            private readonly int[] _basic;
            private readonly int[] _nonbasic;

            internal LinearFeasibility(double[,] a, double[] b)
            {
                _rows = b.Length;
                _columns = a.GetLength(1);
                _table = new double[_rows + 1, _columns + 2];
                _basic = new int[_rows];
                _nonbasic = new int[_columns + 1];
                for (int i = 0; i < _rows; i++)
                {
                    double scale = Math.Max(1, Math.Abs(b[i]));
                    for (int j = 0; j < _columns; j++) scale = Math.Max(scale, Math.Abs(a[i, j]));
                    for (int j = 0; j < _columns; j++) _table[i, j] = a[i, j] / scale;
                    _basic[i] = _columns + i;
                    _table[i, _columns] = -1;
                    _table[i, _columns + 1] = b[i] / scale;
                }
                for (int j = 0; j < _columns; j++) _nonbasic[j] = j;
                _nonbasic[_columns] = -1;
                _table[_rows, _columns] = 1;
            }

            internal bool Solve(out double[] solution)
            {
                solution = null;
                int row = 0;
                for (int i = 1; i < _rows; i++) if (_table[i, _columns + 1] < _table[row, _columns + 1]) row = i;
                if (_table[row, _columns + 1] < -Epsilon)
                {
                    Pivot(row, _columns);
                    bool finished = false;
                    for (int iteration = 0; iteration < 2000; iteration++)
                    {
                        int enter = -1;
                        for (int j = 0; j <= _columns; j++)
                            if (_table[_rows, j] < -Epsilon && (enter < 0 || _nonbasic[j] < _nonbasic[enter])) enter = j;
                        if (enter < 0) { finished = true; break; }
                        int leave = -1;
                        for (int i = 0; i < _rows; i++)
                        {
                            if (_table[i, enter] <= Epsilon) continue;
                            if (leave < 0) { leave = i; continue; }
                            double ratio = _table[i, _columns + 1] / _table[i, enter];
                            double previous = _table[leave, _columns + 1] / _table[leave, enter];
                            if (ratio < previous - Epsilon || (Math.Abs(ratio - previous) <= Epsilon && _basic[i] < _basic[leave])) leave = i;
                        }
                        if (leave < 0) return false;
                        Pivot(leave, enter);
                    }
                    if (!finished) throw new InvalidOperationException("Minimax feasibility solver exceeded its iteration limit.");
                    if (Math.Abs(_table[_rows, _columns + 1]) > Epsilon) return false;
                }
                solution = new double[_columns];
                for (int i = 0; i < _rows; i++)
                    if (_basic[i] >= 0 && _basic[i] < _columns) solution[_basic[i]] = Math.Max(0, _table[i, _columns + 1]);
                return true;
            }

            private void Pivot(int row, int column)
            {
                double inverse = 1 / _table[row, column];
                for (int i = 0; i <= _rows; i++)
                    if (i != row)
                        for (int j = 0; j < _columns + 2; j++)
                            if (j != column) _table[i, j] -= _table[row, j] * _table[i, column] * inverse;
                for (int j = 0; j < _columns + 2; j++) if (j != column) _table[row, j] *= inverse;
                for (int i = 0; i <= _rows; i++) if (i != row) _table[i, column] *= -inverse;
                _table[row, column] = inverse;
                (_basic[row], _nonbasic[column]) = (_nonbasic[column], _basic[row]);
            }
        }
    }
}

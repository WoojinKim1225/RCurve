using System;

namespace RCurve.Editor
{
    // Small editor-only solvers. P and Q are affine in the free Bernstein coefficients.
    // LS is damped Gauss-Newton (local); minimax is sampled LP feasibility + bisection.
    internal sealed class RationalCurveFitter
    {
        internal const int SampleCount = 33;
        private const double _minimumQ = 1e-6;
        private readonly int _degree;
        private readonly int _variableCount;
        private readonly double[,] _p;
        private readonly double[,] _q;
        private readonly double[] _p0;
        private readonly double[] _q0;
        private readonly double[] _y;
        private readonly double[] _initial;
        private readonly double _offset;
        private readonly double _scale;

        internal RationalCurveFitter(int degree, double length, double[] values, double startSlope, double endSlope)
        {
            _degree = degree;
            _variableCount = 2 * degree - 3;
            _offset = values[0];
            double scale = Math.Max(1e-9, Math.Max(Math.Abs(length * startSlope), Math.Abs(length * endSlope)));
            foreach (double value in values) scale = Math.Max(scale, Math.Abs(value - _offset));
            _scale = scale;
            _y = new double[values.Length];
            for (int i = 0; i < values.Length; i++) _y[i] = (values[i] - _offset) / scale;
            startSlope /= scale;
            endSlope /= scale;
            double last = _y[_y.Length - 1];
            _p = new double[values.Length, _variableCount];
            _q = new double[values.Length, _variableCount];
            _p0 = new double[values.Length];
            _q0 = new double[values.Length];
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
                _p0[i] = length * startSlope / degree * basis[1];
                _q0[i] = basis[0];
                for (int j = 0; j < degree; j++) _q[i, j] = basis[j + 1];
                _p[i, degree - 2] = last * basis[degree - 1];
                _p[i, degree - 1] = last * basis[degree] - length * endSlope / degree * basis[degree - 1];
                for (int j = degree; j < _variableCount; j++) _p[i, j] = basis[j - degree + 2];
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
            _initial = new double[_variableCount];
            for (int j = 0; j < degree; j++) _initial[j] = 1;
            for (int j = degree; j < _variableCount; j++) _initial[j] = cubic[j - degree + 2];
        }

        internal RationalCurveSegment Fit(double start, double end, double startValue, double endValue,
            double startSlope, double endSlope, CurveFitObjective objective)
        {
            double[] x = objective == CurveFitObjective.Minimax ? Minimax() : LeastSquares();
            var denominator = new double[_degree + 1];
            denominator[0] = 1;
            Array.Copy(x, 0, denominator, 1, _degree);
            var interior = new double[_degree - 3];
            for (int i = 0; i < interior.Length; i++)
                interior[i] = _offset * denominator[i + 2] + _scale * x[_degree + i];
            return new RationalCurveSegment(start, end, startValue, endValue, startSlope, endSlope, denominator, interior, _minimumQ);
        }

        private void Evaluate(double[] x, int sample, out double p, out double q)
        {
            p = _p0[sample];
            q = _q0[sample];
            for (int j = 0; j < _variableCount; j++)
            {
                p += _p[sample, j] * x[j];
                q += _q[sample, j] * x[j];
            }
        }

        private double Error(double[] x, bool maximum)
        {
            double result = 0;
            for (int i = 0; i < _y.Length; i++)
            {
                Evaluate(x, i, out double p, out double q);
                double error = Math.Abs(p / q - _y[i]);
                if (double.IsNaN(error) || double.IsInfinity(error)) return double.PositiveInfinity;
                result = maximum ? Math.Max(result, error) : result + error * error;
            }
            return result;
        }

        private double[] LeastSquares()
        {
            var x = (double[])_initial.Clone();
            double score = Error(x, false);
            double damping = 1e-3;
            var jacobian = new double[_variableCount];
            for (int iteration = 0; iteration < 80 && score > 1e-24; iteration++)
            {
                var normal = new double[_variableCount, _variableCount];
                var rhs = new double[_variableCount];
                for (int i = 0; i < _y.Length; i++)
                {
                    Evaluate(x, i, out double p, out double q);
                    double value = p / q;
                    for (int j = 0; j < _variableCount; j++) jacobian[j] = (_p[i, j] - value * _q[i, j]) / q;
                    for (int j = 0; j < _variableCount; j++)
                    {
                        rhs[j] -= jacobian[j] * (value - _y[i]);
                        for (int k = 0; k < _variableCount; k++) normal[j, k] += jacobian[j] * jacobian[k];
                    }
                }
                for (int j = 0; j < _variableCount; j++) normal[j, j] += damping * (normal[j, j] + 1e-12);
                if (!Solve(normal, rhs)) { damping *= 10; continue; }
                var candidate = new double[_variableCount];
                for (int j = 0; j < _variableCount; j++)
                    candidate[j] = j < _degree ? Math.Max(_minimumQ, x[j] + rhs[j]) : x[j] + rhs[j];
                double next = Error(candidate, false);
                if (next < score)
                {
                    double improvement = score - next;
                    x = candidate;
                    score = next;
                    damping = Math.Max(1e-12, damping * 0.3);
                    if (improvement < 1e-15) break;
                }
                else damping *= 10;
            }
            return x;
        }

        private double[] Minimax()
        {
            var best = (double[])_initial.Clone();
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
            var a = new double[2 * _y.Length, variables];
            var b = new double[2 * _y.Length];
            for (int i = 0; i < _y.Length; i++)
            {
                double p0 = _p0[i];
                double q0 = _q0[i];
                for (int j = 0; j < _degree; j++) { p0 += _minimumQ * _p[i, j]; q0 += _minimumQ * _q[i, j]; }
                for (int side = 0; side < 2; side++)
                {
                    double sign = side == 0 ? 1 : -1;
                    double target = _y[i] + sign * error;
                    int row = 2 * i + side;
                    b[row] = sign * (target * q0 - p0);
                    for (int j = 0; j < _degree; j++) a[row, j] = sign * (_p[i, j] - target * _q[i, j]);
                    for (int j = 0; j < _degree - 3; j++)
                    {
                        a[row, _degree + 2 * j] = sign * _p[i, _degree + j];
                        a[row, _degree + 2 * j + 1] = -a[row, _degree + 2 * j];
                    }
                }
            }
            coefficients = null;
            if (!new LinearFeasibility(a, b).Solve(out double[] z)) return false;
            coefficients = new double[_variableCount];
            for (int j = 0; j < _degree; j++) coefficients[j] = _minimumQ + z[j];
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
            private const double _epsilon = 1e-10;
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
                if (_table[row, _columns + 1] < -_epsilon)
                {
                    Pivot(row, _columns);
                    bool finished = false;
                    for (int iteration = 0; iteration < 2000; iteration++)
                    {
                        int enter = -1;
                        for (int j = 0; j <= _columns; j++)
                            if (_table[_rows, j] < -_epsilon && (enter < 0 || _nonbasic[j] < _nonbasic[enter])) enter = j;
                        if (enter < 0) { finished = true; break; }
                        int leave = -1;
                        for (int i = 0; i < _rows; i++)
                        {
                            if (_table[i, enter] <= _epsilon) continue;
                            if (leave < 0) { leave = i; continue; }
                            double ratio = _table[i, _columns + 1] / _table[i, enter];
                            double previous = _table[leave, _columns + 1] / _table[leave, enter];
                            if (ratio < previous - _epsilon || (Math.Abs(ratio - previous) <= _epsilon && _basic[i] < _basic[leave])) leave = i;
                        }
                        if (leave < 0) return false;
                        Pivot(leave, enter);
                    }
                    if (!finished) throw new InvalidOperationException("Minimax feasibility solver exceeded its iteration limit.");
                    if (Math.Abs(_table[_rows, _columns + 1]) > _epsilon) return false;
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

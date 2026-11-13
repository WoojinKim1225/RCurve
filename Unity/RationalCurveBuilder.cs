using System;
using System.Collections.Generic;
using UnityEngine;

namespace RCurve
{
    public static class RationalCurveBuilder
    {
        public static BakedRationalCurve Build(AnimationCurve source, CurveFitSettings settings = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            settings ??= new CurveFitSettings();
            settings.Validate();
            CurveWrapMode preWrapMode = ConvertWrapMode(source.preWrapMode);
            CurveWrapMode postWrapMode = ConvertWrapMode(source.postWrapMode);
            AnimationCurveBezierSegment[] sourceSegments = AnimationCurveBezierSegment.FromCurve(source);
            if (sourceSegments.Length == 0) return FromSegments(source, Array.Empty<RationalCurveSegment>(), settings.LookupMode);
            var times = new List<double> { sourceSegments[0].StartTime };
            var values = new List<double> { sourceSegments[0].P0.y };
            var segments = new List<RationalCurveSegment>();
            foreach (AnimationCurveBezierSegment sourceSegment in sourceSegments)
            {
                if (sourceSegment.IsStep)
                {
                    times.Add(sourceSegment.EndTime);
                    values.Add(sourceSegment.P3.y);
                    segments.Add(RationalCurveSegment.Step(sourceSegment.StartTime, sourceSegment.EndTime, sourceSegment.P0.y));
                    continue;
                }
                if (sourceSegment.HasStationaryX)
                    throw new NotSupportedException("A zero-length handle or stationary X needs separate tangent analysis. Increase the handle weight before fitting.");
                FitInterval(sourceSegment, 0, 1, 0, settings, times, values, segments);
            }
            return Assemble(times.ToArray(), values.ToArray(), segments.ToArray(), preWrapMode, postWrapMode, settings.LookupMode);
        }

        private static void FitInterval(AnimationCurveBezierSegment source, double left, double right, int depth,
            CurveFitSettings settings, List<double> times, List<double> values, List<RationalCurveSegment> segments)
        {
            double length = source.EndTime - source.StartTime;
            double start = left == 0 ? source.StartTime : source.StartTime + length * left;
            double end = right == 1 ? source.EndTime : source.StartTime + length * right;
            Sample(source, left, out double first, out double startSlope);
            Sample(source, right, out double last, out double endSlope);
            var samples = new double[RationalCurveFitter.SampleCount];
            for (int i = 0; i < samples.Length; i++)
                Sample(source, left + (right - left) * i / (samples.Length - 1), out samples[i], out _);
            var fitter = new RationalCurveFitter(settings.Degree, end - start, samples, startSlope, endSlope);
            RationalCurveSegment segment = fitter.Fit(start, end, first, last, startSlope, endSlope, settings.Objective);
            // Proven over the whole interval (not at samples): see SegmentCertificate.
            if (SegmentCertificate.Holds(source, Parameter(source, left), Parameter(source, right), left, right, segment,
                    settings.MaxValueError, settings.MaxSlopeError))
            {
                times.Add(end);
                values.Add(last);
                segments.Add(segment);
                return;
            }
            double middle = (left + right) * 0.5;
            if (depth >= settings.MaxSubdivisionDepth || middle <= left || middle >= right)
                throw new InvalidOperationException($"Curve fit exceeded error limits on [{start}, {end}] at subdivision depth {depth}.");
            FitInterval(source, left, middle, depth + 1, settings, times, values, segments);
            FitInterval(source, middle, right, depth + 1, settings, times, values, segments);
        }

        // The Bezier parameter where the source reaches normalizedTime (X is nondecreasing).
        private static double Parameter(AnimationCurveBezierSegment source, double normalizedTime)
        {
            if (!(normalizedTime > 0 && normalizedTime < 1)) return normalizedTime;
            double low = 0;
            double high = 1;
            for (int i = 0; i < 55; i++)
            {
                double middle = (low + high) * 0.5;
                if (source.Evaluate(middle).x < normalizedTime) low = middle;
                else high = middle;
            }
            return (low + high) * 0.5;
        }

        private static void Sample(AnimationCurveBezierSegment source, double normalizedTime, out double value, out double slope)
        {
            double bezierParameter = Parameter(source, normalizedTime);
            value = source.Evaluate(bezierParameter).y;
            var derivative = source.EvaluateDerivative(bezierParameter);
            slope = derivative.y / (derivative.x * (source.EndTime - source.StartTime));
            if (double.IsNaN(slope) || double.IsInfinity(slope))
                throw new NotSupportedException("The source has no finite slope at a fitting sample.");
        }

        // Assembles already-fitted intervals; only the optional index predictor is fitted here.
        // Null entries are allowed only for source step intervals. The result owns a snapshot.
        internal static BakedRationalCurve FromSegments(AnimationCurve source, RationalCurveSegment[] segments,
            CurveLookupMode lookupMode = CurveLookupMode.Auto)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (segments == null) throw new ArgumentNullException(nameof(segments));
            Keyframe[] keys = source.keys;
            if (segments.Length != Math.Max(0, keys.Length - 1))
                throw new ArgumentException("Expected one fitted segment per source interval.", nameof(segments));
            var times = new double[keys.Length];
            var values = new double[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                times[i] = keys[i].time;
                values[i] = keys[i].value;
                if (i < segments.Length)
                {
                    var original = new AnimationCurveBezierSegment(keys[i], keys[i + 1]);
                    if (original.IsStep != (segments[i] == null || segments[i].IsStep))
                        throw new ArgumentException("Steps need step (or null) entries; smooth intervals need a successful fit.", nameof(segments));
                }
            }
            return Assemble(times, values, segments,
                ConvertWrapMode(source.preWrapMode), ConvertWrapMode(source.postWrapMode), lookupMode);
        }

        private static BakedRationalCurve Assemble(double[] times, double[] values, RationalCurveSegment[] segments,
            CurveWrapMode preWrapMode, CurveWrapMode postWrapMode, CurveLookupMode lookupMode)
        {
            CurveIndexPrediction prediction = default;
            if (lookupMode == CurveLookupMode.Auto || lookupMode == CurveLookupMode.Magic)
            {
                prediction = FitIndexPrediction(times, 1);
                if (!prediction.IsEnabled && times.Length >= 4) prediction = FitIndexPrediction(times, 3);
            }
            CurveLookupPlanner.Plan(times, prediction, lookupMode,
                out var managedLookup, out var burstLookup, out var lookupTable, out double lookupScale);
            return new BakedRationalCurve(times, values, segments, preWrapMode, postWrapMode,
                prediction, lookupMode, managedLookup, burstLookup, lookupTable, lookupScale);
        }

        private static CurveIndexPrediction FitIndexPrediction(double[] times, int degree)
        {
            if (times.Length < 2) return default;
            double inverseLength = 1 / (times[times.Length - 1] - times[0]);
            if (double.IsInfinity(inverseLength)) return default;
            int count = degree + 1;
            var system = new double[count, count + 1];
            var powers = new double[2 * degree + 1];
            for (int i = 0; i < times.Length; i++)
            {
                double u = (times[i] - times[0]) * inverseLength;
                powers[0] = 1;
                for (int k = 1; k < powers.Length; k++) powers[k] = powers[k - 1] * u;
                for (int row = 0; row < count; row++)
                {
                    for (int column = 0; column < count; column++) system[row, column] += powers[row + column];
                    system[row, count] += powers[row] * i;
                }
            }
            // Tiny bake-time least-squares system. Ill-conditioned fits simply keep binary search.
            for (int column = 0; column < count; column++)
            {
                int pivot = column;
                for (int row = column + 1; row < count; row++)
                    if (Math.Abs(system[row, column]) > Math.Abs(system[pivot, column])) pivot = row;
                if (!(Math.Abs(system[pivot, column]) > 1e-12)) return default;
                for (int k = column; k <= count; k++)
                    (system[column, k], system[pivot, k]) = (system[pivot, k], system[column, k]);
                double divisor = system[column, column];
                for (int k = column; k <= count; k++) system[column, k] /= divisor;
                for (int row = 0; row < count; row++)
                {
                    if (row == column) continue;
                    double factor = system[row, column];
                    for (int k = column; k <= count; k++) system[row, k] -= factor * system[column, k];
                }
            }
            var prediction = new CurveIndexPrediction
            {
                Constant = system[0, count], Linear = system[1, count],
                Quadratic = degree == 3 ? system[2, count] : 0,
                Cubic = degree == 3 ? system[3, count] : 0, InverseLength = inverseLength
            };
            double minResidual = double.PositiveInfinity;
            double maxResidual = double.NegativeInfinity;
            for (int i = 0; i < times.Length; i++)
            {
                double residual = prediction.Evaluate(times[i] - times[0]) - i;
                minResidual = Math.Min(minResidual, residual);
                maxResidual = Math.Max(maxResidual, residual);
            }
            // Center key-index errors without changing the derivative; certify the shifted fit again.
            prediction.Constant -= 0.5 * minResidual + 0.5 * maxResidual;
            return prediction.IsValidFor(times) ? prediction : default;
        }

        private static CurveWrapMode ConvertWrapMode(WrapMode mode)
        {
            // Unity's Clamp is an alias of Once; standalone Evaluate clamps for both.
            return mode switch
            {
                WrapMode.Default => CurveWrapMode.Default,
                WrapMode.Once => CurveWrapMode.Once,
                WrapMode.ClampForever => CurveWrapMode.ClampForever,
                WrapMode.Loop => CurveWrapMode.Loop,
                WrapMode.PingPong => CurveWrapMode.PingPong,
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
        }
    }
}

using System;
using Unity.Mathematics;
using UnityEngine;

namespace RCurve
{
    // Parametric source for fitting in both Editor and player. Control points use normalized X and unchanged Y.
    public sealed class AnimationCurveBezierSegment
    {
        public double StartTime { get; }
        public double EndTime { get; }
        public double2 P0 { get; }
        public double2 P1 { get; }
        public double2 P2 { get; }
        public double2 P3 { get; }
        public bool IsStep { get; }

        // dx/dt can vanish: this may be a vertical tangent OR a removable degeneracy.
        // Do not fit these with ordinary finite endpoint-slope constraints without analysis.
        public bool HasStationaryX { get; }

        public AnimationCurveBezierSegment(Keyframe start, Keyframe end)
        {
            RequireFinite(start.time, nameof(start));
            RequireFinite(end.time, nameof(end));
            RequireFinite(start.value, nameof(start));
            RequireFinite(end.value, nameof(end));
            if (end.time <= start.time)
                throw new ArgumentException("Key times must be strictly increasing.", nameof(end));
            if (float.IsNaN(start.outTangent) || float.IsNaN(end.inTangent))
                throw new ArgumentException("Segment tangents must not be NaN.");

            StartTime = start.time;
            EndTime = end.time;
            P0 = new double2(0, start.value);
            P3 = new double2(1, end.value);
            IsStep = float.IsInfinity(start.outTangent) || float.IsInfinity(end.inTangent);
            if (IsStep)
            {
                // A discontinuity is not a cubic Bezier. Evaluate handles it separately.
                P1 = P0;
                P2 = P3;
                return;
            }

            double outWeight = GetWeight(start.outWeight, (start.weightedMode & WeightedMode.Out) != 0);
            double inWeight = GetWeight(end.inWeight, (end.weightedMode & WeightedMode.In) != 0);
            double length = EndTime - StartTime;
            P1 = new double2(outWeight, start.value + length * outWeight * start.outTangent);
            P2 = new double2(1 - inWeight, end.value - length * inWeight * end.inTangent);

            // With both weights in [0,1], X is nondecreasing even if P1.x > P2.x.
            // Its derivative can vanish only at a zero-weight endpoint or at t=1/2
            // when both weights are 1. Reject out-of-range weights rather than clamp.
            HasStationaryX = outWeight == 0 || inWeight == 0 || (outWeight == 1 && inWeight == 1);
        }

        // Empty/single-key curves have no intervals. Wrap modes are deliberately not applied.
        public static AnimationCurveBezierSegment[] FromCurve(AnimationCurve curve)
        {
            if (curve == null)
                throw new ArgumentNullException(nameof(curve));
            Keyframe[] keys = curve.keys;
            if (keys.Length < 2)
                return Array.Empty<AnimationCurveBezierSegment>();

            var segments = new AnimationCurveBezierSegment[keys.Length - 1];
            for (int i = 0; i < segments.Length; i++)
                segments[i] = new AnimationCurveBezierSegment(keys[i], keys[i + 1]);
            return segments;
        }

        // t is the Bezier parameter, NOT normalized X. Fitting samples are (result.x, result.y).
        public double2 Evaluate(double t)
        {
            RequireParameter(t);
            if (IsStep)
                return new double2(t, t == 1 ? P3.y : P0.y);

            double2 a = math.lerp(P0, P1, t);
            double2 b = math.lerp(P1, P2, t);
            double2 c = math.lerp(P2, P3, t);
            return math.lerp(math.lerp(a, b, t), math.lerp(b, c, t), t);
        }

        // Parametric derivative (du/dt, dy/dt), not dy/dx.
        // For nonzero dx/dt: dy/dx = result.y / (result.x * (EndTime - StartTime)).
        public double2 EvaluateDerivative(double t)
        {
            RequireParameter(t);
            if (IsStep)
                throw new InvalidOperationException("Step intervals cannot supply smooth fitting derivatives.");

            return 3 * math.lerp(
                math.lerp(P1 - P0, P2 - P1, t),
                math.lerp(P2 - P1, P3 - P2, t), t);
        }

        private static double GetWeight(float weight, bool weighted)
        {
            if (!weighted)
                return 1.0 / 3;
            RequireFinite(weight, nameof(weight));
            if (weight < 0 || weight > 1)
                throw new ArgumentOutOfRangeException(nameof(weight), "Weights must be in [0,1].");
            return weight;
        }

        private static void RequireParameter(double t)
        {
            RequireFinite(t, nameof(t));
            if (t < 0 || t > 1)
                throw new ArgumentOutOfRangeException(nameof(t));
        }

        private static void RequireFinite(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameterName, "Value must be finite.");
        }
    }
}

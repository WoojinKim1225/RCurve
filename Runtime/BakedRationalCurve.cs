using System;

namespace RCurve
{
    // Independent of UnityEngine.WrapMode. Unity 6.6 evaluates explicit Default like Loop;
    // Once (also named Clamp in Unity) and ClampForever clamp.
    public enum CurveWrapMode
    {
        Default,
        Once,
        ClampForever,
        Loop,
        PingPong
    }

    public sealed class BakedRationalCurve
    {
        private readonly double[] _times;
        private readonly double[] _values;
        private readonly RationalCurveSegment[] _segments;
        private readonly CurveIndexPrediction _indexPrediction;
        private readonly int[] _lookupTable;

        public CurveWrapMode PreWrapMode { get; }
        public CurveWrapMode PostWrapMode { get; }
        public int SegmentCount => _segments.Length;
        internal CurveIndexPrediction IndexPrediction => _indexPrediction;
        public CurveLookupMode LookupMode { get; }
        public CurveLookupStrategy ManagedLookup { get; }
        public CurveLookupStrategy BurstLookup { get; }
        internal double LookupScale { get; }
        internal ReadOnlySpan<int> LookupTable => _lookupTable;
        public ReadOnlySpan<double> KeyTimes => _times;
        public ReadOnlySpan<double> KeyValues => _values;
        public ReadOnlySpan<RationalCurveSegment> Segments => _segments;

        // The first and last keys' times and values (the stored key values, exactly as authored). A curve with no
        // keys has neither, and throws.
        public double StartTime => _times.Length > 0 ? _times[0] : throw NoKeys();
        public double EndTime => _times.Length > 0 ? _times[_times.Length - 1] : throw NoKeys();
        public double StartValue => _values.Length > 0 ? _values[0] : throw NoKeys();
        public double EndValue => _values.Length > 0 ? _values[_values.Length - 1] : throw NoKeys();

        private static InvalidOperationException NoKeys() => new InvalidOperationException("The curve has no keys.");

        // A null segment explicitly represents a step, not a missing/failed fit.
        // Each interval owns [times[i], times[i+1]); exact mapped keys return stored values.
        // Post Loop/Default wraps even the exact final key time to the first key.
        internal BakedRationalCurve(double[] times, double[] values, RationalCurveSegment[] segments,
            CurveWrapMode preWrapMode = CurveWrapMode.ClampForever,
            CurveWrapMode postWrapMode = CurveWrapMode.ClampForever,
            CurveIndexPrediction indexPrediction = default, CurveLookupMode lookupMode = CurveLookupMode.Auto,
            CurveLookupStrategy? managedLookup = null, CurveLookupStrategy? burstLookup = null,
            int[] lookupTable = null, double lookupScale = 0)
        {
            if (times == null) throw new ArgumentNullException(nameof(times));
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (segments == null) throw new ArgumentNullException(nameof(segments));
            if (values.Length != times.Length || segments.Length != Math.Max(0, times.Length - 1))
                throw new ArgumentException("Expected one value per key and one segment per interval.");
            ValidateWrapMode(preWrapMode);
            ValidateWrapMode(postWrapMode);
            for (int i = 0; i < times.Length; i++)
            {
                RequireFinite(times[i]);
                RequireFinite(values[i]);
                if (i > 0 && times[i] <= times[i - 1])
                    throw new ArgumentException("Key times must be strictly increasing.", nameof(times));
            }
            if (times.Length > 1)
                RequireFinite(2 * (times[times.Length - 1] - times[0]));
            for (int i = 0; i < segments.Length; i++)
            {
                RationalCurveSegment segment = segments[i];
                if (segment != null && (segment.Start != times[i] || segment.End != times[i + 1] ||
                    !NearlyEqual(segment.Evaluate(times[i]), values[i]) ||
                    !NearlyEqual(segment.Evaluate(times[i + 1]), values[i + 1])))
                    throw new ArgumentException("Rational segment bounds and endpoint values must match its keys.", nameof(segments));
            }
            if (!indexPrediction.IsValidFor(times))
                throw new ArgumentException("Index predictor must be monotone with key-index error less than one.", nameof(indexPrediction));
            if (lookupMode < CurveLookupMode.Auto || lookupMode > CurveLookupMode.Magic)
                throw new ArgumentOutOfRangeException(nameof(lookupMode));
            LookupMode = lookupMode;
            ManagedLookup = managedLookup ?? (indexPrediction.IsEnabled ? CurveLookupStrategy.Magic : CurveLookupStrategy.BinarySearch);
            BurstLookup = burstLookup ?? ManagedLookup;
            if (ManagedLookup < CurveLookupStrategy.BinarySearch || ManagedLookup > CurveLookupStrategy.Magic ||
                BurstLookup < CurveLookupStrategy.BinarySearch || BurstLookup > CurveLookupStrategy.Magic)
                throw new ArgumentOutOfRangeException(nameof(managedLookup));
            if ((ManagedLookup == CurveLookupStrategy.Magic || BurstLookup == CurveLookupStrategy.Magic) && !indexPrediction.IsEnabled)
                throw new ArgumentException("Magic needs a certified predictor.", nameof(indexPrediction));
            _lookupTable = lookupTable == null ? Array.Empty<int>() : (int[])lookupTable.Clone();
            RequireFinite(lookupScale);
            if (_lookupTable.Length > 256) throw new ArgumentException("LUT exceeds the 1 KiB budget.", nameof(lookupTable));
            for (int i = 0; i < _lookupTable.Length; i++)
                if (_lookupTable[i] < 0 || _lookupTable[i] >= segments.Length || (i > 0 && _lookupTable[i] < _lookupTable[i - 1]))
                    throw new ArgumentException("LUT indices must be valid and nondecreasing.", nameof(lookupTable));
            if ((ManagedLookup == CurveLookupStrategy.LUT || BurstLookup == CurveLookupStrategy.LUT) &&
                (_lookupTable.Length < 2 || !(lookupScale > 0) || times.Length < 2 ||
                 double.IsInfinity((times[times.Length - 1] - times[0]) * lookupScale)))
                throw new ArgumentException("LUT needs a finite scale and at least one cell.", nameof(lookupTable));
            LookupScale = lookupScale;
            _indexPrediction = indexPrediction;
            _times = (double[])times.Clone();
            _values = (double[])values.Clone();
            _segments = (RationalCurveSegment[])segments.Clone();
            PreWrapMode = preWrapMode;
            PostWrapMode = postWrapMode;
        }

        public double Evaluate(double time) => Evaluate(time, -1, out _);

        // The hint is optional caller-owned state. Any stale/invalid value is checked before use.
        public double Evaluate(double time, ref int intervalHint) => Evaluate(time, intervalHint, out intervalHint);

        private double Evaluate(double time, int intervalHint, out int resolvedInterval)
        {
            RequireFinite(time);
            resolvedInterval = -1;
            if (_times.Length == 0) return 0;
            if (_times.Length == 1) return _values[0];
            double mapped = MapTime(time, out _);
            int key = FindKey(mapped, intervalHint, out resolvedInterval);
            if (key >= 0) return _values[key];
            return _segments[resolvedInterval]?.Evaluate(mapped) ?? _values[resolvedInterval];
        }

        // NaN means no two-sided derivative: a step, corner, wrap seam or reflection cusp.
        // At smooth reflected portions the sign reverses; strictly clamped regions return zero.
        public double EvaluateSlope(double time)
        {
            RequireFinite(time);
            if (_times.Length < 2) return 0;
            double mapped = MapTime(time, out int direction);
            if (direction == 0) return 0;
            int key = FindKey(mapped);
            if (key < 0)
                return direction * (_segments[~key - 1]?.EvaluateSlope(mapped) ?? 0);
            if (key > 0 && key < _times.Length - 1)
                return direction * JoinDerivative(_values[key], Limit(key - 1, true), Limit(key, false));

            bool outside = time < _times[0] || time > _times[_times.Length - 1] ||
                (time == _times[_times.Length - 1] && IsLoop(PostWrapMode));
            CurveWrapMode mode = time < _times[0] ? PreWrapMode : PostWrapMode;
            if (!outside) mode = key == 0 ? PreWrapMode : PostWrapMode;
            var first = Limit(0, false);
            var last = Limit(_segments.Length - 1, true);
            if (outside && IsLoop(mode))
                return JoinDerivative(_values[key], last, first);
            if (key == 0)
            {
                var left = IsLoop(mode) ? last :
                    mode == CurveWrapMode.PingPong ? (first.value, -first.slope) : (_values[0], 0.0);
                return JoinDerivative(_values[0], left, first);
            }
            var right = IsLoop(mode) ? first :
                mode == CurveWrapMode.PingPong ? (last.value, -last.slope) : (_values[key], 0.0);
            return JoinDerivative(_values[key], last, right);
        }

        private int FindKey(double time) => FindKey(time, -1, out _);

        private int FindKey(double time, int intervalHint, out int resolvedInterval)
        {
            int key;
            resolvedInterval = -1;
            if ((uint)intervalHint < (uint)_segments.Length)
            {
                if (TryInterval(time, intervalHint, out key, out resolvedInterval)) return key;
                if (TryInterval(time, intervalHint + 1, out key, out resolvedInterval)) return key;
                if (TryInterval(time, intervalHint - 1, out key, out resolvedInterval)) return key;
            }
            if (ManagedLookup == CurveLookupStrategy.BruteForce)
            {
                int interval = 0;
                while (interval + 1 < _segments.Length && time >= _times[interval + 1]) interval++;
                if (TryInterval(time, interval, out key, out resolvedInterval)) return key;
            }
            else if (ManagedLookup == CurveLookupStrategy.LUT)
            {
                int cell = (int)Math.Max(0, Math.Min(_lookupTable.Length - 2, (time - _times[0]) * LookupScale));
                int low = _lookupTable[cell];
                int high = Math.Min(_segments.Length, _lookupTable[cell + 1] + 1);
                if (TryInterval(time, low, out key, out resolvedInterval)) return key;
                // Stored/rounded cell bounds are only a hint: confirm the search bracket first.
                if (time >= _times[low] && time <= _times[high])
                {
                    key = Array.BinarySearch(_times, low, high - low + 1, time);
                    resolvedInterval = key >= 0 ? Math.Min(key, _segments.Length - 1) : ~key - 1;
                    return key;
                }
            }
            else if (ManagedLookup == CurveLookupStrategy.Magic)
            {
                // Clamping nonnegative before the cast makes truncation equivalent to floor.
                int interval = (int)Math.Max(0, Math.Min(_segments.Length - 1,
                    _indexPrediction.Evaluate(time - _times[0])));
                if (time < _times[interval] && interval > 0) interval--;
                else if (time >= _times[interval + 1] && interval + 1 < _segments.Length) interval++;
                if (TryInterval(time, interval, out key, out resolvedInterval)) return key;
                // Unreachable while the predictor's certificate holds (checked when the curve is built or loaded):
                // monotone over the whole domain and within 1 - 1e-6 of every key's index, so floor(f) is at most one
                // interval off and the single correction above lands. Getting here means the certificate is broken.
                throw new InvalidOperationException(
                    $"Magic lookup missed time {time}: the index predictor's certificate does not hold.");
            }
            key = Array.BinarySearch(_times, time);
            resolvedInterval = key >= 0 ? Math.Min(key, _segments.Length - 1) : ~key - 1;
            return key;
        }

        private bool TryInterval(double time, int interval, out int key, out int resolvedInterval)
        {
            key = 0;
            resolvedInterval = -1;
            if ((uint)interval >= (uint)_segments.Length) return false;
            double left = _times[interval], right = _times[interval + 1];
            if (time == left) { key = interval; resolvedInterval = interval; return true; }
            if (time == right) { key = interval + 1; resolvedInterval = Math.Min(interval + 1, _segments.Length - 1); return true; }
            if (time > left && time < right) { key = ~(interval + 1); resolvedInterval = interval; return true; }
            return false;
        }

        private double MapTime(double time, out int direction)
        {
            double first = _times[0];
            double last = _times[_times.Length - 1];
            direction = 1;
            if (time >= first && (time < last || (time == last && !IsLoop(PostWrapMode)))) return time;
            CurveWrapMode mode = time < first ? PreWrapMode : PostWrapMode;
            if (!IsLoop(mode) && mode != CurveWrapMode.PingPong)
            {
                direction = 0;
                return time < first ? first : last;
            }
            double length = last - first;
            double period = mode == CurveWrapMode.PingPong ? 2 * length : length;
            // Reduce operands first to avoid overflowing time - first for large finite inputs.
            double phase = (time % period - first % period) % period;
            RequireFinite(phase);
            if (phase < 0) phase += period;
            if (mode == CurveWrapMode.PingPong && phase > length)
            {
                phase = period - phase;
                direction = -1;
            }
            return Math.Max(first, Math.Min(last, first + phase));
        }

        private (double value, double slope) Limit(int interval, bool atEnd)
        {
            int key = atEnd ? interval + 1 : interval;
            RationalCurveSegment segment = _segments[interval];
            return segment == null ? (_values[interval], 0) :
                (_values[key], segment.EvaluateSlope(_times[key]));
        }

        private static double JoinDerivative(double value, (double value, double slope) left,
            (double value, double slope) right)
        {
            return left.value == value && right.value == value && NearlyEqual(left.slope, right.slope)
                ? 0.5 * left.slope + 0.5 * right.slope : double.NaN;
        }

        private static bool NearlyEqual(double a, double b)
        {
            return a == b || Math.Abs(a - b) <= 1e-10 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
        }

        private static bool IsLoop(CurveWrapMode mode)
        {
            return mode == CurveWrapMode.Loop || mode == CurveWrapMode.Default;
        }

        private static void ValidateWrapMode(CurveWrapMode mode)
        {
            if (mode < CurveWrapMode.Default || mode > CurveWrapMode.PingPong)
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        private static void RequireFinite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Value must be finite.");
        }
    }
}

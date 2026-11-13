using System;
using Unity.Collections;

namespace RCurve
{
    // Copies share native allocations. Complete all readers, then Dispose exactly one owner.
    // Evaluation is allocation-free and Burst-compatible; Create is a managed snapshot operation.
    public struct NativeRationalCurve : IDisposable
    {
        private enum SegmentKind : byte { Constant, Rational }

        private struct Segment
        {
            public SegmentKind Kind;
            public int Degree;
            public int CoefficientOffset;
            public double InverseLength;
            public double StartSlope;
            public double EndSlope;
        }

        [ReadOnly] private NativeArray<double> _times;
        [ReadOnly] private NativeArray<double> _values;
        [ReadOnly] private NativeArray<Segment> _segments;
        [ReadOnly] private NativeArray<double> _coefficients;
        private CurveWrapMode _preWrapMode;
        private CurveWrapMode _postWrapMode;
        private CurveIndexPrediction _indexPrediction;
        [ReadOnly] private NativeArray<int> _lookupTable;
        private double _lookupScale;
        private CurveLookupStrategy _lookupStrategy;

        public bool IsCreated => _times.IsCreated;
        public int SegmentCount => _segments.Length;
        public CurveLookupStrategy LookupStrategy => _lookupStrategy;
        public CurveWrapMode PreWrapMode => _preWrapMode;
        public CurveWrapMode PostWrapMode => _postWrapMode;

        public static NativeRationalCurve Create(BakedRationalCurve source, Allocator allocator)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (allocator != Allocator.Temp && allocator != Allocator.TempJob && allocator != Allocator.Persistent)
                throw new ArgumentOutOfRangeException(nameof(allocator));
            int coefficientCount = 0;
            foreach (RationalCurveSegment segment in source.Segments)
                if (segment != null) coefficientCount = checked(coefficientCount + 2 * (segment.Degree + 1));

            var result = new NativeRationalCurve();
            try
            {
                result._times = new NativeArray<double>(source.KeyTimes.Length, allocator, NativeArrayOptions.UninitializedMemory);
                result._values = new NativeArray<double>(source.KeyValues.Length, allocator, NativeArrayOptions.UninitializedMemory);
                result._segments = new NativeArray<Segment>(source.SegmentCount, allocator, NativeArrayOptions.UninitializedMemory);
                result._coefficients = new NativeArray<double>(coefficientCount, allocator, NativeArrayOptions.UninitializedMemory);
                result._preWrapMode = source.PreWrapMode;
                result._postWrapMode = source.PostWrapMode;
                result._indexPrediction = source.IndexPrediction;
                result._lookupStrategy = source.BurstLookup;
                result._lookupScale = source.LookupScale;
                result._lookupTable = new NativeArray<int>(source.BurstLookup == CurveLookupStrategy.LUT ? source.LookupTable.Length : 0,
                    allocator, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < result._lookupTable.Length; i++) result._lookupTable[i] = source.LookupTable[i];
                for (int i = 0; i < source.KeyTimes.Length; i++)
                {
                    result._times[i] = source.KeyTimes[i];
                    result._values[i] = source.KeyValues[i];
                }
                int offset = 0;
                for (int i = 0; i < source.SegmentCount; i++)
                {
                    RationalCurveSegment segment = source.Segments[i];
                    if (segment == null)
                    {
                        result._segments[i] = new Segment { Kind = SegmentKind.Constant };
                        continue;
                    }
                    int count = segment.Degree + 1;
                    result._segments[i] = new Segment
                    {
                        Kind = SegmentKind.Rational,
                        Degree = segment.Degree,
                        CoefficientOffset = offset,
                        InverseLength = 1 / (segment.End - segment.Start),
                        StartSlope = segment.EvaluateSlope(segment.Start),
                        EndSlope = segment.EvaluateSlope(segment.End)
                    };
                    double binomial = 1;
                    for (int j = 0; j < count; j++)
                    {
                        result._coefficients[offset + j] = binomial * segment.NumeratorCoefficients[j];
                        result._coefficients[offset + count + j] = binomial * segment.DenominatorCoefficients[j];
                        binomial = binomial * (segment.Degree - j) / (j + 1);
                    }
                    offset += 2 * count;
                }
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        // Invalid time or an uncreated owner returns NaN instead of throwing inside Burst.
        public double Evaluate(double time) => Evaluate(time, -1, out _);

        public double Evaluate(double time, ref int intervalHint) => Evaluate(time, intervalHint, out intervalHint);

        private double Evaluate(double time, int intervalHint, out int interval)
        {
            interval = -1;
            if (!IsCreated || !IsFinite(time)) return double.NaN;
            if (_times.Length == 0) return 0;
            if (_times.Length == 1) return _values[0];
            double mapped = MapTime(time, out _);
            if (!IsFinite(mapped)) return double.NaN;
            int key = FindKey(mapped, intervalHint, out interval);
            if (key >= 0) return _values[key];
            Segment segment = _segments[interval];
            if (segment.Kind == SegmentKind.Constant) return _values[interval];
            double u = Normalize(mapped, interval, segment);
            bool reverse = u > 0.5;
            double t = reverse ? (1 - u) / u : u / (1 - u);
            int index = segment.CoefficientOffset + (reverse ? 0 : segment.Degree);
            int step = reverse ? 1 : -1;
            int qOffset = segment.Degree + 1;
            double p = _coefficients[index], q = _coefficients[index + qOffset];
            for (int i = 0; i < segment.Degree; i++)
            {
                index += step;
                p = p * t + _coefficients[index];
                q = q * t + _coefficients[index + qOffset];
            }
            return p / q;
        }

        // Matches BakedRationalCurve: NaN at non-differentiable joins, reversed slope when mirrored.
        public double EvaluateSlope(double time)
        {
            if (!IsCreated || !IsFinite(time)) return double.NaN;
            if (_times.Length < 2) return 0;
            double mapped = MapTime(time, out int direction);
            if (!IsFinite(mapped)) return double.NaN;
            if (direction == 0) return 0;
            int key = FindKey(mapped);
            if (key < 0) return direction * SegmentSlope(~key - 1, mapped);
            if (key > 0 && key < _times.Length - 1)
                return direction * JoinDerivative(_values[key], Limit(key - 1, true), Limit(key, false));

            bool outside = time < _times[0] || time > _times[_times.Length - 1] ||
                (time == _times[_times.Length - 1] && IsLoop(_postWrapMode));
            CurveWrapMode mode = time < _times[0] ? _preWrapMode : _postWrapMode;
            if (!outside) mode = key == 0 ? _preWrapMode : _postWrapMode;
            var first = Limit(0, false);
            var last = Limit(_segments.Length - 1, true);
            if (outside && IsLoop(mode)) return JoinDerivative(_values[key], last, first);
            if (key == 0)
            {
                var left = IsLoop(mode) ? last : mode == CurveWrapMode.PingPong
                    ? (first.value, -first.slope) : (_values[0], 0.0);
                return JoinDerivative(_values[0], left, first);
            }
            var right = IsLoop(mode) ? first : mode == CurveWrapMode.PingPong
                ? (last.value, -last.slope) : (_values[key], 0.0);
            return JoinDerivative(_values[key], last, right);
        }

        public void Dispose()
        {
            if (_times.IsCreated) _times.Dispose();
            if (_values.IsCreated) _values.Dispose();
            if (_segments.IsCreated) _segments.Dispose();
            if (_coefficients.IsCreated) _coefficients.Dispose();
            if (_lookupTable.IsCreated) _lookupTable.Dispose();
            this = default;
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
            int low = 0;
            int high = _times.Length - 1;
            if (_lookupStrategy == CurveLookupStrategy.BruteForce)
            {
                int interval = 0;
                while (interval + 1 < _segments.Length && time >= _times[interval + 1]) interval++;
                if (TryInterval(time, interval, out key, out resolvedInterval)) return key;
            }
            else if (_lookupStrategy == CurveLookupStrategy.LUT)
            {
                int cell = (int)Math.Max(0, Math.Min(_lookupTable.Length - 2, (time - _times[0]) * _lookupScale));
                int first = _lookupTable[cell];
                int last = Math.Min(_segments.Length, _lookupTable[cell + 1] + 1);
                if (TryInterval(time, first, out key, out resolvedInterval)) return key;
                if (time >= _times[first] && time <= _times[last]) { low = first; high = last; }
            }
            else if (_lookupStrategy == CurveLookupStrategy.Magic)
            {
                int interval = (int)Math.Max(0, Math.Min(_segments.Length - 1,
                    _indexPrediction.Evaluate(time - _times[0])));
                if (time < _times[interval] && interval > 0) interval--;
                else if (time >= _times[interval + 1] && interval + 1 < _segments.Length) interval++;
                if (TryInterval(time, interval, out key, out resolvedInterval)) return key;
                // Unreachable while the certificate holds (see BakedRationalCurve). The managed path throws here;
                // Burst cannot be relied on to throw in release players, so this one falls through to binary search.
            }
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                double value = _times[middle];
                if (value == time) { resolvedInterval = Math.Min(middle, _segments.Length - 1); return middle; }
                if (value < time) low = middle + 1;
                else high = middle - 1;
            }
            resolvedInterval = low - 1;
            return ~low;
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

        private double SegmentSlope(int interval, double time)
        {
            Segment segment = _segments[interval];
            if (segment.Kind == SegmentKind.Constant) return 0;
            if (time == _times[interval]) return segment.StartSlope;
            if (time == _times[interval + 1]) return segment.EndSlope;
            double u = Normalize(time, interval, segment);
            bool reverse = u > 0.5;
            double denominator = reverse ? u : 1 - u;
            double t = (reverse ? 1 - u : u) / denominator;
            int index = segment.CoefficientOffset + (reverse ? 0 : segment.Degree);
            int step = reverse ? 1 : -1;
            int qOffset = segment.Degree + 1;
            double p = _coefficients[index], q = _coefficients[index + qOffset];
            double dp = 0, dq = 0;
            for (int i = 0; i < segment.Degree; i++)
            {
                index += step;
                dp = dp * t + p;
                dq = dq * t + q;
                p = p * t + _coefficients[index];
                q = q * t + _coefficients[index + qOffset];
            }
            return (dp - (p / q) * dq) / q * (reverse ? -1 : 1) / (denominator * denominator) * segment.InverseLength;
        }

        private double Normalize(double time, int interval, Segment segment)
        {
            return Math.Max(0, Math.Min(1, (time - _times[interval]) * segment.InverseLength));
        }

        private double MapTime(double time, out int direction)
        {
            double first = _times[0];
            double last = _times[_times.Length - 1];
            direction = 1;
            if (time >= first && (time < last || (time == last && !IsLoop(_postWrapMode)))) return time;
            CurveWrapMode mode = time < first ? _preWrapMode : _postWrapMode;
            if (!IsLoop(mode) && mode != CurveWrapMode.PingPong)
            {
                direction = 0;
                return time < first ? first : last;
            }
            double length = last - first;
            double period = mode == CurveWrapMode.PingPong ? 2 * length : length;
            double phase = (time % period - first % period) % period;
            if (!IsFinite(phase)) return double.NaN;
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
            return _segments[interval].Kind == SegmentKind.Constant ? (_values[interval], 0) :
                (_values[key], SegmentSlope(interval, _times[key]));
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

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}

using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace RCurve
{
    // Unity-facing source and baked storage. Fitting happens only when editor tooling or Bake() requests it.
    [Serializable]
    public sealed class RationalCurve : ISerializationCallbackReceiver
    {
        [Serializable]
        private sealed class SegmentData
        {
            public bool Step;
            public double StartSlope;
            public double EndSlope;
            public double[] Denominator;
            public double[] InteriorNumerator;
        }

        [Serializable]
        private sealed class BakedData
        {
            public double[] Times;
            public double[] Values;
            public SegmentData[] Segments;
            public CurveWrapMode Pre;
            public CurveWrapMode Post;
            public CurveIndexPrediction IndexPrediction;
            public bool HasLookupPlan;
            public CurveLookupMode LookupMode;
            public CurveLookupStrategy ManagedLookup;
            public CurveLookupStrategy BurstLookup;
            public int[] LookupTable;
            public double LookupScale;
            public string LookupFallbackReason;
            // the layout of this data (FormatVersion; 0: written before the field existed, the same layout as 1)
            // and the RCurve version that baked it
            public int Format;
            public string Builder;
        }

        [SerializeField] private AnimationCurve _source = AnimationCurve.Linear(0, 0, 1, 1);
        [SerializeField] private CurveFitSettings _settings = new CurveFitSettings();
        // Normal Inspector draws only source/settings. Debug Inspector exposes these serialized fields.
        [SerializeField] private bool _hasBake;
        [SerializeField] private BakedData _baked;
        [SerializeField] private string _builtInput;
        [SerializeField] private string _attemptedInput;
        [SerializeField] private string _buildError;
        [NonSerialized] private BakedRationalCurve _cached;

        // A copy of the source curve: the source changes only through serialization (the Inspector, the asset's
        // file), so a baked curve cannot be edited from code into a curve its bake no longer matches.
        // The baked data's layout. A newer RCurve that changes it raises this, and an older one refuses data it
        // cannot read instead of misreading it.
        internal const int FormatVersion = 1;
        // The RCurve version that bakes, recorded with each bake.
        internal const string BuilderVersion = "0.6.0";

        public AnimationCurve CopySource() => Copy(_source);
        public CurveFitSettings Settings => _settings;
        public bool HasBake => _hasBake;
        public string BuildError => _buildError;
        internal string InputHash
        {
            get
            {
                // Hash semantic IEEE values, not Unity's version-dependent AnimationCurve JSON layout.
                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);
                writer.Write(1); // Signature format version.
                writer.Write((int)_source.preWrapMode); writer.Write((int)_source.postWrapMode);
                Keyframe[] keys = _source.keys;
                writer.Write(keys.Length);
                foreach (Keyframe key in keys)
                {
                    writer.Write(key.time); writer.Write(key.value);
                    writer.Write(key.inTangent); writer.Write(key.outTangent);
                    writer.Write(key.inWeight); writer.Write(key.outWeight); writer.Write((int)key.weightedMode);
                }
                writer.Write((int)_settings.Objective); writer.Write((int)_settings.Quality);
                writer.Write(_settings.MaxValueError); writer.Write(_settings.MaxSlopeError);
                writer.Write(_settings.MaxSubdivisionDepth); writer.Write((int)_settings.LookupMode);
                writer.Flush();
                using var hash = SHA256.Create();
                return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "");
            }
        }
        internal bool NeedsBuild => InputHash != _attemptedInput;
        public bool IsStale => !_hasBake || InputHash != _builtInput;

        public BakedRationalCurve BakedCurve
        {
            get
            {
                if (!_hasBake) throw new InvalidOperationException("The curve has not been baked.");
                if (_baked.Format > FormatVersion)
                    throw new InvalidOperationException($"The curve was baked in format {_baked.Format} by RCurve {_baked.Builder}; " +
                                                        $"this RCurve reads format {FormatVersion} or older. Update RCurve or rebake the curve.");
                if (_cached != null) return _cached;
                // Checked once per load, before the first evaluation: a source or settings changed since the bake
                // (by code without Undo, a text edit, a merge) is not evaluated with the old bake.
                string inputHash = InputHash;
                if (inputHash != _builtInput)
                    throw new InvalidOperationException(inputHash == _attemptedInput && !string.IsNullOrEmpty(_buildError)
                        ? "The curve's latest bake failed: " + _buildError
                        : "The curve's source or settings changed since it was baked.");
                var segments = new RationalCurveSegment[_baked.Segments.Length];
                for (int i = 0; i < segments.Length; i++)
                {
                    SegmentData data = _baked.Segments[i];
                    if (!data.Step)
                        segments[i] = new RationalCurveSegment(_baked.Times[i], _baked.Times[i + 1],
                            _baked.Values[i], _baked.Values[i + 1], data.StartSlope, data.EndSlope,
                            data.Denominator, data.InteriorNumerator);
                }
                _cached = new BakedRationalCurve(_baked.Times, _baked.Values, segments, _baked.Pre, _baked.Post, _baked.IndexPrediction,
                    _baked.LookupMode, _baked.HasLookupPlan ? _baked.ManagedLookup : (CurveLookupStrategy?)null,
                    _baked.HasLookupPlan ? _baked.BurstLookup : (CurveLookupStrategy?)null, _baked.LookupTable, _baked.LookupScale);
                return _cached;
            }
        }

        public double Evaluate(double time) => BakedCurve.Evaluate(time);
        public double Evaluate(double time, ref int intervalHint) => BakedCurve.Evaluate(time, ref intervalHint);
        public double EvaluateSlope(double time) => BakedCurve.EvaluateSlope(time);

        // The baked curve's first and last keys (see BakedRationalCurve); checked like an evaluation.
        public double StartTime => BakedCurve.StartTime;
        public double EndTime => BakedCurve.EndTime;
        public double StartValue => BakedCurve.StartValue;
        public double EndValue => BakedCurve.EndValue;

        // Runtime fitting is intentionally explicit because it allocates and can be expensive.
        public void Bake()
        {
            string hash = InputHash;
            StoreBuild(RationalCurveBuilder.Build(_source, _settings), hash);
        }

        public RationalCurve(AnimationCurve source = null, CurveFitSettings settings = null)
        {
            _source = source != null ? Copy(source) : AnimationCurve.Linear(0, 0, 1, 1);
            _settings = settings ?? new CurveFitSettings();
        }

        // Called by editor tooling; publish only complete results for the still-current input.
        internal bool StoreBuild(BakedRationalCurve curve, string inputHash)
        {
            if (curve == null) throw new ArgumentNullException(nameof(curve));
            if (inputHash != InputHash) return false;
            var data = new BakedData
            {
                Format = FormatVersion, Builder = BuilderVersion,
                Times = curve.KeyTimes.ToArray(), Values = curve.KeyValues.ToArray(),
                Segments = new SegmentData[curve.SegmentCount], Pre = curve.PreWrapMode, Post = curve.PostWrapMode,
                IndexPrediction = curve.IndexPrediction, HasLookupPlan = true,
                LookupMode = curve.LookupMode, ManagedLookup = curve.ManagedLookup, BurstLookup = curve.BurstLookup,
                LookupTable = curve.LookupTable.ToArray(), LookupScale = curve.LookupScale,
                LookupFallbackReason = curve.LookupMode == CurveLookupMode.Magic && !curve.IndexPrediction.IsEnabled
                    ? "Magic could not certify the index error; using binary search."
                    : curve.LookupMode == CurveLookupMode.LUT && curve.LookupTable.Length == 0
                        ? "LUT unavailable for this domain; using binary search." : null
            };
            for (int i = 0; i < data.Segments.Length; i++)
            {
                RationalCurveSegment segment = curve.Segments[i];
                data.Segments[i] = segment == null ? new SegmentData { Step = true } : new SegmentData
                {
                    StartSlope = segment.EvaluateSlope(segment.Start),
                    EndSlope = segment.EvaluateSlope(segment.End),
                    Denominator = segment.DenominatorCoefficients.ToArray(),
                    InteriorNumerator = segment.NumeratorCoefficients.Slice(2, segment.Degree - 3).ToArray()
                };
            }
            _baked = data;
            _cached = curve;
            _hasBake = true;
            _builtInput = _attemptedInput = inputHash;
            _buildError = null;
            return true;
        }

        internal void StoreBuildFailure(string inputHash, string message)
        {
            if (inputHash != InputHash) return;
            _attemptedInput = inputHash;
            _buildError = message;
        }

        private static AnimationCurve Copy(AnimationCurve curve) =>
            new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };

        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() { _cached = null; }
    }
}

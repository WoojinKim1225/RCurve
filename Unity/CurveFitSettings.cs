using System;
using UnityEngine;

namespace RCurve
{
    public enum CurveFitObjective { LeastSquares, Minimax }
    public enum CurveFitQuality { Low = 3, Medium = 4, High = 5, XHigh = 6 }

    [Serializable]
    public sealed class CurveFitSettings
    {
        [SerializeField] private CurveFitObjective _objective = CurveFitObjective.Minimax;
        [SerializeField] private CurveFitQuality _quality = CurveFitQuality.High;
        [SerializeField] private double _maxValueError = 0.001;
        [SerializeField] private double _maxSlopeError = 0.01;
        [SerializeField] private int _maxSubdivisionDepth = 8;
        [SerializeField] private CurveLookupMode _lookupMode = CurveLookupMode.Auto;

        public CurveFitObjective Objective => _objective;
        public CurveLookupMode LookupMode => _lookupMode;
        public CurveFitQuality Quality => _quality;
        public int Degree => _quality switch
        {
            CurveFitQuality.Low => 3, CurveFitQuality.Medium => 4,
            CurveFitQuality.High => 5, CurveFitQuality.XHigh => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(Quality))
        };
        public double MaxValueError => _maxValueError;
        public double MaxSlopeError => _maxSlopeError;
        public int MaxSubdivisionDepth => _maxSubdivisionDepth;

        public CurveFitSettings(CurveFitObjective objective = CurveFitObjective.Minimax,
            CurveFitQuality quality = CurveFitQuality.High, double maxValueError = 0.001,
            double maxSlopeError = 0.01, int maxSubdivisionDepth = 8, CurveLookupMode lookupMode = CurveLookupMode.Auto)
        {
            _lookupMode = lookupMode;
            _objective = objective;
            _quality = quality;
            _maxValueError = maxValueError;
            _maxSlopeError = maxSlopeError;
            _maxSubdivisionDepth = maxSubdivisionDepth;
            Validate();
        }

        public void Validate()
        {
            if (_lookupMode < CurveLookupMode.Auto || _lookupMode > CurveLookupMode.Magic)
                throw new ArgumentOutOfRangeException(nameof(LookupMode));
            if (_objective != CurveFitObjective.LeastSquares && _objective != CurveFitObjective.Minimax)
                throw new ArgumentOutOfRangeException(nameof(Objective));
            _ = Degree;
            if (!(_maxValueError > 0) || double.IsInfinity(_maxValueError))
                throw new ArgumentOutOfRangeException(nameof(MaxValueError));
            if (!(_maxSlopeError > 0) || double.IsInfinity(_maxSlopeError))
                throw new ArgumentOutOfRangeException(nameof(MaxSlopeError));
            if (_maxSubdivisionDepth < 0 || _maxSubdivisionDepth > 12)
                throw new ArgumentOutOfRangeException(nameof(MaxSubdivisionDepth));
        }
    }
}

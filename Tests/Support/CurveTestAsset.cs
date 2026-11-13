using UnityEngine;

namespace RCurve.Tests
{
    // Test-only asset holding a curve, for reimport tests.
    public sealed class CurveTestAsset : ScriptableObject
    {
        public RationalCurve Curve = new RationalCurve();
    }
}

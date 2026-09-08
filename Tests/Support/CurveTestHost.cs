using UnityEngine;

namespace RCurve.Tests
{
    // Test-only runtime component used by serialization, prefab and Inspector tests.
    public sealed class CurveTestHost : MonoBehaviour
    {
        public RationalCurve Curve = new RationalCurve();
    }
}

using RCurve;
using UnityEngine;

// Curves baked in the editor and loaded by the player check.
public sealed class PlayerCheckCurves : ScriptableObject
{
    public RationalCurve[] Curves = new RationalCurve[0];
}

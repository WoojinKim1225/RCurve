namespace RCurve
{
    // User choice. Auto is deliberately zero for existing serialized settings.
    public enum CurveLookupMode { Auto, BruteForce, LUT, Magic }

    // Actual baked strategy. BinarySearch is a fallback/Auto candidate, not a fitting mode.
    public enum CurveLookupStrategy { BinarySearch, BruteForce, LUT, Magic }
}

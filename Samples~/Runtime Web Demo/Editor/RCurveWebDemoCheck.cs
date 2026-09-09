using System;
using System.Reflection;
using RCurve;
using UnityEditor;
using UnityEngine;

public static class RCurveWebDemoCheck
{
    [MenuItem("Tools/RCurve/Check Web Demo")]
    public static void Run()
    {
        var host = new GameObject("Web Demo Check");
        host.SetActive(false);
        try
        {
            var demo = host.AddComponent<RCurveWebDemo>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object Read(string name) => typeof(RCurveWebDemo).GetField(name, flags).GetValue(demo);
            void Bake() => typeof(RCurveWebDemo).GetMethod("Rebuild", flags).Invoke(demo, null);
            void Check(bool condition, string message)
            {
                if (!condition) throw new Exception(message);
            }

            Bake();
            var source = (float[])Read("_sourceSamples");
            var baked = (float[])Read("_bakedSamples");
            Check(Read("_error") == null && baked != null, "Initial bake failed.");
            Check(baked[0] == 0 && baked[baked.Length - 1] == 1, "Endpoints must clamp, not wrap.");
            Check((double)Read("_maxError") <= 0.002, "Sampled error exceeds demo tolerance.");

            var previousCurve = Read("_curve");
            var values = (float[])Read("_values");
            values[1] = float.NaN;
            Bake();
            Check(Read("_error") != null, "Invalid input must report a failure.");
            Check(ReferenceEquals(source, Read("_sourceSamples")) && ReferenceEquals(baked, Read("_bakedSamples")),
                "A failed bake must preserve both graphs.");
            Check(ReferenceEquals(previousCurve, Read("_curve")), "A failed bake must preserve the moving point's curve.");

            values[1] = 1.3f;
            Bake();
            Check(Read("_error") == null && !ReferenceEquals(baked, Read("_bakedSamples")), "Valid input must recover.");
            var inSlopes = (float[])Read("_inSlopes");
            var outSlopes = (float[])Read("_outSlopes");
            var inWeights = (float[])Read("_inWeights");
            var outWeights = (float[])Read("_outWeights");
            for (int count = 2; count <= 5; count++)
            {
                typeof(RCurveWebDemo).GetField("_keyCount", flags).SetValue(demo, count);
                for (int i = 0; i < count; i++)
                {
                    inSlopes[i] = -0.3f;
                    outSlopes[i] = 0.6f;
                    inWeights[i] = 0.25f;
                    outWeights[i] = 0.4f;
                }
                Bake();
                Check(Read("_error") == null, $"Weighted {count}-key bake failed: {Read("_error")}");
                var curve = (BakedRationalCurve)Read("_curve");
                Check(curve.Evaluate(0) == values[0] && curve.Evaluate(1) == values[count - 1], "Wrong active endpoints.");
                // Whole-curve slopes are two-sided and undefined at nonzero clamped endpoint tangents.
                Check(Math.Abs(curve.Segments[0].EvaluateSlope(0) - outSlopes[0]) < 1e-6, "Outgoing slope not preserved.");
                Check(Math.Abs(curve.Segments[curve.SegmentCount - 1].EvaluateSlope(1) - inSlopes[count - 1]) < 1e-6, "Incoming slope not preserved.");
                Check((double)Read("_maxError") <= 0.002, "Weighted sampled error exceeds tolerance.");
                var keys = new Keyframe[count];
                for (int i = 0; i < count; i++)
                    keys[i] = new Keyframe(i / (count - 1f), values[i], inSlopes[i], outSlopes[i], inWeights[i], outWeights[i])
                    { weightedMode = WeightedMode.Both };
                var expected = new AnimationCurve(keys);
                for (int i = 0; i <= 100; i++)
                {
                    float t = i / 100f;
                    Check(Math.Abs(curve.Evaluate(t) - expected.Evaluate(t)) < 0.002, "Independent weights/slopes not applied.");
                }
                previousCurve = curve;
            }
            inWeights[1] = 0;
            Bake();
            Check(Read("_error") != null && ReferenceEquals(previousCurve, Read("_curve")), "Invalid weight must preserve the last curve.");
            Debug.Log("RCurve Web Demo check passed: 2–5 keys, independent slopes/weights, samples and failure preservation.");
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }
}

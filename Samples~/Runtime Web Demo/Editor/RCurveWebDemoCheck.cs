using System;
using System.Reflection;
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

            var values = (float[])Read("_values");
            values[1] = float.NaN;
            Bake();
            Check(Read("_error") != null, "Invalid input must report a failure.");
            Check(ReferenceEquals(source, Read("_sourceSamples")) && ReferenceEquals(baked, Read("_bakedSamples")),
                "A failed bake must preserve both graphs.");

            values[1] = 1.3f;
            Bake();
            Check(Read("_error") == null && !ReferenceEquals(baked, Read("_bakedSamples")), "Valid input must recover.");
            Debug.Log("RCurve Web Demo check passed.");
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }
}

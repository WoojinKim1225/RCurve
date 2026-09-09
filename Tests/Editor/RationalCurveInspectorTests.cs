using System;
using System.Reflection;
using NUnit.Framework;
using RCurve;
using RCurve.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class RationalCurveInspectorTests
{
    [Test]
    public void NormalInspectorIsOneLineOrSettingsOnlyRegardlessOfBakeExpansion()
    {
        Component host = CreateHost(out GameObject gameObject);
        try
        {
            using var serialized = new SerializedObject(host);
            SerializedProperty property = serialized.FindProperty("Curve");
            var drawer = new RationalCurveDrawer();
            var label = new GUIContent("Curve Name");
            property.isExpanded = false;
            Assert.That(drawer.GetPropertyHeight(property, label), Is.EqualTo(EditorGUIUtility.singleLineHeight));
            property.isExpanded = true;
            float expected = EditorGUIUtility.singleLineHeight;
            foreach (string name in new[] { "_objective", "_quality", "_maxValueError", "_maxSlopeError", "_maxSubdivisionDepth", "_lookupMode" })
                expected += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative("_settings." + name), true);
            Assert.That(drawer.GetPropertyHeight(property, label), Is.EqualTo(expected));

            var curve = (RationalCurve)property.boxedValue;
            Assert.That(curve.StoreBuild(RationalCurveBuilder.Build(curve.Source, curve.Settings), curve.InputHash), Is.True);
            property.boxedValue = curve;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            serialized.Update();
            property = serialized.FindProperty("Curve");
            property.isExpanded = true;
            property.FindPropertyRelative("_baked").isExpanded = true;
            property.FindPropertyRelative("_baked.Segments").isExpanded = true;
            property.FindPropertyRelative("_baked.Times").isExpanded = true;
            Assert.That(drawer.GetPropertyHeight(property, label), Is.EqualTo(expected), "Normal layout must not include baked fields.");
            property.isExpanded = false;
            Assert.That(drawer.GetPropertyHeight(property, label), Is.EqualTo(EditorGUIUtility.singleLineHeight));
        }
        finally { Object.DestroyImmediate(gameObject); }
    }

    [Test]
    public void UnityDebugInspectorBypassesTheDrawerAndExposesBakedData()
    {
        Component host = CreateHost(out GameObject gameObject);
        try
        {
            using var serialized = new SerializedObject(host);
            SerializedProperty property = serialized.FindProperty("Curve");
            var curve = (RationalCurve)property.boxedValue;
            curve.StoreBuild(RationalCurveBuilder.Build(curve.Source), curve.InputHash);
            property.boxedValue = curve;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Test-only access to the same mode set by Inspector's Normal/Debug menu.
            // Production uses Unity's built-in Debug renderer, not reflection or a second Debug toggle.
            PropertyInfo mode = typeof(SerializedObject).GetProperty("inspectorMode", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(mode, Is.Not.Null);
            mode.SetValue(serialized, Enum.ToObject(mode.PropertyType, 1));
            serialized.Update();
            property = serialized.FindProperty("Curve");
            property.isExpanded = true;
            property.FindPropertyRelative("_baked").isExpanded = true;
            Assert.That(property.FindPropertyRelative("_hasBake").boolValue, Is.True);
            Assert.That(property.FindPropertyRelative("_baked.Times").arraySize, Is.EqualTo(curve.BakedCurve.KeyTimes.Length));
            Assert.That(property.FindPropertyRelative("_baked.Segments").arraySize, Is.EqualTo(curve.BakedCurve.SegmentCount));
            float normalHeight = new RationalCurveDrawer().GetPropertyHeight(property, GUIContent.none);
            Assert.That(EditorGUI.GetPropertyHeight(property, GUIContent.none, true), Is.GreaterThan(normalHeight));
        }
        finally { Object.DestroyImmediate(gameObject); }
    }

    [Test]
    public void UnitySerializationIsSeparateFromBakedRuntimeData()
    {
        Assert.That(typeof(RationalCurve).Assembly.GetName().Name, Is.EqualTo("RCurve.Unity"));
        Assert.That(typeof(BakedRationalCurve).Assembly.GetName().Name, Is.EqualTo("RCurve.Runtime"));
        foreach (AssemblyName reference in typeof(BakedRationalCurve).Assembly.GetReferencedAssemblies())
            Assert.That(reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal), Is.False);
        var curve = new RationalCurve();
        curve.StoreBuild(RationalCurveBuilder.Build(curve.Source), curve.InputHash);
        var restored = JsonUtility.FromJson<RationalCurve>(JsonUtility.ToJson(curve));
        Assert.That(restored.IsStale, Is.False);
        Assert.That(restored.BakedCurve, Is.TypeOf<BakedRationalCurve>());
        Assert.That(restored.Evaluate(0.5), Is.EqualTo(curve.Evaluate(0.5)).Within(1e-12));
    }

    private static Component CreateHost(out GameObject gameObject)
    {
        Type type = typeof(RCurve.Tests.CurveTestHost);
        Assert.That(type, Is.Not.Null);
        gameObject = new GameObject("RCurve inspector test");
        return gameObject.AddComponent(type);
    }
}

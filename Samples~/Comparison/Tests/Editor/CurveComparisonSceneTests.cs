using System;
using NUnit.Framework;
using RCurve;
using RCurve.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class CurveComparisonSceneTests
{
    [Test]
    public void CanvasDashboardHasSerializedControlsAndInputWithoutPlayMode()
    {
        var scene = EditorSceneManager.OpenPreviewScene(RCurve.Demo.Editor.CurveComparisonSceneBuilder.ScenePath);
        try
        {
            CurveComparisonDemo demo = null;
            EventSystem events = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.TryGetComponent<CurveComparisonDemo>(out var found)) demo = found;
                if (root.TryGetComponent<EventSystem>(out var system)) events = system;
            }
            Assert.That(demo, Is.Not.Null); Assert.That(events, Is.Not.Null);
            Assert.That(events.GetComponent<BaseInputModule>(), Is.Not.Null);
            using var data = new SerializedObject(demo);
            foreach (string name in new[] { "_canvas", "_dashboard", "_laneLabels", "_unityLabel", "_rationalLabel", "_errorLabel",
                "_maximumLabel", "_timeLabel", "_strategyLabel", "_statusLabel", "_playButton", "_reverseButton", "_resetButton", "_timeline", "_hintToggle", "_errorFill" })
                Assert.That(data.FindProperty(name).objectReferenceValue, Is.Not.Null, name);
            var canvas = (Canvas)data.FindProperty("_canvas").objectReferenceValue;
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(canvas.GetComponent<CanvasScaler>().uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
            Assert.That(canvas.GetComponent<GraphicRaycaster>(), Is.Not.Null);
            var slider = (Slider)data.FindProperty("_timeline").objectReferenceValue;
            Assert.That(slider.minValue, Is.EqualTo(0)); Assert.That(slider.maxValue, Is.EqualTo(1));
            Assert.That(slider.targetGraphic.raycastTarget, Is.True);
            foreach (var text in canvas.GetComponentsInChildren<Text>()) Assert.That(text.font, Is.Not.Null);
            Assert.That(Application.isPlaying, Is.False);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void ComparisonSceneHasMatchingSavedBakeAndAssignedMarkers()
    {
        string path = RCurve.Demo.Editor.CurveComparisonSceneBuilder.ScenePath;
        Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), Is.Not.Null);
        var scene = EditorSceneManager.OpenPreviewScene(path);
        try
        {
            CurveComparisonDemo demo = null;
            Camera camera = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.TryGetComponent<CurveComparisonDemo>(out var found)) demo = found;
                if (root.TryGetComponent<Camera>(out var foundCamera)) camera = foundCamera;
            }
            Assert.That(demo, Is.Not.Null); Assert.That(camera, Is.Not.Null);
            Assert.That(demo.UnityMarker, Is.Not.Null); Assert.That(demo.RationalMarker, Is.Not.Null);
            Assert.That(demo.UnityMarker.position.x, Is.LessThan(demo.RationalMarker.position.x));
            Assert.That(demo.UnityMarker.GetComponent<Renderer>().sharedMaterial, Is.Not.Null);
            Assert.That(demo.RationalMarker.GetComponent<Renderer>().sharedMaterial, Is.Not.Null);
            Assert.That(demo.Curve.HasBake, Is.True); Assert.That(demo.Curve.IsStale, Is.False);
            Assert.That(demo.Curve.Settings.LookupMode, Is.EqualTo(CurveLookupMode.Auto));
            Assert.That(demo.Curve.BakedCurve.LookupMode, Is.EqualTo(CurveLookupMode.Auto));
            Assert.That(demo.Curve.Settings.Objective, Is.EqualTo(CurveFitObjective.Minimax));
            var baked = demo.Curve.BakedCurve;
            double[] times = baked.KeyTimes.ToArray();
            foreach (Keyframe key in demo.Curve.CopySource().keys)
            {
                int index = Array.BinarySearch(times, (double)key.time);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), "Authored keys remain segment boundaries.");
                Assert.That(baked.KeyValues[index], Is.EqualTo((double)key.value));
                if (index > 0)
                    Assert.That(baked.Segments[index - 1].EvaluateSlope(key.time), Is.EqualTo((double)key.inTangent).Within(1e-9));
                if (index < baked.SegmentCount)
                    Assert.That(baked.Segments[index].EvaluateSlope(key.time), Is.EqualTo((double)key.outTangent).Within(1e-9));
            }
            double maximum = 0;
            for (int i = 0; i <= 4096; i++)
            {
                float time = i / 4096f;
                maximum = Math.Max(maximum, Math.Abs(demo.Curve.CopySource().Evaluate(time) - demo.Curve.Evaluate(time)));
            }
            Assert.That(maximum, Is.LessThanOrEqualTo(0.0011));
            Assert.That(Application.isPlaying, Is.False, "This validates scene data, not Play Mode or rendered UX.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}

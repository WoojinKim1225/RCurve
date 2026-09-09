using System;
using System.Collections;
using NUnit.Framework;
using RCurve;
using RCurve.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class RationalCurveBuildTests
{
    [Test]
    public void InputSignatureUsesStableSemanticBytesAcrossEditorVersions()
    {
        var source = new AnimationCurve(
            new Keyframe(0, 1, 2, 3, 0.25f, 0.5f) { weightedMode = WeightedMode.Both },
            new Keyframe(1, 4, 5, 6, 0.75f, 0.25f) { weightedMode = WeightedMode.Both })
            { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.PingPong };
        Assert.That(new RationalCurve(source).InputHash,
            Is.EqualTo("69BA22779FD780F3FCB1FEE4C5B3B3EE8900C2E8BC6F6FB258CB20B766BC8090"));
    }

    [Test]
    public void RuntimeBakeStoresCurrentSource()
    {
        var curve = new RationalCurve(AnimationCurve.EaseInOut(0, 0, 1, 1));
        curve.Bake();
        Assert.That(curve.HasBake, Is.True);
        Assert.That(curve.IsStale, Is.False);
        Assert.That(curve.Evaluate(0.5), Is.EqualTo(curve.Source.Evaluate(0.5f)).Within(0.0011));
    }

    [Test]
    public void BuildsWeightedCurvesWithBothObjectivesAndEveryDegree([Values] CurveFitObjective objective, [Values] CurveFitQuality quality)
    {
        AnimationCurve source = Weighted();
        var settings = new CurveFitSettings(objective, quality);
        BakedRationalCurve built = RationalCurveBuilder.Build(source, settings);
        for (int i = 0; i <= 1000; i++)
        {
            float time = i / 1000f;
            Assert.That(built.Evaluate(time), Is.EqualTo(source.Evaluate(time)).Within(0.0011));
        }
        var original = AnimationCurveBezierSegment.FromCurve(source)[0];
        for (int i = 1; i < 100; i++)
        {
            double t = i / 100.0;
            var point = original.Evaluate(t);
            var derivative = original.EvaluateDerivative(t);
            Assert.That(built.EvaluateDerivative(point.x), Is.EqualTo(derivative.y / derivative.x).Within(0.011));
        }
        foreach (RationalCurveSegment segment in built.Segments)
        {
            Assert.That(segment.Degree, Is.EqualTo(settings.Degree));
            foreach (double q in segment.DenominatorCoefficients) Assert.That(q, Is.GreaterThanOrEqualTo(1e-6));
        }
        Assert.That(built.Segments[0].EvaluateDerivative(0), Is.EqualTo(source[0].outTangent).Within(1e-7));
        Assert.That(built.Segments[built.SegmentCount - 1].EvaluateDerivative(1), Is.EqualTo(source[1].inTangent).Within(1e-7));
    }

    [Test]
    public void MinimaxReducesTheSampleMaximumRatherThanAlgebraicResidual()
    {
        AnimationCurve source = Weighted();
        BakedRationalCurve ls = RationalCurveBuilder.Build(source, new CurveFitSettings(CurveFitObjective.LeastSquares, CurveFitQuality.Low, 1, 100, 0));
        BakedRationalCurve mm = RationalCurveBuilder.Build(source, new CurveFitSettings(CurveFitObjective.Minimax, CurveFitQuality.Low, 1, 100, 0));
        double lsMax = 0, mmMax = 0;
        for (int i = 0; i <= 32; i++)
        {
            float time = i / 32f;
            lsMax = Math.Max(lsMax, Math.Abs(ls.Evaluate(time) - source.Evaluate(time)));
            mmMax = Math.Max(mmMax, Math.Abs(mm.Evaluate(time) - source.Evaluate(time)));
        }
        Assert.That(mmMax, Is.LessThanOrEqualTo(lsMax + 5e-6));
        Assert.That(Math.Abs(mm.Evaluate(0.37) - ls.Evaluate(0.37)), Is.GreaterThan(1e-7));
    }

    [Test]
    public void HandlesEmptySingleConstantAndNonNormalizedEqualHeightCurves()
    {
        Assert.That(RationalCurveBuilder.Build(new AnimationCurve()).Evaluate(100), Is.EqualTo(0));
        Assert.That(RationalCurveBuilder.Build(new AnimationCurve(new Keyframe(-2, 5))).Evaluate(-100), Is.EqualTo(5));
        var step = new AnimationCurve(new Keyframe(-2, 3, 0, float.PositiveInfinity), new Keyframe(5, -1, float.PositiveInfinity, 0));
        step.preWrapMode = WrapMode.PingPong;
        step.postWrapMode = WrapMode.Loop;
        var built = RationalCurveBuilder.Build(step);
        foreach (float time in new[] { -20f, -2, 4.999f, 5, 12 }) Assert.That(built.Evaluate(time), Is.EqualTo(step.Evaluate(time)));
        var equal = new AnimationCurve(new Keyframe(-2, -3, 0, 2), new Keyframe(5, -3, -1, 0));
        built = RationalCurveBuilder.Build(equal);
        for (int i = 0; i <= 100; i++)
        {
            float time = -2 + 7 * i / 100f;
            Assert.That(built.Evaluate(time), Is.EqualTo(equal.Evaluate(time)).Within(1e-5));
        }
    }

    [Test]
    public void SerializationRestoresCoefficientsWithoutRefittingAndKeepsLastGoodResult()
    {
        var field = new RationalCurve(Weighted());
        string hash = field.InputHash;
        Assert.That(field.StoreBuild(RationalCurveBuilder.Build(field.Source, field.Settings), hash), Is.True);
        double expected = field.Evaluate(0.37);
        var restored = JsonUtility.FromJson<RationalCurve>(JsonUtility.ToJson(field));
        Assert.That(restored.HasBake, Is.True);
        Assert.That(restored.IsStale, Is.False);
        restored.OnAfterDeserialize();
        Assert.That(restored.Evaluate(0.37), Is.EqualTo(expected).Within(1e-10));
        Keyframe key = restored.Source[0];
        key.outWeight = 0;
        restored.Source.MoveKey(0, key);
        Assert.That(restored.IsStale, Is.True);
        Assert.Throws<NotSupportedException>(() => RationalCurveBuilder.Build(restored.Source));
        restored.StoreBuildFailure(restored.InputHash, "stationary X");
        Assert.That(restored.NeedsBuild, Is.False);
        Assert.That(restored.BuildError, Is.Not.Empty);
        Assert.That(restored.Evaluate(0.37), Is.EqualTo(expected).Within(1e-10));
        Assert.That(restored.StoreBuild(field.BakedCurve, hash), Is.False, "Do not publish a result for an older edit.");
        Assert.Throws<InvalidOperationException>(() => RationalCurveBuilder.Build(Weighted(),
            new CurveFitSettings(maxValueError: 1e-14, maxSlopeError: 1e-14, maxSubdivisionDepth: 0)));
    }

    [UnityTest]
    public IEnumerator InspectorQueueDebouncesTheLatestInput()
    {
        Component host = CreateHost(out GameObject gameObject);
        try
        {
            SetField(host, new RationalCurve(AnimationCurve.Linear(0, 0, 1, 1)));
            CurveAutoBuild.Queue(host, "Curve");
            SetField(host, new RationalCurve(AnimationCurve.Linear(0, 0, 1, 4)));
            CurveAutoBuild.Queue(host, "Curve");
            double deadline = EditorApplication.timeSinceStartup + 10;
            while (!GetField(host).HasBake && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.That(GetField(host).HasBake, Is.True);
            Assert.That(GetField(host).Evaluate(0.5), Is.EqualTo(2).Within(1e-10));
            Assert.That(GetField(host).IsStale, Is.False);
        }
        finally { Object.DestroyImmediate(gameObject); }
    }

    [Test]
    public void UndoRedoAndPrefabSaveRestoreSourceAndBake()
    {
        Component host = CreateHost(out GameObject gameObject);
        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/RCurveSerializationTest.prefab");
        GameObject loaded = null;
        try
        {
            SetField(host, new RationalCurve(AnimationCurve.Linear(0, 0, 1, 2)));
            CurveAutoBuild.Queue(host, "Curve");
            CurveAutoBuild.FlushPending();
            Assert.That(GetField(host).Evaluate(0.5), Is.EqualTo(1).Within(1e-10));
            Undo.RegisterCompleteObjectUndo(host, "Edit RCurve source");
            SetField(host, new RationalCurve(AnimationCurve.Linear(0, 0, 1, 6)));
            Undo.FlushUndoRecordObjects();
            CurveAutoBuild.Queue(host, "Curve");
            CurveAutoBuild.FlushPending();
            Assert.That(GetField(host).Evaluate(0.5), Is.EqualTo(3).Within(1e-10));
            Undo.PerformUndo();
            CurveAutoBuild.FlushPending();
            Assert.That(GetField(host).Evaluate(0.5), Is.EqualTo(1).Within(1e-10));
            Undo.PerformRedo();
            CurveAutoBuild.FlushPending();
            Assert.That(GetField(host).Evaluate(0.5), Is.EqualTo(3).Within(1e-10));
            Assert.That(PrefabUtility.SaveAsPrefabAsset(gameObject, path), Is.Not.Null);
            loaded = PrefabUtility.LoadPrefabContents(path);
            var restored = GetField(loaded.GetComponent(host.GetType()));
            restored.OnAfterDeserialize();
            Assert.That(restored.HasBake, Is.True);
            Assert.That(restored.IsStale, Is.False);
            Assert.That(restored.Evaluate(0.5), Is.EqualTo(3).Within(1e-10));
        }
        finally
        {
            if (loaded != null) PrefabUtility.UnloadPrefabContents(loaded);
            Undo.ClearUndo(host);
            Object.DestroyImmediate(gameObject);
            AssetDatabase.DeleteAsset(path);
        }
    }

    [Test]
    public void SerializedSettingsWrapAndBuildFailureAreHandledByTheEditorQueue()
    {
        Component host = CreateHost(out GameObject gameObject);
        try
        {
            SetField(host, new RationalCurve(AnimationCurve.Linear(0, 0, 1, 4)));
            CurveAutoBuild.Queue(host, "Curve");
            CurveAutoBuild.FlushPending();
            string previous = GetField(host).InputHash;
            using (var serialized = new SerializedObject(host))
            {
                serialized.FindProperty("Curve._settings._quality").intValue = (int)CurveFitQuality.Low;
                SerializedProperty property = serialized.FindProperty("Curve._source");
                AnimationCurve source = property.animationCurveValue;
                source.postWrapMode = WrapMode.Loop;
                property.animationCurveValue = source;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.That(GetField(host).InputHash, Is.Not.EqualTo(previous));
            CurveAutoBuild.Queue(host, "Curve");
            CurveAutoBuild.FlushPending();
            Assert.That(GetField(host).BakedCurve.Segments[0].Degree, Is.EqualTo(3));
            Assert.That(GetField(host).Evaluate(1), Is.EqualTo(0));
            using (var serialized = new SerializedObject(host))
            {
                SerializedProperty property = serialized.FindProperty("Curve._source");
                AnimationCurve source = property.animationCurveValue;
                Keyframe key = source[0];
                key.weightedMode = WeightedMode.Out;
                key.outWeight = 0;
                source.MoveKey(0, key);
                property.animationCurveValue = source;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            CurveAutoBuild.Queue(host, "Curve");
            CurveAutoBuild.FlushPending();
            var failed = GetField(host);
            Assert.That(failed.HasBake, Is.True);
            Assert.That(failed.IsStale, Is.True);
            Assert.That(failed.NeedsBuild, Is.False);
            Assert.That(failed.BuildError, Is.Not.Empty);
            Assert.That(failed.Evaluate(0.5), Is.EqualTo(2).Within(1e-10));
        }
        finally { Object.DestroyImmediate(gameObject); }
    }

    [Test]
    public void SavingASceneFlushesPendingBakeAndReloadsItsCoefficients()
    {
        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/RCurveSerializationTest.unity");
        // Save a temporary copy: never rename, close or discard the active unsaved scene.
        Component host = CreateHost(out GameObject gameObject);
        gameObject.name = "RCurve serialization " + Guid.NewGuid().ToString("N");
        Scene scene = gameObject.scene;
        string originalPath = scene.path;
        Scene loaded = default;
        try
        {
            SetField(host, new RationalCurve(AnimationCurve.Linear(0, 0, 1, 4)));
            CurveAutoBuild.Queue(host, "Curve");
            Assert.That(GetField(host).HasBake, Is.False);
            Assert.That(EditorSceneManager.SaveScene(scene, path, true), Is.True);
            Assert.That(scene.path, Is.EqualTo(originalPath));
            Assert.That(GetField(host).HasBake, Is.True, "Saving must flush the pending bake before serialization.");
            loaded = EditorSceneManager.OpenPreviewScene(path);
            GameObject root = Array.Find(loaded.GetRootGameObjects(), item => item.name == gameObject.name);
            Assert.That(root, Is.Not.Null);
            Component restoredHost = root.GetComponent(host.GetType());
            var restored = GetField(restoredHost);
            Assert.That(restored.HasBake, Is.True);
            Assert.That(restored.IsStale, Is.False);
            Assert.That(restored.Evaluate(0.5), Is.EqualTo(2).Within(1e-10));
        }
        finally
        {
            if (loaded.IsValid() && loaded.isLoaded) EditorSceneManager.ClosePreviewScene(loaded);
            Object.DestroyImmediate(gameObject);
            AssetDatabase.DeleteAsset(path);
        }
    }

    private static AnimationCurve Weighted()
    {
        return new AnimationCurve(
            new Keyframe(0, 0, 0, 3, 0, 0.15f) { weightedMode = WeightedMode.Out },
            new Keyframe(1, 1, -0.7f, 0, 0.65f, 0) { weightedMode = WeightedMode.In });
    }

    private static Component CreateHost(out GameObject gameObject)
    {
        Type type = typeof(RCurve.Tests.CurveTestHost);
        Assert.That(type, Is.Not.Null);
        gameObject = new GameObject("RCurve serialization test");
        return gameObject.AddComponent(type);
    }

    private static RationalCurve GetField(Component host)
    {
        using var serialized = new SerializedObject(host);
        return (RationalCurve)serialized.FindProperty("Curve").boxedValue;
    }

    private static void SetField(Component host, RationalCurve value)
    {
        using var serialized = new SerializedObject(host);
        serialized.FindProperty("Curve").boxedValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}

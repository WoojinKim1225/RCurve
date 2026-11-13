using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RCurve;
using RCurve.Editor;
using RCurve.Tests;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// The curves CurveChangeTracking bakes without an Inspector drawing them: edits recorded through Undo, assets
// reimported from disk, and scenes as they open. Each starts from a curve whose bake no longer matches its source
// (or is about to stop matching), changed in a way the old Inspector-only auto-build never saw.
public sealed class CurveChangeTrackingTests
{
    [Test]
    public void CurvePathIsTheCurveAnEditedPropertyIsIn()
    {
        Assert.That(CurveChangeTracking.CurvePath("Curve._source.m_Curve.Array.data[0].value"), Is.EqualTo("Curve"));
        Assert.That(CurveChangeTracking.CurvePath("outer.inner._settings._quality"), Is.EqualTo("outer.inner"));
        Assert.That(CurveChangeTracking.CurvePath("curves.Array.data[2]._source"), Is.EqualTo("curves.Array.data[2]"));
        Assert.That(CurveChangeTracking.CurvePath("Curve"), Is.EqualTo("Curve"), "A whole curve replaced.");
        Assert.That(CurveChangeTracking.CurvePath("my_source_field"), Is.EqualTo("my_source_field"), "Not a part named _source.");
        Assert.That(CurveChangeTracking.CurvePath(""), Is.Null);
    }

    [UnityTest]
    public IEnumerator AnUndoRecordedEditBakesTheCurveNoInspectorDraws()
    {
        var gameObject = new GameObject("RCurve undo tracking " + Guid.NewGuid().ToString("N"));
        try
        {
            var host = gameObject.AddComponent<CurveTestHost>();
            SetBaked(host, AnimationCurve.Linear(0, 0, 1, 4));
            // an editor tool's edit, recorded with Undo (the source is changed through serialization, as a tool does)
            Undo.RecordObject(host, "Edit curve");
            using (var serialized = new SerializedObject(host))
            {
                serialized.FindProperty("Curve._source").animationCurveValue = AnimationCurve.Linear(0, 0, 1, 8);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Undo.FlushUndoRecordObjects();
            Assert.That(Curve(host).IsStale, Is.True, "Changed, not yet baked.");
            // the edit is queued on the next editor tick; flushing stands in for the debounce running out
            for (int i = 0; i < 5; i++) yield return null;
            CurveAutoBuild.FlushPending();
            RationalCurve curve = Curve(host);
            Assert.That(curve.IsStale, Is.False);
            Assert.That(curve.Evaluate(0.5), Is.EqualTo(4).Within(1e-9));
        }
        finally
        {
            Undo.ClearAll();
            Object.DestroyImmediate(gameObject);
        }
    }

    [UnityTest]
    public IEnumerator AnAssetChangedOnDiskIsBakedAndSavedWhenReimported()
    {
        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/RCurveReimportTest.asset");
        try
        {
            var asset = ScriptableObject.CreateInstance<CurveTestAsset>();
            asset.Curve = new RationalCurve(AnimationCurve.Linear(0, 0, 1, 4));
            asset.Curve.Bake();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            for (int i = 0; i < 5; i++) yield return null;

            // a text edit or a merge: the end key's value 4 -> 8, the bake left as it was
            string text = File.ReadAllText(path);
            var endKey = new Regex(@"^(\s+)value: 4$", RegexOptions.Multiline);
            Assert.That(endKey.Matches(text).Count, Is.EqualTo(1), "One key with value 4 in the file.");
            File.WriteAllText(path, endKey.Replace(text, "${1}value: 8"));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            // built and saved after the import, on a later editor tick
            CurveTestAsset reloaded = null;
            for (int i = 0; i < 20; i++)
            {
                yield return null;
                reloaded = AssetDatabase.LoadAssetAtPath<CurveTestAsset>(path);
                if (reloaded != null && !reloaded.Curve.IsStale) break;
            }
            Assert.That(reloaded, Is.Not.Null);
            Assert.That(reloaded.Curve.IsStale, Is.False);
            Assert.That(reloaded.Curve.Evaluate(0.5), Is.EqualTo(4).Within(1e-9));
            Assert.That(EditorUtility.IsDirty(reloaded), Is.False, "Saved with the asset, not left dirty.");
            Assert.That(File.ReadAllText(path), Does.Contain("value: 8"));
        }
        finally { AssetDatabase.DeleteAsset(path); }
    }

    [Test]
    public void AStaleCurveInASceneIsBakedWhenTheSceneOpens()
    {
        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/RCurveSceneOpenTest.unity");
        string name = "RCurve scene tracking " + Guid.NewGuid().ToString("N");
        // saved as a copy and opened beside the active scene, then closed; the active scene is never renamed or closed
        var gameObject = new GameObject(name);
        Scene scene = gameObject.scene;
        Scene reopened = default;
        try
        {
            var host = gameObject.AddComponent<CurveTestHost>();
            SetBaked(host, AnimationCurve.Linear(0, 0, 1, 4));
            using (var serialized = new SerializedObject(host))
            {
                // changed without Undo: nothing queues it
                serialized.FindProperty("Curve._source").animationCurveValue = AnimationCurve.Linear(0, 0, 1, 8);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.That(EditorSceneManager.SaveScene(scene, path, true), Is.True);
            Assert.That(Curve(host).IsStale, Is.True, "Saved stale: nothing had queued it.");
            Object.DestroyImmediate(gameObject);

            reopened = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            CurveAutoBuild.FlushPending(); // stands in for the debounce running out
            GameObject root = Array.Find(reopened.GetRootGameObjects(), item => item.name == name);
            Assert.That(root, Is.Not.Null);
            RationalCurve curve = Curve(root.GetComponent<CurveTestHost>());
            Assert.That(curve.IsStale, Is.False);
            Assert.That(curve.Evaluate(0.5), Is.EqualTo(4).Within(1e-9));
            Assert.That(reopened.isDirty, Is.True, "Marked dirty to be saved.");
        }
        finally
        {
            if (gameObject != null) Object.DestroyImmediate(gameObject);
            if (reopened.IsValid() && reopened.isLoaded) EditorSceneManager.CloseScene(reopened, true);
            AssetDatabase.DeleteAsset(path);
        }
    }

    private static void SetBaked(CurveTestHost host, AnimationCurve source)
    {
        var curve = new RationalCurve(source);
        curve.Bake();
        using var serialized = new SerializedObject(host);
        serialized.FindProperty("Curve").boxedValue = curve;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static RationalCurve Curve(Component host)
    {
        using var serialized = new SerializedObject(host);
        return (RationalCurve)serialized.FindProperty("Curve").boxedValue;
    }
}

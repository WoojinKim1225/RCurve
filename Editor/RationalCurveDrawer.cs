using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RCurve.Editor
{
    [CustomPropertyDrawer(typeof(RationalCurve))]
    public sealed class RationalCurveDrawer : PropertyDrawer
    {
        private const float ErrorHeight = 56;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded) return height;
            foreach (SerializedProperty setting in Settings(property))
                height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(setting, true);
            if (BuildError(property) != null) height += EditorGUIUtility.standardVerticalSpacing + ErrorHeight;
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            int indent = EditorGUI.indentLevel;
            try
            {
                string error = BuildError(property);
                EditorGUI.BeginChangeCheck();
                var header = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
                var foldout = new Rect(header.x, header.y, EditorGUIUtility.labelWidth, header.height);
                property.isExpanded = EditorGUI.Foldout(foldout, property.isExpanded, label, true);
                var curveRect = new Rect(foldout.xMax, header.y, Math.Max(0, header.xMax - foldout.xMax), header.height);
                EditorGUI.indentLevel = 0;
                if (error != null)
                {
                    curveRect.xMax -= 20;
                    var icon = new GUIContent(EditorGUIUtility.IconContent("console.erroricon.sml")) { tooltip = error };
                    GUI.Label(new Rect(header.xMax - 18, header.y, 18, header.height), icon);
                }
                EditorGUI.PropertyField(curveRect, property.FindPropertyRelative("_source"), GUIContent.none);
                if (property.isExpanded)
                {
                    EditorGUI.indentLevel = indent + 1;
                    float y = header.yMax;
                    foreach (SerializedProperty setting in Settings(property))
                    {
                        y += EditorGUIUtility.standardVerticalSpacing;
                        float height = EditorGUI.GetPropertyHeight(setting, true);
                        EditorGUI.PropertyField(new Rect(position.x, y, position.width, height), setting, true);
                        y += height;
                    }
                    if (error != null)
                        EditorGUI.HelpBox(new Rect(position.x, y + EditorGUIUtility.standardVerticalSpacing, position.width, ErrorHeight), error, MessageType.Error);
                }
                bool changed = EditorGUI.EndChangeCheck();
                Object[] targets = property.serializedObject.targetObjects;
                string path = property.propertyPath;
                foreach (Object target in targets) CurveAutoBuild.Queue(target, path);
                if (changed)
                    EditorApplication.delayCall += () => { foreach (Object target in targets) CurveAutoBuild.Queue(target, path); };
            }
            finally
            {
                EditorGUI.indentLevel = indent;
                EditorGUI.EndProperty();
            }
            // Unity's Debug Inspector bypasses custom property drawers and exposes serialized bake data.
        }

        private static IEnumerable<SerializedProperty> Settings(SerializedProperty property)
        {
            using SerializedProperty settings = property.FindPropertyRelative("_settings");
            using SerializedProperty iterator = settings.Copy();
            using SerializedProperty end = settings.GetEndProperty();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                yield return iterator;
                enterChildren = false;
            }
        }

        private static string BuildError(SerializedProperty property)
        {
            if (property.hasMultipleDifferentValues || !(property.boxedValue is RationalCurve curve) ||
                string.IsNullOrEmpty(curve.BuildError) || curve.NeedsBuild) return null;
            return (curve.HasBake ? "Build failed; keeping the previous bake. " : "Build failed; no baked result. ") + curve.BuildError;
        }
    }

    [InitializeOnLoad]
    public static class CurveAutoBuild
    {
        private sealed class Pending
        {
            public WeakReference<Object> Target;
            public string PropertyPath;
            public string InputHash;
            public double BuildAfterTime;
            public Object GetTarget() => Target.TryGetTarget(out Object target) ? target : null;
        }

        private const double DebounceSeconds = 0.35;
        // Weak references avoid retaining assets and work across Unity 6.0/6.6 without instance-ID APIs.
        // ponytail: linear lookup over inspected fields; index only if inspector scale warrants it.
        private static readonly List<Pending> _observed = new();

        static CurveAutoBuild()
        {
            EditorApplication.update += Update;
            Undo.undoRedoPerformed += QueueObserved;
            EditorSceneManager.sceneSaving += (scene, path) => FlushPending();
        }

        public static void Queue(Object target, string propertyPath)
        {
            if (target == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            Pending pending = _observed.Find(item => item.GetTarget() == target && item.PropertyPath == propertyPath);
            using var serialized = new SerializedObject(target);
            if (!(serialized.FindProperty(propertyPath)?.boxedValue is RationalCurve curve))
            {
                if (pending != null) _observed.Remove(pending);
                return;
            }
            if (pending == null)
            {
                pending = new Pending { Target = new WeakReference<Object>(target), PropertyPath = propertyPath };
                _observed.Add(pending);
            }
            if (!curve.NeedsBuild) { pending.InputHash = null; return; }
            string hash = curve.InputHash;
            if (pending.InputHash != hash)
            {
                pending.InputHash = hash;
                pending.BuildAfterTime = EditorApplication.timeSinceStartup + DebounceSeconds;
            }
        }

        private static void QueueObserved()
        {
            foreach (Pending pending in _observed.ToArray())
            {
                Object target = pending.GetTarget();
                if (target != null) Queue(target, pending.PropertyPath);
                else _observed.Remove(pending);
            }
        }

        private static void Update()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || GUIUtility.hotControl != 0) return;
            foreach (Pending pending in _observed.ToArray())
            {
                if (pending.GetTarget() == null) { _observed.Remove(pending); continue; }
                if (pending.InputHash == null || EditorApplication.timeSinceStartup < pending.BuildAfterTime) continue;
                Rebuild(pending, pending.InputHash);
            }
        }

        // Save is also an edit-commit boundary; do not save an avoidably stale pending bake.
        public static void FlushPending()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            QueueObserved();
            foreach (Pending pending in _observed.ToArray())
                if (pending.InputHash != null) Rebuild(pending, pending.InputHash);
        }

        private static void Rebuild(Pending pending, string expectedHash)
        {
            pending.InputHash = null;
            Object target = pending.GetTarget();
            if (target == null) { _observed.Remove(pending); return; }
            using var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(pending.PropertyPath);
            if (!(property?.boxedValue is RationalCurve curve)) { _observed.Remove(pending); return; }
            if (curve.InputHash != expectedHash) { Queue(target, pending.PropertyPath); return; }
            Store(target, serialized, property, curve, expectedHash);
        }

        // Builds now, without the debounce, when the curve's input changed since its last attempt; whether it built.
        internal static bool BuildNow(Object target, string propertyPath)
        {
            if (target == null || EditorApplication.isPlayingOrWillChangePlaymode) return false;
            using var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyPath);
            if (!(property?.boxedValue is RationalCurve curve) || !curve.NeedsBuild) return false;
            Store(target, serialized, property, curve, curve.InputHash);
            return true;
        }

        private static void Store(Object target, SerializedObject serialized, SerializedProperty property,
            RationalCurve curve, string expectedHash)
        {
            try
            {
                curve.Bake();
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is NotSupportedException)
            {
                curve.StoreBuildFailure(expectedHash, exception.Message);
            }
            property.boxedValue = curve;
            // Bake is derived data, not a separate Undo step that would immediately rebuild itself.
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
            if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            if (target is Component component && component.gameObject.scene.IsValid() &&
                !EditorSceneManager.IsPreviewScene(component.gameObject.scene))
                EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }
    }

    internal sealed class CurveSaveProcessor : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            CurveAutoBuild.FlushPending();
            return paths;
        }
    }
}

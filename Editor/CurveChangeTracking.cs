using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RCurve.Editor
{
    // Bakes curves changed where no Inspector draws them:
    // - edits recorded with Undo.RecordObject, on any field (folded away or never shown), debounced like the
    //   Inspector's (Unity does not raise Undo.postprocessModifications for SerializedObject.ApplyModifiedProperties
    //   called from a script; such tools call CurveAutoBuild.Queue);
    // - assets reimported from disk (text edits, version control), built and saved at once;
    // - scenes as they open (a scene file is not an asset the importer hands over), marked dirty to save.
    // Work is deferred to the next EditorApplication.update, which runs in batch mode too (delayCall does not).
    // Changes made any other way are not seen here; RationalCurve.IsStale tells them, and evaluation throws.
    [InitializeOnLoad]
    internal static class CurveChangeTracking
    {
        static CurveChangeTracking()
        {
            Undo.postprocessModifications += OnModifications;
            EditorSceneManager.sceneOpened += (scene, mode) => QueueScene(scene);
            NextTick(() =>
            {
                for (int i = 0; i < SceneManager.sceneCount; i++) QueueScene(SceneManager.GetSceneAt(i));
            });
        }

        // once, on the next editor update
        private static void NextTick(Action action)
        {
            void Run()
            {
                EditorApplication.update -= Run;
                action();
            }
            EditorApplication.update += Run;
        }

        // The modification is recorded before it is applied, so the curve is read on the next editor tick.
        private static UndoPropertyModification[] OnModifications(UndoPropertyModification[] modifications)
        {
            var changed = new HashSet<(Object, string)>();
            foreach (UndoPropertyModification modification in modifications)
            {
                Object target = modification.currentValue?.target;
                string path = CurvePath(modification.currentValue?.propertyPath);
                if (target != null && path != null) changed.Add((target, path));
            }
            if (changed.Count > 0)
                NextTick(() =>
                {
                    foreach ((Object target, string path) in changed) CurveAutoBuild.Queue(target, path);
                });
            return modifications;
        }

        // The curve a property path is inside: everything before its "_source" or "_settings", or the path itself
        // (a whole curve replaced). Queue ignores a path that is not a RationalCurve.
        internal static string CurvePath(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath)) return null;
            int at = LastPart(propertyPath, "_source");
            int settings = LastPart(propertyPath, "_settings");
            if (settings > at) at = settings;
            return at > 0 ? propertyPath.Substring(0, at - 1) : propertyPath;
        }

        // where the last path part named `name` starts (after a '.'), or -1
        private static int LastPart(string path, string name)
        {
            for (int at = path.LastIndexOf(name); at > 0; at = path.LastIndexOf(name, at - 1))
            {
                int end = at + name.Length;
                if (path[at - 1] == '.' && (end == path.Length || path[end] == '.')) return at;
            }
            return -1;
        }

        private static void QueueScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) return;
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            foreach (string path in CurvePaths(behaviour))
                CurveAutoBuild.Queue(behaviour, path);
        }

        // Every RationalCurve field in the object, nested ones included (the curves themselves are not entered).
        internal static List<string> CurvePaths(Object target)
        {
            var paths = new List<string>();
            if (!(target is MonoBehaviour || target is ScriptableObject)) return paths;
            using var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.GetIterator();
            bool enter = true;
            while (property.Next(enter))
            {
                if (property.type == nameof(RationalCurve) || property.type == "managedReference<" + nameof(RationalCurve) + ">")
                {
                    paths.Add(property.propertyPath);
                    enter = false;
                    continue;
                }
                enter = (property.propertyType == SerializedPropertyType.Generic ||
                         property.propertyType == SerializedPropertyType.ManagedReference) && !IsPlainArray(property);
            }
            return paths;
        }

        // an array of values that hold no fields (nothing to look for in its elements)
        private static bool IsPlainArray(SerializedProperty property)
        {
            if (!property.isArray) return false;
            switch (property.arrayElementType)
            {
                case "float": case "double": case "int": case "uint": case "long": case "ulong": case "short":
                case "ushort": case "char": case "bool": case "string": return true;
                default: return property.arrayElementType.StartsWith("PPtr<");
            }
        }

        // Assets changed on disk (or saved) are reimported: build what changed in them and save it with the file.
        private sealed class Importer : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                var paths = new List<string>();
                foreach (string path in imported)
                    if ((path.EndsWith(".asset") || path.EndsWith(".prefab")) && MayHoldCurve(path)) paths.Add(path);
                if (paths.Count == 0) return;
                // assets are changed and saved after the import, not during it
                NextTick(() =>
                {
                    foreach (string path in paths) BuildAsset(path);
                });
            }

            // a text asset holding a RationalCurve has its "_hasBake" field; a binary one is checked by loading it
            private static bool MayHoldCurve(string path)
            {
                try
                {
                    using var reader = new StreamReader(path);
                    char[] head = new char[16];
                    int read = reader.Read(head, 0, head.Length);
                    if (!new string(head, 0, read).StartsWith("%YAML")) return true;
                    return (new string(head, 0, read) + reader.ReadToEnd()).Contains("_hasBake:");
                }
                catch (IOException)
                {
                    return true;
                }
            }

            private static void BuildAsset(string path)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                bool built = false;
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                foreach (string curve in CurvePaths(asset))
                    built |= CurveAutoBuild.BuildNow(asset, curve);
                if (built) AssetDatabase.SaveAssetIfDirty(AssetDatabase.GUIDFromAssetPath(path));
            }
        }
    }
}

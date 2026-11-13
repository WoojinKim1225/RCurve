using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace RCurve.Demo.Editor
{
    // Batch-only authoring utility. Run in an isolated project; never replace the user's open scene.
    public static class CurveComparisonSceneBuilder
    {
        public static string SampleDirectory
        {
            get
            {
                foreach (string guid in AssetDatabase.FindAssets("CurveComparisonDemo t:MonoScript"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    if (script != null && script.GetClass() == typeof(CurveComparisonDemo))
                        return Path.GetDirectoryName(path).Replace('\\', '/');
                }
                throw new InvalidOperationException("Import the Curve Comparison sample first.");
            }
        }
        public static string ScenePath => SampleDirectory + "/Comparison.unity";

        public static void RebakeExisting()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Rebake in an isolated batch project.");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent<CurveComparisonDemo>(out var demo))
                {
                    demo.Curve.Bake();
                    if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save rebaked scene.");
                    return;
                }
            throw new InvalidOperationException("Comparison scene has no demo component.");
        }

        public static void Create()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Generate the demo in an isolated batch project.");
            Directory.CreateDirectory(SampleDirectory + "/Materials");
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Comparison Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.orthographicSize = 4.2f;
            camera.rect = new Rect(0, 0, 1, 0.6f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.075f, 0.1f);
            camera.tag = "MainCamera";
            var blue = Material("UnityBlue", new Color(0.15f, 0.65f, 1));
            var orange = Material("RationalOrange", new Color(1, 0.5f, 0.12f));
            var grid = Material("Grid", new Color(0.18f, 0.22f, 0.28f));
            for (int i = -1; i <= 1; i++)
                Primitive("Reference " + i, PrimitiveType.Cube, new Vector3(0, 2.5f * i, 0.6f), new Vector3(6, 0.025f, 0.025f), grid);
            Primitive("Unity lane", PrimitiveType.Cube, new Vector3(-2, 0, 0.6f), new Vector3(0.025f, 6.5f, 0.025f), grid);
            Primitive("Rational lane", PrimitiveType.Cube, new Vector3(2, 0, 0.6f), new Vector3(0.025f, 6.5f, 0.025f), grid);
            var demo = new GameObject("Curve Comparison - select to edit source and lookup mode").AddComponent<CurveComparisonDemo>();
            demo.UnityMarker = Primitive("Unity AnimationCurve", PrimitiveType.Sphere, new Vector3(-2, 0, 0), Vector3.one * 0.5f, blue);
            demo.RationalMarker = Primitive("RationalCurve", PrimitiveType.Sphere, new Vector3(2, 0, 0), Vector3.one * 0.5f, orange);
            var source = new AnimationCurve(
                Key(0, 0, 1, 2, 0.35f, 0.6f), Key(0.35f, 1, 1, -2, 0.4f, 0.2f),
                Key(0.7f, -0.5f, -1, 1.2f, 0.6f, 0.3f), Key(1, 0, 1, 1, 0.3f, 0.35f))
                { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.Loop };
            demo.Curve = new RationalCurve(source);
            demo.Curve.StoreBuild(RationalCurveBuilder.Build(source, demo.Curve.Settings), demo.Curve.InputHash);
            ComparisonCanvasBuilder.Build(demo);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save comparison scene.");
            AssetDatabase.SaveAssets();
        }

        private static Keyframe Key(float time, float value, float incoming, float outgoing, float inWeight, float outWeight)
            => new Keyframe(time, value, incoming, outgoing, inWeight, outWeight) { weightedMode = WeightedMode.Both };

        private static Material Material(string name, Color color)
        {
            string path = SampleDirectory + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Unlit/Color");
            if (shader == null) throw new InvalidOperationException("This authoring scene uses the Built-in Unlit/Color shader.");
            material = new Material(shader) { color = color, name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Transform Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var instance = GameObject.CreatePrimitive(type);
            instance.name = name; instance.transform.position = position; instance.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(instance.GetComponent<Collider>());
            var renderer = instance.GetComponent<Renderer>();
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return instance.transform;
        }
    }
}

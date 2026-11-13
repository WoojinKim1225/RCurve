using System;
using RCurve;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildPlayerCheck
{
    // -executeMethod BuildPlayerCheck.Build
    public static void Build()
    {
        try
        {
            var asset = ScriptableObject.CreateInstance<PlayerCheckCurves>();
            var random = new System.Random(5);
            var curves = new System.Collections.Generic.List<RationalCurve>();
            foreach (CurveLookupMode mode in new[] { CurveLookupMode.Magic, CurveLookupMode.LUT, CurveLookupMode.BruteForce, CurveLookupMode.Auto })
                for (int i = 0; i < 4; i++)
                {
                    var curve = new RationalCurve(PlayerCheck.RandomCurve(random), new CurveFitSettings(lookupMode: mode));
                    try { curve.Bake(); } catch (InvalidOperationException) { continue; }
                    curves.Add(curve);
                }
            asset.Curves = curves.ToArray();
            AssetDatabase.CreateAsset(asset, "Assets/PlayerCheck/Resources/PlayerCheckCurves.asset");
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Player check").AddComponent<PlayerCheck>();
            EditorSceneManager.SaveScene(scene, "Assets/PlayerCheck/PlayerCheck.unity");

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/PlayerCheck/PlayerCheck.unity" },
                locationPathName = "Build/PlayerCheck.x86_64",
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.None
            });
            Debug.Log($"PLAYERCHECK build {report.summary.result}, {curves.Count} baked curves");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
        catch (Exception exception)
        {
            Debug.LogError("PLAYERCHECK " + exception);
            EditorApplication.Exit(1);
        }
    }
}

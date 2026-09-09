using System;
using RCurve;
using UnityEngine;

public sealed class RCurveWebDemo : MonoBehaviour
{
    private readonly float[] _values = { 0, 0.85f, 0.15f, 1 };
    private const int SampleCount = 161;
    private float[] _sourceSamples;
    private float[] _bakedSamples;
    private double _maxError;
    private bool _dirty;
    private string _error;
    private GUIStyle _title;

    private void Awake() => Rebuild();

    private void Rebuild()
    {
        _dirty = false;
        try
        {
            foreach (float value in _values)
                if (float.IsNaN(value) || float.IsInfinity(value))
                    throw new ArgumentException("Key values must be finite.");
            var source = new AnimationCurve(
                new Keyframe(0, _values[0], 0, 0),
                new Keyframe(1f / 3, _values[1], 0, 0),
                new Keyframe(2f / 3, _values[2], 0, 0),
                new Keyframe(1, _values[3], 0, 0));
            source.preWrapMode = source.postWrapMode = WrapMode.ClampForever;
            var curve = new RationalCurve(source, new CurveFitSettings(
                CurveFitObjective.LeastSquares, CurveFitQuality.Low, 0.002, 0.02, 8));
            curve.Bake();
            var sourceSamples = new float[SampleCount];
            var bakedSamples = new float[SampleCount];
            double maxError = 0;
            for (int i = 0; i < SampleCount; i++)
            {
                float t = i / (SampleCount - 1f);
                sourceSamples[i] = source.Evaluate(t);
                double value = curve.Evaluate(t);
                bakedSamples[i] = (float)value;
                maxError = Math.Max(maxError, Math.Abs(sourceSamples[i] - value));
            }
            // Publish only complete results; failed bakes keep the last good graph.
            _sourceSamples = sourceSamples;
            _bakedSamples = bakedSamples;
            _maxError = maxError;
            _error = null;
        }
        catch (Exception exception) { _error = exception.Message; }
    }

    private void OnGUI()
    {
        _title ??= new GUIStyle(GUI.skin.label) { fontSize = 25, fontStyle = FontStyle.Bold };
        float width = Mathf.Min(980, Screen.width - 32);
        GUILayout.BeginArea(new Rect((Screen.width - width) / 2, 14, width, Screen.height - 28));
        GUILayout.Label("RCurve — runtime rational curve baking", _title);
        GUILayout.Label("Key values preserved; tangents fixed at zero. R(u) = P(u) / Q(u).");
        GUILayout.Label("Interactive preview: LeastSquares / Low (3/3), value tolerance 0.002, slope tolerance 0.02.");

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Ease", GUILayout.Width(100))) Set(0, 0, 1, 1);
        if (GUILayout.Button("Wave", GUILayout.Width(100))) Set(0, 1, -0.3f, 0.8f);
        if (GUILayout.Button("Overshoot", GUILayout.Width(100))) Set(0, 1.3f, 0.7f, 1);
        GUILayout.EndHorizontal();

        for (int i = 0; i < _values.Length; i++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Key {i + 1}: {_values[i],6:F2}", GUILayout.Width(100));
            float value = GUILayout.HorizontalSlider(_values[i], -1, 2);
            _dirty |= value != _values[i];
            _values[i] = value;
            GUILayout.EndHorizontal();
        }
        if (_dirty && GUIUtility.hotControl == 0) Rebuild();

        Rect graph = GUILayoutUtility.GetRect(width, Mathf.Max(220, Screen.height - 285));
        GUI.Box(graph, GUIContent.none);
        if (Event.current.type == EventType.Repaint)
        {
            DrawGrid(graph);
            if (_sourceSamples != null)
            {
                DrawCurve(graph, _sourceSamples, new Color(0.2f, 0.65f, 1), 3);
                DrawCurve(graph, _bakedSamples, new Color(1, 0.45f, 0.12f), 2);
            }
        }
        GUILayout.Label("Blue: Unity AnimationCurve    Orange: RCurve    Release the slider to bake.");
        if (_sourceSamples != null) GUILayout.Label($"Max sampled error ({SampleCount} points): {_maxError:G4}");
        if (_error != null) GUILayout.Label("Bake failed; graph unchanged: " + _error);
        GUILayout.EndArea();
    }

    private void Set(float a, float b, float c, float d)
    {
        _values[0] = a; _values[1] = b; _values[2] = c; _values[3] = d;
        Rebuild();
    }

    private void DrawGrid(Rect rect)
    {
        for (int i = 0; i <= 6; i++) DrawLine(new Vector2(rect.x, rect.y + rect.height * i / 6), new Vector2(rect.xMax, rect.y + rect.height * i / 6), new Color(1, 1, 1, 0.12f), 1);
        for (int i = 0; i <= 6; i++) DrawLine(new Vector2(rect.x + rect.width * i / 6, rect.y), new Vector2(rect.x + rect.width * i / 6, rect.yMax), new Color(1, 1, 1, 0.12f), 1);
    }

    private void DrawCurve(Rect rect, float[] samples, Color color, float thickness)
    {
        Vector2 previous = Point(rect, 0, samples[0]);
        for (int i = 1; i < samples.Length; i++)
        {
            float t = i / (samples.Length - 1f);
            Vector2 next = Point(rect, t, samples[i]);
            DrawLine(previous, next, color, thickness);
            previous = next;
        }
    }

    private static Vector2 Point(Rect rect, float t, float value) => new(rect.x + t * rect.width, rect.yMax - Mathf.InverseLerp(-1, 2, value) * rect.height);

    private void DrawLine(Vector2 a, Vector2 b, Color color, float width)
    {
        Matrix4x4 matrix = GUI.matrix;
        Color previous = GUI.color;
        float angle = Vector2.SignedAngle(Vector2.right, b - a);
        GUIUtility.RotateAroundPivot(angle, a);
        GUI.color = color;
        GUI.DrawTexture(new Rect(a.x, a.y - width / 2, Vector2.Distance(a, b), width), Texture2D.whiteTexture);
        GUI.matrix = matrix;
        GUI.color = previous;
    }
}

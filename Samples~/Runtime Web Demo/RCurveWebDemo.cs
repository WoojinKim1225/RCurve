using System;
using RCurve;
using UnityEngine;

public sealed class RCurveWebDemo : MonoBehaviour
{
    private const int SampleCount = 161;
    private static readonly string[] KeyCounts = { "2 keys", "3 keys", "4 keys", "5 keys" };
    private readonly float[] _values = { 0, 0.85f, 0.15f, 1, 0.5f };
    private readonly float[] _inSlopes = new float[5];
    private readonly float[] _outSlopes = new float[5];
    private readonly float[] _inWeights = { 1f / 3, 1f / 3, 1f / 3, 1f / 3, 1f / 3 };
    private readonly float[] _outWeights = { 1f / 3, 1f / 3, 1f / 3, 1f / 3, 1f / 3 };
    private int _keyCount = 4;
    private float[] _sourceSamples;
    private float[] _bakedSamples;
    private BakedRationalCurve _curve;
    private float _graphMin = -1, _graphMax = 2;
    private double _maxError;
    private bool _dirty;
    private string _error;
    private GUIStyle _title;
    private Vector2 _scroll;

    private void Awake() => Rebuild();

    private void Rebuild()
    {
        _dirty = false;
        try
        {
            if (_keyCount < 2 || _keyCount > 5) throw new ArgumentOutOfRangeException(nameof(_keyCount));
            var keys = new Keyframe[_keyCount];
            for (int i = 0; i < keys.Length; i++)
            {
                RequireFinite(_values[i]); RequireFinite(_inSlopes[i]); RequireFinite(_outSlopes[i]);
                RequireFinite(_inWeights[i]); RequireFinite(_outWeights[i]);
                if (_inWeights[i] < 0.01f || _inWeights[i] > 1 || _outWeights[i] < 0.01f || _outWeights[i] > 1)
                    throw new ArgumentOutOfRangeException("Weights must be between 0.01 and 1.");
                keys[i] = new Keyframe(i / (_keyCount - 1f), _values[i], _inSlopes[i], _outSlopes[i], _inWeights[i], _outWeights[i])
                { weightedMode = WeightedMode.Both };
            }
            var source = new AnimationCurve(keys) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
            var curve = RationalCurveBuilder.Build(source, new CurveFitSettings(
                CurveFitObjective.LeastSquares, CurveFitQuality.Low, 0.002, 0.02, 8));
            var sourceSamples = new float[SampleCount];
            var bakedSamples = new float[SampleCount];
            double maxError = 0;
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            for (int i = 0; i < SampleCount; i++)
            {
                float t = i / (SampleCount - 1f);
                sourceSamples[i] = source.Evaluate(t);
                double value = curve.Evaluate(t);
                bakedSamples[i] = (float)value;
                RequireFinite(sourceSamples[i]); RequireFinite(bakedSamples[i]);
                maxError = Math.Max(maxError, Math.Abs(sourceSamples[i] - value));
                minimum = Mathf.Min(minimum, Mathf.Min(sourceSamples[i], bakedSamples[i]));
                maximum = Mathf.Max(maximum, Mathf.Max(sourceSamples[i], bakedSamples[i]));
            }
            // Publish only complete results; failed bakes keep the last good graph and moving point.
            _sourceSamples = sourceSamples;
            _bakedSamples = bakedSamples;
            _curve = curve;
            float padding = Mathf.Max(0.1f, (maximum - minimum) * 0.1f);
            _graphMin = minimum - padding;
            _graphMax = maximum + padding;
            _maxError = maxError;
            _error = null;
        }
        catch (Exception exception) { _error = exception.Message; }
    }

    private static void RequireFinite(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("Key parameters must be finite.");
    }

    private void OnGUI()
    {
        _title ??= new GUIStyle(GUI.skin.label) { fontSize = 25, fontStyle = FontStyle.Bold };
        float width = Mathf.Min(1100, Screen.width - 32);
        GUILayout.BeginArea(new Rect((Screen.width - width) / 2, 14, width, Screen.height - 28));
        GUILayout.Label("RCurve - runtime rational curve baking", _title);
        GUILayout.Label("Equally spaced keys. In / Out = incoming / outgoing tangent; weight = handle strength.");
        GUILayout.Label("LeastSquares / Low (3/3), value tolerance 0.002, slope tolerance 0.02. Release a slider to bake.");
        GUILayout.BeginHorizontal();
        int count = GUILayout.Toolbar(_keyCount - 2, KeyCounts) + 2;
        _dirty |= count != _keyCount;
        _keyCount = count;
        if (GUILayout.Button("Ease", GUILayout.Width(80))) Set(0, 0, 1, 1);
        if (GUILayout.Button("Wave", GUILayout.Width(80))) Set(0, 1, -0.3f, 0.8f);
        if (GUILayout.Button("Overshoot", GUILayout.Width(80))) Set(0, 1.3f, 0.7f, 1);
        GUILayout.EndHorizontal();

        float controlsHeight = Mathf.Min(_keyCount * 45 + 12, Mathf.Min(270, Screen.height * 0.4f));
        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(controlsHeight));
        for (int i = 0; i < _keyCount; i++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Key {i + 1}\nt={i / (_keyCount - 1f):F2}", GUILayout.Width(65));
            Slider("Value", ref _values[i], -1, 2);
            Slider("In slope", ref _inSlopes[i], -8, 8, i > 0);
            Slider("In weight", ref _inWeights[i], 0.01f, 1, i > 0);
            Slider("Out slope", ref _outSlopes[i], -8, 8, i < _keyCount - 1);
            Slider("Out weight", ref _outWeights[i], 0.01f, 1, i < _keyCount - 1);
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
        if (_dirty && GUIUtility.hotControl == 0) Rebuild();

        Rect graph = GUILayoutUtility.GetRect(width, Mathf.Max(100, Screen.height - controlsHeight - 230));
        GUI.Box(graph, GUIContent.none);
        float time = Mathf.Repeat(Time.unscaledTime / 4, 1);
        if (Event.current.type == EventType.Repaint)
        {
            DrawGrid(graph);
            if (_curve != null)
            {
                DrawCurve(graph, _sourceSamples, new Color(0.2f, 0.65f, 1), 3);
                DrawCurve(graph, _bakedSamples, new Color(1, 0.45f, 0.12f), 2);
                Vector2 point = Point(graph, time, (float)_curve.Evaluate(time));
                GUI.DrawTexture(new Rect(point.x - 7, point.y - 7, 14, 14), Texture2D.whiteTexture,
                    ScaleMode.StretchToFill, true, 0, Color.yellow, 0, 7);
            }
        }
        GUILayout.Label($"Blue: Unity    Orange: RCurve    Yellow: RCurve(t), 4-second loop    t={time:F2}    Y: {_graphMin:F2} .. {_graphMax:F2}");
        if (_curve != null) GUILayout.Label($"Max sampled error ({SampleCount} points): {_maxError:G4}");
        if (_error != null) GUILayout.Label("Bake failed; graph and point use the last successful bake: " + _error);
        GUILayout.EndArea();
    }

    private void Set(float a, float b, float c, float d)
    {
        _keyCount = 4;
        _values[0] = a; _values[1] = b; _values[2] = c; _values[3] = d;
        Array.Clear(_inSlopes, 0, _inSlopes.Length);
        Array.Clear(_outSlopes, 0, _outSlopes.Length);
        Array.Fill(_inWeights, 1f / 3);
        Array.Fill(_outWeights, 1f / 3);
        _dirty = true;
    }

    private void Slider(string label, ref float value, float minimum, float maximum, bool enabled = true)
    {
        GUILayout.BeginVertical(GUILayout.MinWidth(95));
        bool previous = GUI.enabled;
        GUI.enabled = previous && enabled;
        GUILayout.Label(enabled ? $"{label}: {value:F2}" : label + ": N/A");
        float next = GUILayout.HorizontalSlider(value, minimum, maximum);
        _dirty |= next != value;
        value = next;
        GUI.enabled = previous;
        GUILayout.EndVertical();
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
            Vector2 next = Point(rect, i / (samples.Length - 1f), samples[i]);
            DrawLine(previous, next, color, thickness);
            previous = next;
        }
    }

    private Vector2 Point(Rect rect, float t, float value) => new(rect.x + t * rect.width, rect.yMax - (value - _graphMin) / (_graphMax - _graphMin) * rect.height);

    private void DrawLine(Vector2 a, Vector2 b, Color color, float width)
    {
        Matrix4x4 matrix = GUI.matrix;
        Color previous = GUI.color;
        GUIUtility.RotateAroundPivot(Vector2.SignedAngle(Vector2.right, b - a), a);
        GUI.color = color;
        GUI.DrawTexture(new Rect(a.x, a.y - width / 2, Vector2.Distance(a, b), width), Texture2D.whiteTexture);
        GUI.matrix = matrix;
        GUI.color = previous;
    }
}

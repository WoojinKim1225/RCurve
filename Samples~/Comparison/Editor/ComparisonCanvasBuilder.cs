using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RCurve.Demo.Editor
{
    // Authoring only: creates real, editable Canvas objects. Does not change the source curve or its bake.
    public static class ComparisonCanvasBuilder
    {
        private static readonly Color _blue = new Color(0.3f, 0.75f, 1);
        private static readonly Color _orange = new Color(1, 0.65f, 0.3f);
        private static readonly Color _muted = new Color(0.65f, 0.74f, 0.86f);

        public static void UpgradeExisting()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Upgrade the UI in an isolated batch project.");
            var scene = EditorSceneManager.OpenScene(CurveComparisonSceneBuilder.ScenePath, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent<CurveComparisonDemo>(out var demo))
                {
                    Build(demo);
                    if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Canvas comparison scene.");
                    AssetDatabase.SaveAssets();
                    return;
                }
            throw new InvalidOperationException("Comparison scene has no CurveComparisonDemo.");
        }

        public static void Build(CurveComparisonDemo demo)
        {
            var previous = demo.transform.Find("Comparison Canvas");
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            var root = Rect("Comparison Canvas", demo.transform);
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var scaler = root.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800); scaler.matchWidthOrHeight = 0.5f;
            root.gameObject.AddComponent<GraphicRaycaster>();
            var panel = Rect("Dashboard", root); Top(panel, 0, 0, 360);
            Image(panel, new Color(0.055f, 0.08f, 0.12f));
            using var serialized = new SerializedObject(demo);
            Bind(serialized, "_canvas", canvas); Bind(serialized, "_dashboard", panel);
            Label("Title", panel, "CURVE COMPARISON", 28, Color.white, 20, 12, 36, FontStyle.Bold);
            Label("Contract", panel, "Key values and finite in/out tangents are preserved. Only between-key motion is approximated.", 15, _muted, 20, 52, 30);
            var metrics = Rect("Value cards", panel); Top(metrics, 20, 88, 76);
            var cards = metrics.gameObject.AddComponent<HorizontalLayoutGroup>(); cards.spacing = 12;
            cards.childControlWidth = cards.childControlHeight = cards.childForceExpandWidth = cards.childForceExpandHeight = true;
            Bind(serialized, "_unityLabel", Metric(metrics, "Unity value", "UNITY ANIMATIONCURVE", _blue));
            Bind(serialized, "_rationalLabel", Metric(metrics, "Rational value", "RATIONAL CURVE", _orange));
            Bind(serialized, "_errorLabel", Metric(metrics, "Absolute error", "ABSOLUTE ERROR", new Color(0.5f, 0.9f, 0.75f)));
            Bind(serialized, "_maximumLabel", Metric(metrics, "Maximum error", "MAX OBSERVED", new Color(0.82f, 0.87f, 0.94f)));
            var controls = Rect("Playback controls", panel); Top(controls, 20, 176, 36);
            var buttons = controls.gameObject.AddComponent<HorizontalLayoutGroup>(); buttons.spacing = 10; buttons.childForceExpandWidth = false;
            buttons.childControlWidth = buttons.childControlHeight = buttons.childForceExpandHeight = true;
            Bind(serialized, "_playButton", Button(controls, "Play Pause", "Pause", 86));
            Bind(serialized, "_reverseButton", Button(controls, "Direction", "Direction: forward", 170));
            Bind(serialized, "_resetButton", Button(controls, "Reset", "Reset", 86));
            var toggleRect = Rect("Interval hint", controls); Width(toggleRect, 160);
            var toggle = toggleRect.gameObject.AddComponent<Toggle>();
            var box = Rect("Box", toggleRect); box.anchorMin = box.anchorMax = new Vector2(0, 0.5f); box.pivot = new Vector2(0, 0.5f); box.sizeDelta = new Vector2(22, 22);
            toggle.targetGraphic = Image(box, new Color(0.2f, 0.29f, 0.4f)); toggle.targetGraphic.raycastTarget = true;
            var check = Rect("Check", box); Stretch(check, 5); toggle.graphic = Image(check, _blue); toggle.isOn = true;
            var toggleText = Text(Rect("Label", toggleRect), "Interval hint", 15, Color.white); Stretch(toggleText.rectTransform, 0); toggleText.rectTransform.offsetMin = new Vector2(30, 0); toggleText.raycastTarget = true;
            Bind(serialized, "_hintToggle", toggle);
            Bind(serialized, "_timeLabel", Label("Time", panel, "TIME  0.0000  /  0%", 14, _muted, 20, 216, 20));
            var sliderRect = Rect("Timeline", panel); Top(sliderRect, 24, 240, 22);
            Image(sliderRect, new Color(0.055f, 0.08f, 0.12f)).raycastTarget = true;
            var slider = sliderRect.gameObject.AddComponent<Slider>(); slider.minValue = 0; slider.maxValue = 1;
            var track = Rect("Track", sliderRect); Stretch(track); track.anchorMin = new Vector2(0, 0.4f); track.anchorMax = new Vector2(1, 0.6f);
            Image(track, new Color(0.22f, 0.3f, 0.41f));
            var fillArea = Rect("Fill area", sliderRect); Stretch(fillArea);
            fillArea.anchorMin = new Vector2(0, 0.4f); fillArea.anchorMax = new Vector2(1, 0.6f);
            var fill = Rect("Fill", fillArea); Stretch(fill);
            Image(fill, new Color(0.2f, 0.53f, 0.77f)); slider.fillRect = fill;
            var handleArea = Rect("Handle area", sliderRect); Stretch(handleArea); handleArea.offsetMin = new Vector2(9, 0); handleArea.offsetMax = new Vector2(-9, 0);
            var handle = Rect("Handle", handleArea); handle.anchorMin = Vector2.zero; handle.anchorMax = new Vector2(0, 1); handle.sizeDelta = new Vector2(18, 0);
            slider.targetGraphic = Image(handle, _blue); slider.targetGraphic.raycastTarget = true; slider.handleRect = handle; Bind(serialized, "_timeline", slider);
            Label("Error scale", panel, "SIGNED ERROR  /  x1000 visual aid only - marker positions are NOT exaggerated", 12, _muted, 20, 272, 18);
            var errorTrack = Rect("Signed error track", panel); Top(errorTrack, 20, 294, 7); Image(errorTrack, new Color(0.2f, 0.28f, 0.38f));
            var errorFill = Rect("Signed error fill", errorTrack); errorFill.anchorMin = errorFill.anchorMax = new Vector2(0.5f, 0.5f); errorFill.sizeDelta = Vector2.zero; Image(errorFill, _orange); Bind(serialized, "_errorFill", errorFill);
            var zero = Rect("Zero", errorTrack); zero.anchorMin = new Vector2(0.5f, 0); zero.anchorMax = new Vector2(0.5f, 1); zero.sizeDelta = new Vector2(1, 6); Image(zero, _muted);
            Bind(serialized, "_strategyLabel", Label("Lookup strategy", panel, "Loading saved lookup strategy...", 13, _muted, 20, 310, 20));
            Bind(serialized, "_statusLabel", Label("Status", panel, "Visual accuracy demo, not a timing benchmark. Stop Play Mode to edit / rebake.", 12, _muted, 20, 335, 20));
            var lanes = Rect("Lane labels", root); lanes.anchorMin = new Vector2(0, 0); lanes.anchorMax = new Vector2(1, 0); lanes.pivot = new Vector2(0.5f, 1); lanes.sizeDelta = new Vector2(0, 24); lanes.anchoredPosition = new Vector2(0, 430);
            var laneLayout = lanes.gameObject.AddComponent<HorizontalLayoutGroup>();
            laneLayout.childControlWidth = laneLayout.childControlHeight = laneLayout.childForceExpandWidth = laneLayout.childForceExpandHeight = true;
            Text(Rect("Unity lane label", lanes), "UNITY  /  BLUE", 16, _blue, TextAnchor.MiddleCenter);
            Text(Rect("Rational lane label", lanes), "RATIONAL  /  ORANGE", 16, _orange, TextAnchor.MiddleCenter);
            Bind(serialized, "_laneLabels", lanes);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EventSystem system = null;
            foreach (var sceneRoot in demo.gameObject.scene.GetRootGameObjects())
                if (sceneRoot.TryGetComponent<EventSystem>(out var found)) system = found;
            if (system == null)
            {
                var host = new GameObject("Comparison EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
                // Editor-only reflection avoids a mandatory Input System dependency. The component is serialized in the scene.
                var inputType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (inputType == null) throw new InvalidOperationException("Input System UI module is unavailable.");
                host.AddComponent(inputType);
#else
                host.AddComponent<StandaloneInputModule>();
#endif
            }
        }

        private static void Bind(SerializedObject target, string field, UnityEngine.Object value) => target.FindProperty(field).objectReferenceValue = value;
        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
            go.transform.SetParent(parent, false); return (RectTransform)go.transform;
        }
        private static void Top(RectTransform rect, float inset, float top, float height)
        {
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 1);
            rect.sizeDelta = new Vector2(-2 * inset, height); rect.anchoredPosition = new Vector2(0, -top);
        }
        private static void Stretch(RectTransform rect, float inset = 0)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.one * inset; rect.offsetMax = -Vector2.one * inset;
        }
        private static Image Image(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image;
        }
        private static Text Text(RectTransform rect, string text, int size, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var label = rect.gameObject.AddComponent<Text>(); label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = text; label.fontSize = size; label.color = color; label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; label.raycastTarget = false;
            return label;
        }
        private static Text Label(string name, Transform parent, string text, int size, Color color, float inset, float top, float height, FontStyle style = FontStyle.Normal)
        {
            var rect = Rect(name, parent); Top(rect, inset, top, height);
            var label = Text(rect, text, size, color); label.fontStyle = style; return label;
        }
        private static Text Metric(Transform parent, string name, string caption, Color color)
        {
            var card = Rect(name, parent); card.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Image(card, new Color(0.095f, 0.14f, 0.2f));
            var accent = Rect("Accent", card); accent.anchorMin = Vector2.zero; accent.anchorMax = new Vector2(0, 1); accent.pivot = new Vector2(0, 0.5f); accent.sizeDelta = new Vector2(3, 0); Image(accent, color);
            Label("Caption", card, caption, 11, _muted, 14, 8, 20, FontStyle.Bold);
            return Label("Value", card, "--", 26, color, 14, 30, 40, FontStyle.Bold);
        }
        private static void Width(RectTransform rect, float width)
        {
            var layout = rect.gameObject.AddComponent<LayoutElement>(); layout.preferredWidth = width; layout.minWidth = width; layout.preferredHeight = 36;
        }
        private static Button Button(Transform parent, string name, string caption, float width)
        {
            var rect = Rect(name, parent); Width(rect, width);
            var image = Image(rect, new Color(0.15f, 0.25f, 0.36f)); image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var labelRect = Rect("Label", rect); Stretch(labelRect); Text(labelRect, caption, 15, Color.white, TextAnchor.MiddleCenter);
            return button;
        }
    }
}

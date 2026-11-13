using System;
using UnityEngine;
using UnityEngine.UI;

namespace RCurve.Demo
{
    // Visual accuracy demo, not a frame-time benchmark. No runtime fitting or shared playback state.
    public sealed class CurveComparisonDemo : MonoBehaviour
    {
        public RationalCurve Curve = new RationalCurve();
        public Transform UnityMarker;
        public Transform RationalMarker;
        [SerializeField] private float _verticalScale = 2.5f;
        [SerializeField, HideInInspector] private Canvas _canvas;
        [SerializeField, HideInInspector] private RectTransform _dashboard;
        [SerializeField, HideInInspector] private RectTransform _laneLabels;
        [SerializeField, HideInInspector] private Text _unityLabel, _rationalLabel, _errorLabel, _maximumLabel, _timeLabel, _strategyLabel, _statusLabel;
        [SerializeField, HideInInspector] private Button _playButton, _reverseButton, _resetButton;
        [SerializeField, HideInInspector] private Slider _timeline;
        [SerializeField, HideInInspector] private Toggle _hintToggle;
        [SerializeField, HideInInspector] private RectTransform _errorFill;
        private AnimationCurve _source;
        private BakedRationalCurve _baked;
        private bool _playing = true;
        private bool _useHint = true;
        private float _speed = 0.2f;
        private float _phase;
        private int _intervalHint = -1;
        private double _unityValue, _rationalValue, _maximumError;
        private float _time;
        private string _error;
        private Camera _camera;
        private Rect _originalViewport;
        private readonly Vector3[] _corners = new Vector3[4];

        private void OnEnable()
        {
            if (!Application.isPlaying || _canvas == null) return;
            _camera = Camera.main;
            if (_camera != null) _originalViewport = _camera.rect;
            _playButton.onClick.AddListener(TogglePlayback);
            _reverseButton.onClick.AddListener(Reverse);
            _resetButton.onClick.AddListener(ResetPlayback);
            _timeline.onValueChanged.AddListener(Seek);
            _hintToggle.onValueChanged.AddListener(SetHint);
            Canvas.willRenderCanvases += UpdateViewport;
        }

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= UpdateViewport;
            if (_camera != null) _camera.rect = _originalViewport;
            if (_canvas == null) return;
            _playButton.onClick.RemoveListener(TogglePlayback);
            _reverseButton.onClick.RemoveListener(Reverse);
            _resetButton.onClick.RemoveListener(ResetPlayback);
            _timeline.onValueChanged.RemoveListener(Seek);
            _hintToggle.onValueChanged.RemoveListener(SetHint);
        }

        private void Start()
        {
            if (Curve == null || !Curve.HasBake || Curve.IsStale || UnityMarker == null || RationalMarker == null)
            {
                _error = "Stop Play Mode, assign both markers, and bake the curve in the Inspector first.";
                RefreshUI();
                return;
            }
            _source = Curve.CopySource();
            _baked = Curve.BakedCurve;
            ApplyTime();
        }

        private void Update()
        {
            if (_baked == null) return;
            if (_playing) _phase = Mathf.Repeat(_phase + Time.deltaTime * _speed, 1);
            ApplyTime();
        }

        private void ApplyTime()
        {
            float first = _source.length == 0 ? 0 : _source[0].time;
            float last = _source.length < 2 ? first : _source[_source.length - 1].time;
            _time = Mathf.Lerp(first, last, _phase);
            _unityValue = _source.Evaluate(_time);
            _rationalValue = _useHint ? _baked.Evaluate(_time, ref _intervalHint) : _baked.Evaluate(_time);
            _maximumError = Math.Max(_maximumError, Math.Abs(_rationalValue - _unityValue));
            var position = UnityMarker.localPosition;
            position.y = (float)_unityValue * _verticalScale;
            UnityMarker.localPosition = position;
            position = RationalMarker.localPosition;
            position.y = (float)_rationalValue * _verticalScale;
            RationalMarker.localPosition = position;
            RefreshUI();
        }

        private void TogglePlayback() { _playing = !_playing; RefreshUI(); }
        private void Reverse() { _speed = -_speed; RefreshUI(); }
        private void ResetPlayback()
        {
            _phase = 0; _maximumError = 0; _intervalHint = -1;
            if (_baked != null) ApplyTime();
        }
        private void Seek(float phase)
        {
            _phase = phase; _playing = false; _intervalHint = -1;
            if (_baked != null) ApplyTime();
        }
        private void SetHint(bool enabled) { _useHint = enabled; _intervalHint = -1; RefreshUI(); }

        private void UpdateViewport()
        {
            if (_camera == null || _canvas == null || _dashboard == null || Screen.height == 0) return;
            _dashboard.GetWorldCorners(_corners);
            float height = Mathf.Clamp01(RectTransformUtility.WorldToScreenPoint(null, _corners[0]).y / Screen.height);
            _camera.rect = new Rect(0, 0, 1, height);
            if (_laneLabels != null)
                _laneLabels.anchoredPosition = new Vector2(0, ((RectTransform)_canvas.transform).rect.height * height - 10);
        }

        private void RefreshUI()
        {
            if (_canvas == null) return;
            bool ready = _baked != null;
            _playButton.interactable = _reverseButton.interactable = _resetButton.interactable = _timeline.interactable = _hintToggle.interactable = ready;
            if (_error != null) { _statusLabel.text = _error; _statusLabel.color = new Color(1, 0.55f, 0.5f); }
            _playButton.GetComponentInChildren<Text>().text = _playing ? "Pause" : "Play";
            _reverseButton.GetComponentInChildren<Text>().text = _speed >= 0 ? "Direction: forward" : "Direction: reverse";
            _timeLabel.text = $"TIME  {_time:F4}   /   {_phase * 100:F1}%";
            _timeline.SetValueWithoutNotify(_phase);
            _hintToggle.SetIsOnWithoutNotify(_useHint);
            if (!ready) return;
            _unityLabel.text = _unityValue.ToString("F6");
            _rationalLabel.text = _rationalValue.ToString("F6");
            _errorLabel.text = Math.Abs(_rationalValue - _unityValue).ToString("E2");
            _maximumLabel.text = _maximumError.ToString("E2");
            _strategyLabel.text = $"{_baked.LookupMode} -> managed {_baked.ManagedLookup} / Burst {_baked.BurstLookup}   |   {_baked.SegmentCount} segments   |   hint {(_useHint ? _intervalHint.ToString() : "off")}";
            float scaled = (float)Math.Max(-1, Math.Min(1, (_rationalValue - _unityValue) * 1000));
            _errorFill.anchorMin = new Vector2(scaled < 0 ? 0.5f + scaled * 0.5f : 0.5f, 0);
            _errorFill.anchorMax = new Vector2(scaled < 0 ? 0.5f : 0.5f + scaled * 0.5f, 1);
            _errorFill.offsetMin = _errorFill.offsetMax = Vector2.zero;
        }
    }
}

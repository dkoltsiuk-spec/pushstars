using System;
using PushStars.CV;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Pre-match calibration screen (phase 08). Binds an <see cref="IPoseSource"/>'s tracking quality
    /// to a clear status indicator — «ТРЕКИНГ ОК / СЛАБЫЙ ТРЕКИНГ / СКЕЛЕТ НЕ НАЙДЕН» — with a short
    /// instruction telling the player how to fix a bad setup (light, distance, full body in frame).
    /// The "Начать" button only becomes interactable once tracking is stable, satisfying the
    /// acceptance criterion that a lost skeleton always explains the cause.
    ///
    /// The pose source is any MonoBehaviour implementing <see cref="IPoseSource"/> — drop a
    /// <c>MockPoseSource</c> here to exercise the screen in the editor, or a <c>MediaPipePoseSource</c>
    /// once the plugin is installed. An optional camera-preview RawImage shows the live feed.
    /// </summary>
    public class CalibrationScreen : MonoBehaviour
    {
        [Header("Pose source (must implement IPoseSource)")]
        [SerializeField] private MonoBehaviour _poseSourceBehaviour;

        [Header("Status")]
        [SerializeField] private TextMeshProUGUI _statusText;
        [SerializeField] private Image           _statusDot;
        [SerializeField] private TextMeshProUGUI _hintText;

        [Header("Optional")]
        [SerializeField] private RawImage _cameraPreview; // assign a backend-provided feed if available
        [SerializeField] private Button   _startButton;   // interactable only when tracking is good

        /// <summary>Raised when the player taps «Начать» with tracking ready.</summary>
        public event Action OnStartRequested;

        private static readonly Color Green  = new Color32( 107, 255,  74, 255);
        private static readonly Color Yellow = new Color32( 245, 200,  66, 255);
        private static readonly Color Red    = new Color32( 255,  60,  90, 255);
        private static readonly Color Gray   = new Color32( 136, 136, 170, 255);

        private IPoseSource _source;

        private void Awake()
        {
            _source = _poseSourceBehaviour as IPoseSource;
            if (_source == null && _poseSourceBehaviour != null)
                Debug.LogError($"[Calibration] {_poseSourceBehaviour.GetType().Name} does not implement IPoseSource.");

            if (_startButton != null) _startButton.onClick.AddListener(HandleStartClicked);
        }

        private void OnEnable()
        {
            if (_source == null) { Render(TrackingQuality.None); return; }

            _source.OnQualityChanged += Render;
            _source.StartTracking();
            Render(_source.Quality);
        }

        private void OnDisable()
        {
            if (_source != null) _source.OnQualityChanged -= Render;
        }

        private void OnDestroy()
        {
            if (_startButton != null) _startButton.onClick.RemoveListener(HandleStartClicked);
        }

        private void HandleStartClicked()
        {
            if (_source != null && _source.Quality == TrackingQuality.Good)
                OnStartRequested?.Invoke();
        }

        private void Render(TrackingQuality quality)
        {
            string status, hint;
            Color color;

            switch (quality)
            {
                case TrackingQuality.Good:
                    status = "ТРЕКИНГ ОК"; color = Green;
                    hint   = "Отлично видно — можно начинать.";
                    break;
                case TrackingQuality.LowVisibility:
                    status = "СЛАБЫЙ ТРЕКИНГ"; color = Yellow;
                    hint   = "Добавьте света и убедитесь, что тело целиком в кадре.";
                    break;
                case TrackingQuality.Lost:
                    status = "СКЕЛЕТ НЕ НАЙДЕН"; color = Red;
                    hint   = "Отойдите от камеры так, чтобы в кадр попало всё тело.";
                    break;
                default:
                    status = "ИНИЦИАЛИЗАЦИЯ…"; color = Gray;
                    hint   = "Наводим камеру…";
                    break;
            }

            if (_statusText != null) { _statusText.text = status; _statusText.color = color; }
            if (_statusDot  != null) _statusDot.color = color;
            if (_hintText   != null) _hintText.text = hint;
            if (_startButton != null) _startButton.interactable = quality == TrackingQuality.Good;
        }
    }
}

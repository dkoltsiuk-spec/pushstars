using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    public sealed partial class FightHud
    {
        private RawImage _soloPortrait;
        private AspectRatioFitter _soloPortraitAspect;
        private bool _originalAspectEnabled, _capturedAspect, _soloPaused;
        private AspectRatioFitter.AspectMode _originalAspectMode;
        private Image _depthMarker;
        private float _displayedDepth;

        private const float DepthTrackHeight = 220f;
        private static readonly Vector2 DepthTrackSize = new Vector2(27f, DepthTrackHeight);
        private static readonly Vector2 DepthMarkerSize = new Vector2(46f, 26f);

        public RawImage SoloPortrait => _soloPortrait;

        /// <summary>The live onboarding portrait reaches this rect before the HUD takes ownership.</summary>
        public void AdoptOnboardingPortrait(RawImage portrait)
        {
            if (portrait == null || _soloPortrait == null || portrait == _soloPortrait) return;
            var target = _soloPortrait.rectTransform;
            var rect = portrait.rectTransform;
            rect.SetParent(target.parent, false);
            rect.anchorMin = target.anchorMin;
            rect.anchorMax = target.anchorMax;
            rect.pivot = target.pivot;
            rect.sizeDelta = target.sizeDelta;
            rect.anchoredPosition = target.anchoredPosition;
            rect.localScale = target.localScale;
            rect.localRotation = target.localRotation;
            _soloPortrait.gameObject.SetActive(false);
            _soloPortrait = portrait;
            _soloPortraitAspect = portrait.GetComponent<AspectRatioFitter>();
            if (_soloPortraitAspect == null) _soloPortraitAspect = portrait.gameObject.AddComponent<AspectRatioFitter>();
            _soloPortraitAspect.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            _soloPortraitAspect.aspectRatio = (float)portrait.texture.width / portrait.texture.height;
        }

        private void ConfigureSoloPresentation()
        {
            if (_playerHalf != null)
            {
                _soloPortrait = _playerHalf.GetComponentInChildren<RawImage>(true);
                if (_soloPortrait != null)
                {
                    // Shared framing for measurement and training. Absolute values avoid
                    // accumulating the enlargement when a new training set starts.
                    _soloPortrait.rectTransform.localScale = Vector3.one * 2.145f;
                    _soloPortrait.rectTransform.anchoredPosition = new Vector2(0f, 110f);
                    _soloPortraitAspect = _soloPortrait.GetComponent<AspectRatioFitter>();
                    if (!_capturedAspect)
                    {
                        _capturedAspect = true;
                        _originalAspectEnabled = _soloPortraitAspect != null && _soloPortraitAspect.enabled;
                        _originalAspectMode = _soloPortraitAspect != null
                            ? _soloPortraitAspect.aspectMode : AspectRatioFitter.AspectMode.None;
                    }
                    if (_soloPortraitAspect == null)
                        _soloPortraitAspect = _soloPortrait.gameObject.AddComponent<AspectRatioFitter>();
                    _soloPortraitAspect.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
                    _soloPortraitAspect.enabled = true;
                }
            }
            if (_depthMarker == null && _soloPanel != null)
            {
                var track = DepthImage(_soloPanel.transform, "PushupDepth", Color.white);
                track.sprite = Resources.Load<Sprite>("PushupDepthTrack");
                track.preserveAspect = true;
                track.rectTransform.anchorMin = track.rectTransform.anchorMax = new Vector2(1f, .42f);
                track.rectTransform.anchoredPosition = new Vector2(-24f, 0f);
                track.rectTransform.sizeDelta = DepthTrackSize;
                // Pause curtain and labels must render over the gauge.
                track.transform.SetAsFirstSibling();
                _depthMarker = DepthImage(track.transform, "DepthMarker", Color.white);
                _depthMarker.sprite = Resources.Load<Sprite>("PushupDepthMarker");
                _depthMarker.preserveAspect = true;
                _depthMarker.rectTransform.sizeDelta = DepthMarkerSize;
            }
            _displayedDepth = 0f;
            if (_depthMarker != null)
            {
                _depthMarker.rectTransform.anchorMin = _depthMarker.rectTransform.anchorMax = new Vector2(.5f, 1f);
                _depthMarker.rectTransform.anchoredPosition = Vector2.zero;
            }
            UpdateSoloPresentation();
        }

        private void RestoreSoloPortraitAspect()
        {
            if (_soloPortraitAspect == null) return;
            _soloPortraitAspect.aspectMode = _originalAspectMode;
            _soloPortraitAspect.enabled = _originalAspectEnabled;
        }

        private void UpdateSoloPresentation()
        {
            if (!_solo) return;
            if (_soloPortrait != null && _soloPortraitAspect != null && _soloPortrait.texture != null)
                _soloPortraitAspect.aspectRatio = (float)_soloPortrait.texture.width / _soloPortrait.texture.height;
            if (_depthMarker == null || _soloPaused || _session == null) return;
            var tracker = _session.Tracker;
            bool valid = _session.isActiveAndEnabled && tracker.SignalValid;
            if (valid)
                _displayedDepth = Mathf.Lerp(_displayedDepth, Mathf.Clamp01(tracker.CurrentDepth01),
                    1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
            _depthMarker.rectTransform.anchorMin = _depthMarker.rectTransform.anchorMax =
                new Vector2(.5f, 1f - _displayedDepth);
            _depthMarker.rectTransform.anchoredPosition = Vector2.zero;
            _depthMarker.color = valid ? Color.white : new Color32(145, 151, 170, 255);
        }

        private static Image DepthImage(Transform parent, string name, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            obj.layer = parent.gameObject.layer;
            obj.transform.SetParent(parent, false);
            var image = obj.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
    }
}

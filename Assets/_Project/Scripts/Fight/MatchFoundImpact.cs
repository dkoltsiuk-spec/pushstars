using PushStars.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>One entrance per found opponent, with all impact effects on the same clock.</summary>
    [DefaultExecutionOrder(450)]
    [DisallowMultipleComponent]
    public sealed class MatchFoundImpact : MonoBehaviour
    {
        private const float LeadIn = 0.08f;
        private const float DropTime = 0.30f;
        private const float SettleTime = 0.32f;
        private const float CrackTime = 0.12f;
        private const float ShakeTime = 0.22f;

        private RectTransform _screen;
        private RectTransform _medal;
        private Image _original;
        private Image _crown;
        private Image _crack;
        private RectTransform _reveal;
        private Vector3 _screenPosition;
        private Vector2 _crackSize;
        private float _elapsed;
        private bool _playing;
        private bool _impacted;
        private bool _originalEnabled;
        private int _medalSiblingIndex;

        public void Play()
        {
            Cancel();
            if (!EnsureArtwork()) return;
            _screenPosition = _screen.localPosition;
            _originalEnabled = _original.enabled;
            _medalSiblingIndex = _medal.GetSiblingIndex();
            _medal.SetAsLastSibling();
            _original.enabled = false;
            _crown.gameObject.SetActive(true);
            _reveal.gameObject.SetActive(true);
            _elapsed = 0f;
            _impacted = false;
            _playing = true;
            ApplyFrame(0f);
        }

        private bool EnsureArtwork()
        {
            if (_crown != null) return true;
            _screen = transform as RectTransform;
            foreach (var candidate in GetComponentsInChildren<RectTransform>(true))
                if (candidate.name == "VsMedal") { _medal = candidate; break; }
            if (_screen == null || _medal == null) return false;
            _original = _medal.GetComponent<Image>();
            var crown = Resources.Load<Sprite>("MatchFound/VsCrown");
            var crack = Resources.Load<Sprite>("MatchFound/ImpactCrack");
            if (_original == null || crown == null || crack == null) return false;

            // Keep the authored medal's bounds/pivot intact for saved layouts. Only its art moves.
            _reveal = new GameObject("ImpactCrackReveal", typeof(RectTransform), typeof(RectMask2D))
                .GetComponent<RectTransform>();
            _reveal.SetParent(_medal, false);
            Centre(_reveal);
            _reveal.gameObject.layer = _medal.gameObject.layer;
            _crack = Artwork("ImpactCrack", _reveal, crack);
            _crown = Artwork("FallingVsCrown", _medal, crown);
            return true;
        }

        private static Image Artwork(string name, RectTransform parent, Sprite sprite)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
                .GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.gameObject.layer = parent.gameObject.layer;
            Centre(image.rectTransform);
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static void Centre(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
        }

        private void LateUpdate()
        {
            if (!_playing) return;
            _elapsed += Time.unscaledDeltaTime;
            if (!_impacted && _elapsed >= LeadIn + DropTime)
            {
                _impacted = true;
                Haptics.Medium();
            }
            ApplyFrame(_elapsed);
            if (_elapsed >= LeadIn + DropTime + SettleTime)
            {
                _screen.localPosition = _screenPosition;
                _playing = false;
            }
        }

        private void ApplyFrame(float elapsed)
        {
            float unit = Mathf.Min(_medal.rect.width, _medal.rect.height);
            _crown.rectTransform.sizeDelta = new Vector2(unit * 1.45f, unit * 1.61f);
            _crackSize = new Vector2(unit * 2.5f, unit * 2.5f * _crack.sprite.rect.height / _crack.sprite.rect.width);
            _crack.rectTransform.sizeDelta = _crackSize;
            float impact = elapsed - LeadIn - DropTime;
            if (impact < 0f)
            {
                float drop = Mathf.Clamp01((elapsed - LeadIn) / DropTime);
                float eased = drop * drop * drop;
                float height = Mathf.Max(unit * 3.5f, _screen.rect.height * 0.62f);
                _crown.rectTransform.anchoredPosition = Vector2.up * height * (1f - eased);
                _crown.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.16f, 1f, eased);
                _crown.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-12f, 0f, eased));
                _crown.color = new Color(1f, 1f, 1f, Mathf.Clamp01(drop * 6f));
                _reveal.sizeDelta = Vector2.zero;
                _crack.color = Color.clear;
                return;
            }

            _crown.color = Color.white;
            float settle = Mathf.Clamp01(impact / SettleTime);
            float bounce = Mathf.Sin(settle * Mathf.PI) * (1f - settle);
            float squash = Mathf.Sin(Mathf.Clamp01(impact / 0.085f) * Mathf.PI) * 0.11f;
            _crown.rectTransform.anchoredPosition = Vector2.up * (unit * 0.18f * bounce);
            _crown.rectTransform.localScale = new Vector3(1f + squash, 1f - squash, 1f);
            _crown.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(settle * Mathf.PI * 2f) * (1f - settle) * 3f);

            // Reveal the stationary cracks outwards from the point of contact.
            float reveal = Mathf.Clamp01(impact / CrackTime);
            _reveal.sizeDelta = _crackSize * (1f - Mathf.Pow(1f - reveal, 3f));
            _crack.color = new Color(1f, 1f, 1f, Mathf.Clamp01(impact / 0.035f));
            float shake = Mathf.Clamp01(1f - impact / ShakeTime);
            float amplitude = Mathf.Min(_screen.rect.width, _screen.rect.height) * 0.008f * shake * shake;
            _screen.localPosition = _screenPosition + new Vector3(
                Mathf.Sin(impact * 113f + 1f) * amplitude,
                Mathf.Cos(impact * 137f) * amplitude * 0.7f, 0f);
        }

        public void Cancel()
        {
            if (_playing && _screen != null) _screen.localPosition = _screenPosition;
            _playing = false;
            if (_crown != null) _crown.gameObject.SetActive(false);
            if (_reveal != null) _reveal.gameObject.SetActive(false);
            if (_original != null) _original.enabled = _originalEnabled;
            if (_crown != null && _medal != null) _medal.SetSiblingIndex(_medalSiblingIndex);
        }

        private void OnDisable() => Cancel();
        private void OnDestroy() => Cancel();
    }
}

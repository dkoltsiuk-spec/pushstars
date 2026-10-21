using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// "PLAYER is looking for a duel — FIGHT": shown on the main screen when another player's
    /// search calls this one while the app is open (with the app closed, the push does this job).
    /// Slides in under the HUD and goes away by itself once the caller has stopped waiting.
    /// </summary>
    public sealed class CallBanner : MonoBehaviour
    {
        private const float ShowSec = 30f, SlideSec = .3f;
        private static readonly Color Plate = new Color32(31, 34, 41, 245);
        private static readonly Color Gold = new Color32(255, 210, 31, 255);

        private RectTransform _rt;
        private CanvasGroup _group;
        private TextMeshProUGUI _label;
        private float _shownAt = -1f;

        public static CallBanner Create(RectTransform parent, TMP_FontAsset font, Action fight)
        {
            var rt = new GameObject("CallBanner", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, 1f);
            rt.sizeDelta = new Vector2(350f, 66f);
            var banner = rt.gameObject.AddComponent<CallBanner>();
            banner._rt = rt;
            banner._group = rt.gameObject.AddComponent<CanvasGroup>();
            rt.gameObject.AddComponent<DuelActionPlate>().color = Plate;

            font = font != null ? font : TMP_Settings.defaultFontAsset;
            banner._label = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            var label = banner._label;
            label.transform.SetParent(rt, false);
            label.font = font;
            label.fontSize = 15f;
            label.fontStyle = FontStyles.Bold;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            var lr = label.rectTransform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(18f, 6f); lr.offsetMax = new Vector2(-124f, -6f);

            var button = new GameObject("Fight", typeof(RectTransform)).GetComponent<RectTransform>();
            button.SetParent(rt, false);
            button.anchorMin = button.anchorMax = new Vector2(1f, .5f);
            button.anchoredPosition = new Vector2(-62f, 0f);
            button.sizeDelta = new Vector2(104f, 48f);
            var face = button.gameObject.AddComponent<DuelActionPlate>();
            face.color = Gold;
            var tap = button.gameObject.AddComponent<Button>();
            tap.targetGraphic = face;
            tap.onClick.AddListener(() => { banner.Hide(); fight?.Invoke(); });
            var text = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(button, false);
            text.font = font;
            text.fontSize = 20f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.text = "FIGHT";
            var tr = text.rectTransform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;

            rt.gameObject.SetActive(false);
            return banner;
        }

        public void Show(string text)
        {
            _label.text = text;
            _shownAt = Time.unscaledTime;
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            Place();
        }

        public void Hide()
        {
            _shownAt = -1f;
            if (this != null) gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_shownAt < 0f) return;
            if (Time.unscaledTime - _shownAt > ShowSec) { Hide(); return; }
            Place();
        }

        private void Place()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - _shownAt) / SlideSec);
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            _group.alpha = ease;
            _rt.anchoredPosition = new Vector2(0f, -128f + 40f * (1f - ease));
        }
    }
}

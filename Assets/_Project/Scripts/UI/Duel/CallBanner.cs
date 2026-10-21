using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// "● DENIS ищет дуэль [БОЙ]": a callout just above the main screen's START when another
    /// player's search calls this one while the app is open (with the app closed, the push does
    /// this job). It pops in, breathes gently while the caller waits and
    /// goes away by itself once they have stopped waiting. The whole callout is the button.
    /// </summary>
    public sealed class CallBanner : MonoBehaviour
    {
        private const float ShowSec = 30f, PopSec = .28f;
        private static readonly Color Plate = new Color32(24, 27, 36, 245);
        private static readonly Color Gold = new Color32(255, 210, 31, 255);
        private static readonly Color Live = new Color32(82, 255, 2, 255);

        /// <summary>True while a callout is up: the duel-hour badge in the same place steps aside.</summary>
        public static bool Showing { get; private set; }

        private RectTransform _rt;
        private CanvasGroup _group;
        private TextMeshProUGUI _label;
        private Image _dot;
        private float _shownAt = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Showing = false;

        /// <param name="button">START: the callout sits just above it.</param>
        public static CallBanner Create(RectTransform button, TMP_FontAsset font, Action fight)
        {
            var rt = new GameObject("CallBubble", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(button, false);
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, 1f);
            rt.pivot = new Vector2(.5f, 0f);
            rt.sizeDelta = new Vector2(300f, 56f);
            var banner = rt.gameObject.AddComponent<CallBanner>();
            banner._rt = rt;
            banner._group = rt.gameObject.AddComponent<CanvasGroup>();
            font = font != null ? font : TMP_Settings.defaultFontAsset;
            var theme = Resources.Load<PushStarsTheme>("PushStarsTheme");

            var plate = new GameObject("Plate", typeof(RectTransform)).AddComponent<DuelActionPlate>();
            plate.rectTransform.SetParent(rt, false);
            Fill(plate.rectTransform);
            plate.color = Plate;
            var tap = rt.gameObject.AddComponent<Button>();
            tap.targetGraphic = plate;
            tap.onClick.AddListener(() => { banner.Hide(); fight?.Invoke(); });

            banner._dot = new GameObject("Live", typeof(RectTransform)).AddComponent<Image>();
            var dot = banner._dot.rectTransform;
            dot.SetParent(rt, false);
            dot.anchorMin = dot.anchorMax = new Vector2(0f, .5f);
            dot.anchoredPosition = new Vector2(22f, 1f);
            dot.sizeDelta = new Vector2(10f, 10f);
            banner._dot.sprite = theme != null ? theme.CircleShape : null;
            banner._dot.color = Live;
            banner._dot.raycastTarget = false;

            banner._label = Text(rt, font, 15f, Color.white, TextAlignmentOptions.MidlineLeft);
            // "Name" keeps InterfaceLocalization off it: the caller's name must never be translated
            // (a trailing "S" would read as the seconds unit). The caller localizes the sentence.
            banner._label.name = "Name";
            var lr = banner._label.rectTransform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(36f, 4f); lr.offsetMax = new Vector2(-98f, -4f);
            banner._label.enableAutoSizing = true;
            banner._label.fontSizeMin = 12f;
            banner._label.fontSizeMax = 15f;

            var chip = new GameObject("Fight", typeof(RectTransform)).GetComponent<RectTransform>();
            chip.SetParent(rt, false);
            chip.anchorMin = chip.anchorMax = new Vector2(1f, .5f);
            chip.anchoredPosition = new Vector2(-50f, 1f);
            chip.sizeDelta = new Vector2(86f, 40f);
            var face = chip.gameObject.AddComponent<DuelActionPlate>();
            face.color = Gold;
            face.raycastTarget = false;
            var fightLabel = Text(chip, font, 19f, Color.white, TextAlignmentOptions.Center);
            fightLabel.fontSharedMaterial = font.material; // outlined, like START
            Fill(fightLabel.rectTransform);
            fightLabel.text = "FIGHT";

            rt.gameObject.SetActive(false);
            return banner;
        }

        /// <param name="text">Already localized, with the caller's name in it.</param>
        public void Show(string text)
        {
            _label.text = text;
            _shownAt = Time.unscaledTime;
            Showing = true;
            gameObject.SetActive(true);
            Place();
        }

        public void Hide()
        {
            _shownAt = -1f;
            Showing = false;
            if (this != null) gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_shownAt < 0f) return;
            if (Time.unscaledTime - _shownAt > ShowSec) { Hide(); return; }
            Place();
        }

        private void OnDestroy() { if (_shownAt >= 0f) Showing = false; }

        // Pops up from START with a slight overshoot, then floats a couple of pixels; the live
        // dot pulses — someone is really there, waiting.
        private void Place()
        {
            float age = Time.unscaledTime - _shownAt;
            float t = Mathf.Clamp01(age / PopSec);
            float pop = 1f + 2.2f * Mathf.Pow(t - 1f, 3f) + 1.2f * Mathf.Pow(t - 1f, 2f);
            _group.alpha = t;
            _rt.localScale = Vector3.one * Mathf.LerpUnclamped(.8f, 1f, pop);
            _rt.anchoredPosition = new Vector2(0f, 14f + 2f * Mathf.Sin(age * 2.4f));
            var c = _dot.color;
            c.a = .55f + .45f * (.5f + .5f * Mathf.Sin(age * 5f));
            _dot.color = c;
        }

        private static TextMeshProUGUI Text(Transform parent, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
        {
            var label = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false);
            label.font = font;
            label.fontSize = size;
            label.fontStyle = FontStyles.Bold;
            label.color = color;
            label.alignment = align;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        private static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}

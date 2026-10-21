using System;
using PushStars.Core;
using TMPro;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// "DUEL HOUR · 42:10" just above the main screen's START while the duel hour runs
    /// (<see cref="DuelHour"/>) and the duel mode is selected — the hour the daily reminder
    /// gathers players for. Hidden the rest of the day, and while a call's callout takes the spot.
    /// </summary>
    public sealed class DuelHourBadge : MonoBehaviour
    {
        private static readonly Color Plate = new Color32(20, 22, 30, 225);
        private static readonly Color Gold = new Color32(255, 210, 31, 255);

        private TextMeshProUGUI _label;
        private float _nextTick;

        public static DuelHourBadge Attach(RectTransform button)
        {
            if (button == null) return null;
            var font = button.GetComponentInChildren<TextMeshProUGUI>(true)?.font ?? TMP_Settings.defaultFontAsset;
            var host = new GameObject("DuelHourBadge", typeof(RectTransform)).GetComponent<RectTransform>();
            host.SetParent(button, false);
            host.anchorMin = host.anchorMax = new Vector2(.5f, 1f);
            host.pivot = new Vector2(.5f, 0f);
            host.anchoredPosition = new Vector2(0f, 10f);
            host.sizeDelta = new Vector2(210f, 32f);
            var badge = host.gameObject.AddComponent<DuelHourBadge>();
            var plate = host.gameObject.AddComponent<DuelActionPlate>();
            plate.color = Plate;
            plate.raycastTarget = false;

            var label = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(host, false);
            label.font = font;
            label.fontSize = 15f;
            label.fontStyle = FontStyles.Bold;
            label.color = Gold;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            var lr = label.rectTransform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            badge._label = label;
            badge.Refresh();
            return badge;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextTick) return;
            Refresh();
        }

        private void Refresh()
        {
            _nextTick = Time.unscaledTime + 1f;
            var now = DateTime.Now;
            bool on = DuelHour.IsOn(now) && SelectedGameMode.Current == GameMode.Pvp && !CallBanner.Showing;
            var plate = GetComponent<DuelActionPlate>();
            if (plate != null) plate.enabled = on;
            _label.enabled = on;
            if (!on) return;
            var left = DuelHour.Left(now);
            _label.text = Localization.Text($"DUEL HOUR · {(int)left.TotalMinutes}:{left.Seconds:00}");
        }
    }
}

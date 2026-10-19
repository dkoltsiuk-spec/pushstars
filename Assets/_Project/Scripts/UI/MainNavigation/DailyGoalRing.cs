using PushStars.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Thin progress ring for the daily goal around the HUD streak flame. The flame is dimmed until
    /// today has a workout (the streak is at risk), the ring fills with today's push-ups and closes
    /// when the goal is done, with one pulse. Also collects daily Aura that no fight result paid —
    /// a goal finished in training — so it lands on Home. Built at runtime, the scene is untouched.
    /// </summary>
    public sealed class DailyGoalRing : MonoBehaviour
    {
        // A round badge on the pill's left end: dark disc, thin ring on its rim, the flame inside.
        private const float BadgeSize = 30f, FlameSize = 21f, PulseSeconds = .35f, PollSeconds = 2f;
        /// <summary>The badge sits this much right of the original icon, tucked onto the pill (owner's call).</summary>
        private const float BadgeNudgeX = 2f;
        private static readonly Color Disc = new Color32(16, 16, 24, 240);
        private static readonly Color Track = new Color(1f, 1f, 1f, .16f);
        private static readonly Color DimFlame = new Color(.55f, .55f, .6f, .9f);
        private static Sprite _ringSprite, _discSprite;

        private Image _fill, _icon;
        private RectTransform _pulseRoot;
        private float _nextPoll, _pulseStart = -1f;
        private bool _wasDone, _polled;

        public static DailyGoalRing Install(RectTransform panel)
        {
            var pill = panel != null ? FindDeep(panel, "StreakPill") : null;
            var icon = pill != null ? pill.Find("Icon") : null;
            if (icon == null) return null;
            var existing = pill.GetComponent<DailyGoalRing>();
            if (existing != null) return existing;

            var ring = pill.gameObject.AddComponent<DailyGoalRing>();
            ring._icon = icon.GetComponent<Image>();
            var number = pill.Find("Number")?.GetComponent<Graphic>();
            Color accent = number != null ? number.color : new Color(1f, .78f, .2f);

            // Ring and icon pulse together, so both live under one holder centred on the icon.
            var holder = new GameObject("GoalRing", typeof(RectTransform));
            ring._pulseRoot = (RectTransform)holder.transform;
            ring._pulseRoot.SetParent(pill, false);
            var iconRt = (RectTransform)icon;
            ring._pulseRoot.anchorMin = iconRt.anchorMin;
            ring._pulseRoot.anchorMax = iconRt.anchorMax;
            ring._pulseRoot.anchoredPosition = iconRt.anchoredPosition + new Vector2(BadgeNudgeX, 0f);
            ring._pulseRoot.sizeDelta = new Vector2(BadgeSize, BadgeSize);
            ring._pulseRoot.SetSiblingIndex(icon.GetSiblingIndex());

            MakeLayer(ring._pulseRoot, "Disc", Disc, DiscSprite(), false);
            MakeLayer(ring._pulseRoot, "Track", Track, RingSprite(), false);
            ring._fill = MakeLayer(ring._pulseRoot, "Fill", accent, RingSprite(), true);
            icon.SetParent(ring._pulseRoot, false);
            iconRt.anchorMin = iconRt.anchorMax = iconRt.pivot = new Vector2(.5f, .5f);
            iconRt.anchoredPosition = Vector2.zero;
            iconRt.sizeDelta = new Vector2(FlameSize, FlameSize);
            ring.Refresh(false);
            return ring;
        }

        private void OnEnable() => _nextPoll = 0f;

        private void Update()
        {
            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + PollSeconds;
                Refresh(true);
            }
            if (_pulseStart < 0f) return;
            float t = (Time.unscaledTime - _pulseStart) / PulseSeconds;
            float scale = t >= 1f ? 1f : 1f + .28f * Mathf.Sin(t * Mathf.PI);
            _pulseRoot.localScale = new Vector3(scale, scale, 1f);
            if (t >= 1f) _pulseStart = -1f;
        }

        public void Pulse() => _pulseStart = Time.unscaledTime;

        private void Refresh(bool collect)
        {
            DailyGoalStatus goal;
            int streak;
            try { goal = LocalProfile.TodayGoal; streak = LocalProfile.DailyStreak; }
            catch (System.Exception exception) { Debug.LogException(exception, this); return; }

            _fill.fillAmount = goal.Fraction;
            if (_icon != null) _icon.color = goal.Progress > 0 ? Color.white : DimFlame;

            bool pulse = _polled && goal.Done && !_wasDone;
            if (collect)
                try { pulse |= DailyRewards.CollectPending(goal, streak).Change > 0; }
                catch (System.Exception exception) { Debug.LogException(exception, this); }
            if (pulse) Pulse();
            _wasDone = goal.Done;
            _polled = true;
        }

        private static Image MakeLayer(RectTransform parent, string name, Color color, Sprite sprite, bool filled)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
            image.transform.SetParent(parent, false);
            var rt = image.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            if (filled)
            {
                image.type = Image.Type.Filled;
                image.fillMethod = Image.FillMethod.Radial360;
                image.fillOrigin = (int)Image.Origin360.Top;
                image.fillClockwise = true;
                image.fillAmount = 0f;
            }
            return image;
        }

        private static Sprite RingSprite() => _ringSprite != null ? _ringSprite : (_ringSprite = Circle("Daily goal ring", .085f));
        private static Sprite DiscSprite() => _discSprite != null ? _discSprite : (_discSprite = Circle("Daily goal disc", 1f));

        /// <summary>Anti-aliased white ring of the given thickness (share of the size); 1 = a full disc.</summary>
        private static Sprite Circle(string name, float thickness)
        {
            const int size = 128;
            float outer = size * .5f - 1f, inner = outer - size * thickness;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(size * .5f, size * .5f));
                    float a = Mathf.Clamp01(outer - d + .5f) * Mathf.Clamp01(d - inner + .5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f);
            sprite.name = name;
            return sprite;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }
    }
}

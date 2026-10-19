using PushStars.Core;
using PushStars.CV;
using PushStars.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>
    /// Presentation only: every accepted rep throws its XP ("⚡ +12") up off the player's back, so
    /// the total the result screen adds up is earned in sight, one rep at a time — duel, boss
    /// battle, training and the level test alike.
    ///
    /// <para>The value is <see cref="XpCalculator.XpForRep"/>, the same per-rep math the finish
    /// totals, so the popups never promise more than the result pays. A junk rep (FORM under the
    /// threshold) earns nothing and throws nothing.</para>
    ///
    /// <para>Each screen shows the player's stage in a different picture (the duel half, the
    /// boss portrait, the training copy of the duel texture), so the popups follow the body, not
    /// a screen: the player's <see cref="FightAvatar"/> says where it stands in its render, and
    /// whichever visible image shows that render places it on screen.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RepXpPopups : MonoBehaviour
    {
        public const float Duration = 1.15f;
        private const int PoolSize = 8;
        private const float Rise = 120f;
        /// <summary>How far under the top of the player's picture a popup's centre may climb. The
        /// duel clock sits on that edge, over the seam, and must never be covered.</summary>
        private const float Headroom = 115f;

        private static readonly Color32 GoldTop = new Color32(255, 246, 158, 255);
        private static readonly Color32 GoldBottom = new Color32(255, 184, 28, 255);
        private static readonly Color32 ClapTop = new Color32(255, 226, 120, 255);
        private static readonly Color32 ClapBottom = new Color32(255, 104, 38, 255);

        private sealed class Popup
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Image Icon;
            public TextMeshProUGUI Label;
            public Vector2 Origin;
            public float Drift, Scale, Started = -100f;
            public bool Live;
        }

        private readonly Popup[] _pool = new Popup[PoolSize];
        private PushupSession _session;
        private FightAvatar _avatar;
        private RawImage _image;
        private RectTransform _layer;
        private Sprite _icon;
        private int _next, _shown;

        public void Bind(PushupSession session) { _session = session; _avatar = null; _image = null; }

        /// <summary>One accepted rep at this FORM. <paramref name="clap"/> marks the second rep a
        /// clap push-up is worth: it flies a little bigger and hotter.</summary>
        public void Show(float form, bool clap = false)
        {
            long xp = XpCalculator.XpForRep(form);
            if (xp <= 0 || !ResolveTarget()) return;
            EnsureLayer();
            var popup = _pool[_next];
            _next = (_next + 1) % PoolSize;
            popup.Label.text = "+" + xp;
            popup.Label.colorGradient = clap
                ? new VertexGradient(ClapTop, ClapTop, ClapBottom, ClapBottom)
                : new VertexGradient(GoldTop, GoldTop, GoldBottom, GoldBottom);
            popup.Origin = SpawnPoint();
            // Alternating sideways drift keeps two quick reps from stacking into one blot.
            popup.Drift = (_shown++ % 2 == 0 ? 1f : -1f) * 14f;
            popup.Scale = clap ? 1.25f : 1f;
            popup.Started = Time.unscaledTime;
            popup.Live = true;
            popup.Root.gameObject.SetActive(true);
            popup.Root.SetAsLastSibling();
            _layer.SetAsLastSibling();
            Sample(popup, 0f);
        }

        public void Clear()
        {
            foreach (var popup in _pool)
            {
                if (popup == null) continue;
                popup.Live = false;
                popup.Root.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            foreach (var popup in _pool)
            {
                if (popup == null || !popup.Live) continue;
                float t = Time.unscaledTime - popup.Started;
                if (t >= Duration) { popup.Live = false; popup.Root.gameObject.SetActive(false); continue; }
                Sample(popup, t);
            }
        }

        private static void Sample(Popup popup, float t)
        {
            float k = Mathf.Clamp01(t / Duration);
            // Punch in past full size, settle, then glide up and fade out over the last third.
            float punch = t < .12f ? Mathf.Lerp(.2f, 1.3f, t / .12f) : Mathf.Lerp(1.3f, 1f, Mathf.Clamp01((t - .12f) / .14f));
            // Half linear: a run of quick reps reads as an evenly spaced column, not a pile-up at the top.
            float rise = Mathf.Lerp(k, 1f - (1f - k) * (1f - k), .45f);
            float fade = Mathf.Clamp01((k - .62f) / .38f);
            popup.Root.anchoredPosition = popup.Origin + new Vector2(popup.Drift * rise, Rise * rise);
            popup.Root.localScale = Vector3.one * (popup.Scale * punch);
            popup.Icon.rectTransform.localRotation = Quaternion.Euler(0, 0, 10f * Mathf.Sin(t * 14f) * (1f - k));
            popup.Group.alpha = 1f - fade * fade;
        }

        // ── Where the body is on screen ──────────────────────────────────────────────────────────

        private bool ResolveTarget()
        {
            if (_avatar == null)
                foreach (var avatar in FindObjectsByType<FightAvatar>(FindObjectsSortMode.None))
                    if (avatar.IsDrivenBy(_session)) { _avatar = avatar; break; }
            if (_avatar == null || _avatar.StageCamera == null) return false;
            if (_image != null && _image.isActiveAndEnabled) return true;
            var stage = _avatar.StageCamera.GetComponentInParent<CharacterStage>(true);
            if (stage == null) return false;
            _image = stage.TargetImage != null && stage.TargetImage.isActiveAndEnabled ? stage.TargetImage : null;
            // Training copies the stage's texture into a picture of its own and hides the source.
            if (_image == null && stage.RenderTarget != null)
                foreach (var image in FindObjectsByType<RawImage>(FindObjectsSortMode.None))
                    if (image.isActiveAndEnabled && image.texture == stage.RenderTarget) { _image = image; break; }
            if (_image == null || _image.canvas == null) return false;
            if (_layer != null && _layer.parent != _image.canvas.transform) _layer.SetParent(_image.canvas.transform, false);
            return true;
        }

        /// <summary>Just above the body's back, a little toward its shoulders, in layer units —
        /// kept on screen when the body runs off the side of its picture.</summary>
        private Vector2 SpawnPoint()
        {
            var rect = _image.rectTransform.rect;
            var uv = _image.uvRect;
            Vector2 local;
            if (_avatar.TryGetBodyViewport(out var body) && uv.width > 0f && uv.height > 0f)
            {
                float x = (Mathf.Lerp(body.xMin, body.xMax, .58f) - uv.xMin) / uv.width;
                float y = (body.yMax - uv.yMin) / uv.height;
                local = new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, x), Mathf.Lerp(rect.yMin, rect.yMax, y));
            }
            else local = new Vector2(rect.center.x, Mathf.Lerp(rect.yMin, rect.yMax, .65f));
            Vector2 point = _layer.InverseTransformPoint(_image.rectTransform.TransformPoint(local));
            // The whole climb stays inside the player's own picture: a duel or boss portrait has
            // little room over the head, and a number crossing into the opponent's half reads as
            // theirs. There it starts over the body instead, the way the reference clips do.
            float pictureTop = _layer.InverseTransformPoint(_image.rectTransform.TransformPoint(new Vector2(0f, rect.yMax))).y;
            var bounds = _layer.rect;
            point.x = Mathf.Clamp(point.x, bounds.xMin + 80f, bounds.xMax - 80f);
            point.y = Mathf.Min(point.y + 10f, Mathf.Min(pictureTop, bounds.yMax) - Rise - Headroom);
            point.y = Mathf.Max(point.y, bounds.yMin + 60f);
            return point;
        }

        // ── Artwork ──────────────────────────────────────────────────────────────────────────────

        private void EnsureLayer()
        {
            if (_layer != null) return;
            _layer = new GameObject("RepXpPopups", typeof(RectTransform)).GetComponent<RectTransform>();
            _layer.SetParent(_image.canvas.transform, false);
            _layer.gameObject.layer = _image.gameObject.layer;
            _layer.anchorMin = Vector2.zero; _layer.anchorMax = Vector2.one;
            _layer.offsetMin = _layer.offsetMax = Vector2.zero;
            var group = _layer.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false; group.blocksRaycasts = false;
            var theme = Resources.Load<PushStarsTheme>("PushStarsTheme");
            _icon = theme != null ? theme.IconXP : null;
            for (int i = 0; i < PoolSize; i++) _pool[i] = Build(i);
        }

        private Popup Build(int index)
        {
            var root = Rect("Xp" + index, _layer, new Vector2(160, 60));
            var popup = new Popup { Root = root, Group = root.gameObject.AddComponent<CanvasGroup>() };
            popup.Group.interactable = false; popup.Group.blocksRaycasts = false;

            var row = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter; row.spacing = 2f;
            row.childControlWidth = row.childControlHeight = false;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            popup.Icon = Rect("Icon", root, new Vector2(30, 36)).gameObject.AddComponent<Image>();
            popup.Icon.sprite = _icon; popup.Icon.preserveAspect = true; popup.Icon.raycastTarget = false;
            popup.Icon.enabled = _icon != null;

            popup.Label = Rect("Value", root, new Vector2(110, 60)).gameObject.AddComponent<TextMeshProUGUI>();
            popup.Label.raycastTarget = false;
            FightTypography.Apply(popup.Label, FightTypography.Role.Value);
            popup.Label.fontSize = 44f;
            popup.Label.alignment = TextAlignmentOptions.MidlineLeft;
            popup.Label.textWrappingMode = TextWrappingModes.NoWrap;
            popup.Label.overflowMode = TextOverflowModes.Overflow;
            popup.Label.enableVertexGradient = true;
            popup.Label.color = Color.white;

            root.gameObject.SetActive(false);
            return popup;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.gameObject.layer = parent.gameObject.layer;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.sizeDelta = size;
            return rect;
        }

        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            if (_layer == null) return;
            if (Application.isPlaying) Destroy(_layer.gameObject); else DestroyImmediate(_layer.gameObject);
        }
    }
}

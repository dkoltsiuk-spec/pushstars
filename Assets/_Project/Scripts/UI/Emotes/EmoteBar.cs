using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Clash Royale-style emote button for the pre-fight card and the result screen. The round
    /// face opens a panel of every emote the player owns, each shown as a silhouette of their own
    /// hero in the emote's pose; a tap plays it on the hero and puts the button on a short cooldown.
    ///
    /// <para>Built at runtime over whatever screen hosts it, so no scene has to be rebuilt to carry
    /// it. The opponent is a ghost or a bot, so it answers on its own — a wave for a wave, a laugh
    /// for a taunt — and sometimes shows off an emote the player does not have yet.</para>
    /// </summary>
    public sealed class EmoteBar : MonoBehaviour
    {
        private const float Cooldown = 2.4f;
        private const int Columns = 4;
        private const float TileWidth = 78f, TileHeight = 100f, Gap = 6f, Padding = 12f, ButtonSize = 78f;

        private static readonly Color32 PanelFill = new Color32(16, 28, 78, 245);
        private static readonly Color32 PanelEdge = new Color32(88, 132, 255, 255);
        private static readonly Color32 TileFill = new Color32(45, 91, 227, 255);
        private static readonly Color32 TileFillPressed = new Color32(66, 118, 255, 255);

        private static readonly string[] Taunts = { "laugh", "loser", "threat", "flex" };
        private static readonly string[] Friendly = { "hello", "gg" };

        private Func<Animator> _self, _opponent;
        private Func<GameObject> _model;
        private RectTransform _root, _button, _panel;
        private GameObject _blocker;
        private EmoteCooldownGraphic _cooldown;
        private TMP_FontAsset _font;
        private Material _fontMaterial;
        private float _readyAt, _panelOpenedAt = -1f;
        private bool _opponentAnswers = true;
        private readonly List<(EmoteDef emote, RawImage image)> _tiles = new List<(EmoteDef, RawImage)>();

        public bool IsOpen => _panel != null && _panel.gameObject.activeSelf;

        /// <param name="host">Full-screen rect the button and panel are laid over.</param>
        /// <param name="self">The player's hero Animator (read on every tap — bodies get rebuilt).</param>
        /// <param name="opponent">The opponent's Animator, or null for no answers.</param>
        /// <param name="model">The player's hero model, for the silhouettes.</param>
        /// <param name="corner">Screen corner the button sits in (0..1 anchor).</param>
        /// <param name="offset">Offset from that corner, in host units, towards the centre.</param>
        public static EmoteBar Attach(RectTransform host, Func<Animator> self, Func<Animator> opponent,
            Func<GameObject> model, Vector2 corner, Vector2 offset)
        {
            if (host == null) return null;
            var existing = host.GetComponentInChildren<EmoteBar>(true);
            if (existing != null) Destroy(existing.gameObject);
            var go = new GameObject("EmoteBar", typeof(RectTransform));
            var root = (RectTransform)go.transform;
            root.SetParent(host, false);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            root.SetAsLastSibling();
            var bar = go.AddComponent<EmoteBar>();
            bar._self = self; bar._opponent = opponent; bar._model = model;
            bar.Build(host, corner, offset);
            return bar;
        }

        /// <summary>The opponent plays one emote by itself after <paramref name="delay"/> — a
        /// taunt when it won, a GG when it lost, a wave on the pre-fight card.</summary>
        public void OpponentOpens(float delay, bool? opponentWon)
        {
            string[] pool = opponentWon == true ? Taunts : opponentWon == false ? new[] { "gg", "hello" } : Friendly;
            StartCoroutine(OpponentPlays(delay, pool, 0.2f));
        }

        public bool OpponentAnswers { get => _opponentAnswers; set => _opponentAnswers = value; }

        private void OnEnable() => EmotePlayer.Started += OnEmoteStarted;
        private void OnDisable() => EmotePlayer.Started -= OnEmoteStarted;

        // ── Build ────────────────────────────────────────────────────────────────────────

        private void Build(RectTransform host, Vector2 corner, Vector2 offset)
        {
            _root = (RectTransform)transform;
            var sample = host.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(t => t.font != null);
            if (sample == null) sample = FindAnyObjectByType<TextMeshProUGUI>();
            _font = sample != null ? sample.font : TMP_Settings.defaultFontAsset;
            _fontMaterial = sample != null && sample.font == _font ? sample.fontSharedMaterial : null;

            // Tap-outside closes the panel.
            var blocker = new GameObject("Blocker", typeof(RectTransform), typeof(Image), typeof(Button));
            _blocker = blocker;
            var blockerRect = (RectTransform)blocker.transform;
            blockerRect.SetParent(_root, false);
            blockerRect.anchorMin = Vector2.zero; blockerRect.anchorMax = Vector2.one;
            blockerRect.offsetMin = blockerRect.offsetMax = Vector2.zero;
            blocker.GetComponent<Image>().color = new Color(0f, 0f, 0f, .25f);
            blocker.GetComponent<Button>().transition = Selectable.Transition.None;
            blocker.GetComponent<Button>().onClick.AddListener(Close);
            blocker.SetActive(false);

            _button = NewRect("EmoteButton", _root);
            _button.anchorMin = _button.anchorMax = corner;
            _button.pivot = corner;
            _button.sizeDelta = new Vector2(ButtonSize, ButtonSize);
            _button.anchoredPosition = new Vector2(corner.x < .5f ? offset.x : -offset.x, corner.y < .5f ? offset.y : -offset.y);
            var ring = _button.gameObject.AddComponent<EmoteShape>();
            ring.Radius = ButtonSize; ring.color = new Color32(255, 255, 255, 255);
            var inner = NewRect("Fill", _button);
            Stretch(inner, 4f);
            var innerShape = inner.gameObject.AddComponent<EmoteShape>();
            innerShape.Radius = ButtonSize; innerShape.color = TileFill; innerShape.raycastTarget = false;
            var face = NewRect("Face", _button);
            Stretch(face, 11f);
            face.gameObject.AddComponent<EmoteFaceGraphic>().raycastTarget = false;
            var sweep = NewRect("Cooldown", _button);
            Stretch(sweep, 4f);
            _cooldown = sweep.gameObject.AddComponent<EmoteCooldownGraphic>();
            _cooldown.color = new Color(0f, 0f, .08f, .55f); _cooldown.raycastTarget = false;
            var button = _button.gameObject.AddComponent<Button>();
            button.targetGraphic = ring;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Toggle);
            // Arrives after the screen's own entrance instead of competing with it.
            _button.gameObject.AddComponent<UiTactile>().Reveal(.9f);

            BuildPanel(corner);
        }

        private void BuildPanel(Vector2 corner)
        {
            var owned = EmoteCatalog.Owned();
            int rows = Mathf.Max(1, Mathf.CeilToInt(owned.Length / (float)Columns));
            int columns = Mathf.Clamp(owned.Length, 1, Columns);
            var size = new Vector2(Padding * 2f + columns * TileWidth + (columns - 1) * Gap,
                Padding * 2f + rows * TileHeight + (rows - 1) * Gap);

            _panel = NewRect("EmotePanel", _root);
            // Centred across the screen, just above (or below) the button: four tiles are nearly
            // the full width of a phone, so pinning the panel to the button's corner would clip it.
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(.5f, corner.y);
            _panel.sizeDelta = size;
            _panel.anchoredPosition = new Vector2(0f, _button.anchoredPosition.y + (ButtonSize + 12f) * (corner.y < .5f ? 1f : -1f));
            var edge = _panel.gameObject.AddComponent<EmoteShape>();
            edge.Radius = 20f; edge.color = PanelEdge;
            var fill = NewRect("Fill", _panel); Stretch(fill, 3f);
            var fillShape = fill.gameObject.AddComponent<EmoteShape>();
            fillShape.Radius = 17f; fillShape.color = PanelFill; fillShape.raycastTarget = false;

            _tiles.Clear();
            for (int i = 0; i < owned.Length; i++)
            {
                var emote = owned[i];
                int col = i % Columns, row = i / Columns;
                var tile = NewRect(emote.Id, _panel);
                tile.anchorMin = tile.anchorMax = tile.pivot = new Vector2(0f, 1f);
                tile.sizeDelta = new Vector2(TileWidth, TileHeight);
                tile.anchoredPosition = new Vector2(Padding + col * (TileWidth + Gap), -Padding - row * (TileHeight + Gap));
                var bg = tile.gameObject.AddComponent<EmoteShape>();
                bg.Radius = 12f; bg.color = TileFill;
                var slot = NewRect("SilhouetteSlot", tile);
                slot.anchorMin = new Vector2(0f, 0f); slot.anchorMax = new Vector2(1f, 1f);
                slot.offsetMin = new Vector2(4f, 22f); slot.offsetMax = new Vector2(-4f, -4f);
                var image = NewRect("Silhouette", slot).gameObject.AddComponent<RawImage>();
                image.color = Color.white; image.raycastTarget = false; image.enabled = false;
                var label = NewText(tile, emote.Name, 12.5f);
                var labelRect = label.rectTransform;
                labelRect.anchorMin = new Vector2(0f, 0f); labelRect.anchorMax = new Vector2(1f, 0f);
                labelRect.pivot = new Vector2(.5f, 0f);
                labelRect.offsetMin = new Vector2(2f, 3f); labelRect.offsetMax = new Vector2(-2f, 21f);
                var tileButton = tile.gameObject.AddComponent<Button>();
                tileButton.targetGraphic = bg;
                var colors = tileButton.colors;
                colors.highlightedColor = Color.white; colors.pressedColor = (Color)TileFillPressed * 1.1f;
                tileButton.colors = colors;
                tileButton.onClick.AddListener(() => Choose(emote));
                tile.gameObject.AddComponent<UiTactile>();
                _tiles.Add((emote, image));
            }
            _panel.gameObject.SetActive(false);
        }

        // ── Behaviour ─────────────────────────────────────────────────────────────────────

        private void Toggle()
        {
            if (IsOpen) { Close(); return; }
            if (Time.unscaledTime < _readyAt) return;
            _blocker.SetActive(true);
            _blocker.transform.SetAsLastSibling();
            _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling();
            _button.SetAsLastSibling();
            _panelOpenedAt = Time.unscaledTime;
            FillSilhouettes();
        }

        private void Close()
        {
            if (_panel != null) _panel.gameObject.SetActive(false);
            if (_blocker != null) _blocker.SetActive(false);
        }

        /// <summary>Lazily, on first open: rendering a pose costs a camera render each.</summary>
        private void FillSilhouettes()
        {
            var model = _model?.Invoke();
            if (model == null) return;
            foreach (var (emote, image) in _tiles)
            {
                if (image.texture != null) continue;
                var texture = EmoteThumbnails.Get(model, emote);
                if (texture == null) continue;
                image.texture = texture;
                image.enabled = true;
                FitAspect(image, texture);
            }
        }

        private void Choose(EmoteDef emote)
        {
            Close();
            var player = EmotePlayer.For(_self?.Invoke());
            if (player == null || !player.Play(emote)) return;
            _readyAt = Time.unscaledTime + Mathf.Max(Cooldown, Mathf.Min(emote.Duration, 3.5f));
        }

        private void Update()
        {
            float remaining = _readyAt - Time.unscaledTime;
            _cooldown.Fill = remaining > 0f ? remaining / Cooldown : 0f;
            if (IsOpen)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - _panelOpenedAt) / .22f);
                float s = Mathf.LerpUnclamped(.82f, 1f, UITween.EaseOutBackSoft(t));
                _panel.localScale = new Vector3(s, s, 1f);
            }
        }

        private void OnEmoteStarted(EmotePlayer player, EmoteDef emote)
        {
            if (!_opponentAnswers || player == null || _self == null) return;
            var self = _self();
            if (self == null || player.Animator != self) return;
            if (UnityEngine.Random.value > .6f) return;
            string[] pool = Taunts.Contains(emote.Id) ? Taunts
                : Friendly.Contains(emote.Id) ? Friendly
                : new[] { "laugh", "flex", "hiphop", "snake", "giddyup", "gg" };
            StartCoroutine(OpponentPlays(UnityEngine.Random.Range(1f, 1.8f), pool, .35f));
        }

        /// <summary>The opponent picks from <paramref name="pool"/>, and with
        /// <paramref name="showOff"/> chance from the whole catalog — ghosts own everything, which
        /// is also where players first see the paid moves.</summary>
        private IEnumerator OpponentPlays(float delay, string[] pool, float showOff)
        {
            yield return new WaitForSecondsRealtime(delay);
            var opponent = EmotePlayer.For(_opponent?.Invoke());
            if (opponent == null || opponent.IsPlaying) yield break;
            var candidates = UnityEngine.Random.value < showOff
                ? EmoteCatalog.All.Where(e => e.Clip != null && !e.Free).ToArray()
                : pool.Select(EmoteCatalog.Find).Where(e => e != null && e.Clip != null).ToArray();
            if (candidates.Length == 0) yield break;
            opponent.Play(candidates[UnityEngine.Random.Range(0, candidates.Length)]);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────

        private TextMeshProUGUI NewText(RectTransform parent, string text, float size)
        {
            var rect = NewRect("Label", parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) label.font = _font;
            if (_fontMaterial != null) label.fontSharedMaterial = _fontMaterial;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMin = 8f; label.fontSizeMax = size;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset); rect.offsetMax = new Vector2(-inset, -inset);
        }

        /// <summary>Letterboxes the silhouette inside its slot instead of stretching it.</summary>
        public static void FitAspect(RawImage image, Texture texture)
        {
            var fitter = image.GetComponent<AspectRatioFitter>();
            if (fitter == null) fitter = image.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = (float)texture.width / texture.height;
        }
    }
}

namespace PushStars.UI
{
    /// <summary>Where the emote button sits on each screen (host units, from the named corner).</summary>
    public static class EmoteLayout
    {
        public static readonly Vector2 PreparationCorner = new Vector2(1f, 0f);
        public static readonly Vector2 PreparationOffset = new Vector2(20f, 18f);
        public static readonly Vector2 ResultCorner = new Vector2(0f, 0f);
        public static readonly Vector2 ResultOffset = new Vector2(16f, 18f);
    }
}

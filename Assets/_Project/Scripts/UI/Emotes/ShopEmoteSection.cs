using System.Collections.Generic;
using System.Linq;
using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// The EMOTES section of the shop: a card per emote showing a black silhouette of the player's
    /// own hero in that emote's pose — so it is plain that the shop sells the move, not a hero —
    /// and its gem price. Tapping a card opens a preview where the hero on the home stage performs
    /// it, with the buy button under it.
    ///
    /// <para>Appended at runtime under the authored shop content (below the gem packs), in the
    /// shop's own 390-unit design space, so the authored scene does not change.</para>
    /// </summary>
    public sealed class ShopEmoteSection : MonoBehaviour
    {
        private const float PreviewHeight = 372f, PreviewFeet = -22f;
        private const float CardWidth = 106f, CardHeight = 150f, Column = 122f, Left = 20f, RowGap = 14f;

        private sealed class Card
        {
            public EmoteDef Emote;
            public RawImage Silhouette;
            public TextMeshProUGUI Price;
            public Image Gem;
        }

        private ShopScreen _shop;
        private TMP_FontAsset _font;
        private Material _fontMaterial;
        private Sprite _gemSprite, _buttonSprite;
        private readonly List<Card> _cards = new List<Card>();
        private string _silhouetteHero;

        // Preview dialog
        private GameObject _dialog;
        private TextMeshProUGUI _dialogTitle, _dialogRarity, _dialogAction;
        private RawImage _dialogStage;
        private Image _dialogGem;
        private Button _dialogBuy;
        private EmoteDef _previewing;
        private float _replayAt, _rarityRestoreAt;

        public static ShopEmoteSection Install(ShopScreen shop)
        {
            if (shop == null || shop.Content == null || EmoteCatalog.All.Length == 0) return null;
            var section = shop.gameObject.GetComponent<ShopEmoteSection>();
            if (section == null) section = shop.gameObject.AddComponent<ShopEmoteSection>();
            section.Build(shop);
            return section;
        }

        private void Build(ShopScreen shop)
        {
            _shop = shop;
            var sample = shop.Content.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(t => t.font != null);
            _font = sample != null ? sample.font : TMP_Settings.defaultFontAsset;
            _fontMaterial = sample != null ? sample.fontSharedMaterial : null;
            var gem = shop.Art.Find("GemIcon");
            _gemSprite = gem != null ? gem.GetComponent<Image>()?.sprite : null;
            _buttonSprite = shop.Okay != null ? shop.Okay.image.sprite : null;

            float top = ContentBottom(shop.Content) + 22f;
            var header = Rect("EMOTES", shop.Content);
            Place(header, 23f, top, 344f, 31f);
            header.gameObject.AddComponent<ShopSectionGraphic>().raycastTarget = false;
            var title = Text(header, "EMOTES", 17f, new Color32(255, 224, 0, 255));
            title.alignment = TextAlignmentOptions.Left;
            Place(title.rectTransform, 12f, 1f, 326f, 28f);

            var emotes = EmoteCatalog.All.Where(e => e.Clip != null).OrderBy(e => e.Free).ThenBy(e => e.Price).ToArray();
            float y = top + 44f;
            for (int i = 0; i < emotes.Length; i++)
            {
                int col = i % 3, row = i / 3;
                _cards.Add(BuildCard(emotes[i], Left + col * Column, y + row * (CardHeight + RowGap)));
            }
            int rows = Mathf.CeilToInt(emotes.Length / 3f);
            float bottom = y + rows * (CardHeight + RowGap) + 10f;
            shop.Content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(shop.Content.rect.height, bottom));
            BuildDialog();
        }

        /// <summary>Lowest edge of the authored content (top-left pivots, y grows downwards).</summary>
        private static float ContentBottom(RectTransform content)
        {
            float bottom = 0f;
            foreach (RectTransform child in content)
                if (child.gameObject.activeSelf)
                    bottom = Mathf.Max(bottom, -child.anchoredPosition.y + child.rect.height);
            return bottom;
        }

        private Card BuildCard(EmoteDef emote, float x, float y)
        {
            var root = Rect(emote.Id, _shop.Content);
            Place(root, x, y, CardWidth, CardHeight);
            var surface = Rect("CardSurface", root);
            Stretch(surface);
            var graphic = surface.gameObject.AddComponent<EmoteCardSurface>();
            graphic.Rarity = emote.Rarity;
            var name = Text(root, emote.Name, 14f, Color.white);
            Place(name.rectTransform, 4f, 5f, CardWidth - 8f, 22f);
            var slot = Rect("SilhouetteSlot", root);
            Place(slot, 8f, 27f, CardWidth - 16f, CardHeight - 27f - 32f);
            var silhouette = Rect("Silhouette", slot).gameObject.AddComponent<RawImage>();
            silhouette.color = new Color32(12, 14, 32, 255);
            silhouette.raycastTarget = false;
            silhouette.enabled = false;
            var price = Text(root, "", 16f, Color.white);
            Place(price.rectTransform, 4f, CardHeight - 27f, CardWidth - 8f, 24f);
            var gem = Rect("Gem", root).gameObject.AddComponent<Image>();
            gem.sprite = _gemSprite; gem.preserveAspect = true; gem.raycastTarget = false;
            Place(gem.rectTransform, 0f, CardHeight - 24f, 16f, 18f);
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => OpenPreview(emote));
            root.gameObject.AddComponent<UiTactile>();
            return new Card { Emote = emote, Silhouette = silhouette, Price = price, Gem = gem };
        }

        // ── Refresh ──────────────────────────────────────────────────────────────────────

        /// <summary>Prices/ownership, and the silhouettes when the selected hero changed.</summary>
        public void Refresh()
        {
            foreach (var card in _cards)
            {
                bool owned = EmoteCatalog.Owns(card.Emote);
                string label = card.Emote.Free ? "FREE" : owned ? "OWNED" : card.Emote.Price.ToString();
                card.Price.text = label;
                bool showGem = !owned && !card.Emote.Free && _gemSprite != null;
                card.Gem.enabled = showGem;
                if (showGem)
                {
                    card.Price.ForceMeshUpdate();
                    float width = card.Price.preferredWidth + 20f;
                    float start = (CardWidth - width) * .5f;
                    Place(card.Gem.rectTransform, start, CardHeight - 24f, 16f, 18f);
                    Place(card.Price.rectTransform, start + 20f, CardHeight - 27f, width - 20f, 24f);
                    card.Price.alignment = TextAlignmentOptions.Left;
                }
                else
                {
                    Place(card.Price.rectTransform, 4f, CardHeight - 27f, CardWidth - 8f, 24f);
                    card.Price.alignment = TextAlignmentOptions.Center;
                }
            }
            RefreshSilhouettes();
            if (_dialog != null && _dialog.activeSelf) RefreshDialog();
        }

        private void RefreshSilhouettes()
        {
            var model = HomeModel();
            if (model == null) return;
            string hero = model.name;
            if (hero == _silhouetteHero && _cards.All(c => c.Silhouette.texture != null)) return;
            _silhouetteHero = hero;
            foreach (var card in _cards)
            {
                var texture = EmoteThumbnails.Get(model, card.Emote);
                card.Silhouette.texture = texture;
                card.Silhouette.enabled = texture != null;
                if (texture != null) EmoteBar.FitAspect(card.Silhouette, texture);
            }
        }

        private static CharacterStage HomeStage()
        {
            var roster = FindAnyObjectByType<CharacterRoster>();
            return roster != null ? roster.GetComponentInChildren<CharacterStage>(true) ?? roster.GetComponentInParent<CharacterStage>() : null;
        }

        private static GameObject HomeModel() => EmoteThumbnails.ModelOn(HomeStage());

        // ── Preview dialog ─────────────────────────────────────────────────────────────

        private void BuildDialog()
        {
            var overlay = Rect("EmoteDialog", _shop.Art);
            Stretch(overlay);
            var dim = overlay.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, .02f, .15f, .85f);
            var closeHit = overlay.gameObject.AddComponent<Button>();
            closeHit.transition = Selectable.Transition.None;
            closeHit.onClick.AddListener(ClosePreview);
            _dialog = overlay.gameObject;

            var panel = Rect("Panel", overlay);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
            panel.sizeDelta = new Vector2(332f, 452f);
            var edge = panel.gameObject.AddComponent<EmoteShape>();
            edge.Radius = 18f; edge.color = Color.black;
            var fill = Rect("Fill", panel);
            Stretch(fill, 3f);
            var fillShape = fill.gameObject.AddComponent<EmoteShape>();
            fillShape.Radius = 15f; fillShape.color = new Color32(32, 63, 164, 255);
            panel.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // swallow taps
            panel.gameObject.AddComponent<UiTactile>();

            _dialogTitle = Text(panel, "", 24f, Color.white);
            Place(_dialogTitle.rectTransform, 15f, 16f, 302f, 34f);
            _dialogRarity = Text(panel, "", 14f, Color.white);
            Place(_dialogRarity.rectTransform, 15f, 48f, 302f, 20f);

            var stageSlot = Rect("StageSlot", panel);
            Place(stageSlot, 36f, 72f, 260f, 280f);
            var stageBack = stageSlot.gameObject.AddComponent<EmoteShape>();
            stageBack.Radius = 14f; stageBack.color = new Color32(22, 44, 128, 255); stageBack.raycastTarget = false;
            var stageMask = Rect("Mask", stageSlot);
            Stretch(stageMask);
            stageMask.gameObject.AddComponent<RectMask2D>();
            _dialogStage = Rect("Hero", stageMask).gameObject.AddComponent<RawImage>();
            _dialogStage.raycastTarget = false;

            var buy = Rect("Buy", panel);
            Place(buy, 76f, 370f, 180f, 60f);
            var buyImage = buy.gameObject.AddComponent<Image>();
            buyImage.sprite = _buttonSprite;
            buyImage.color = _buttonSprite != null ? Color.white : new Color32(245, 200, 66, 255);
            _dialogBuy = buy.gameObject.AddComponent<Button>();
            _dialogBuy.targetGraphic = buyImage;
            _dialogBuy.onClick.AddListener(Buy);
            buy.gameObject.AddComponent<UiTactile>();
            _dialogAction = Text(buy, "", 21f, Color.white);
            Place(_dialogAction.rectTransform, 3f, 0f, 174f, 55f);
            _dialogGem = Rect("Gem", buy).gameObject.AddComponent<Image>();
            _dialogGem.sprite = _gemSprite; _dialogGem.preserveAspect = true; _dialogGem.raycastTarget = false;

            _dialog.SetActive(false);
        }

        private void OpenPreview(EmoteDef emote)
        {
            _previewing = emote;
            _dialog.SetActive(true);
            _dialog.transform.SetAsLastSibling();
            _dialog.GetComponentInChildren<UiTactile>()?.Reveal();
            var stage = HomeStage();
            _dialogStage.texture = stage != null ? stage.RenderTarget : null;
            _dialogStage.enabled = _dialogStage.texture != null;
            if (stage != null && stage.RenderTarget != null)
            {
                var uv = stage.DisplayUv;
                _dialogStage.uvRect = uv;
                float aspect = stage.RenderTarget.width * Mathf.Abs(uv.width) / Mathf.Max(1f, stage.RenderTarget.height * Mathf.Abs(uv.height));
                var r = _dialogStage.rectTransform;
                // The home shot leaves headroom above the hero; show it larger than the slot and
                // let the mask crop the empty sky, feet kept just above the slot's bottom edge.
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, 0f);
                r.sizeDelta = new Vector2(PreviewHeight * aspect, PreviewHeight);
                r.anchoredPosition = new Vector2(0f, PreviewFeet);
            }
            RefreshDialog();
            PlayPreview();
        }

        private void RefreshDialog()
        {
            if (_previewing == null) return;
            bool owned = EmoteCatalog.Owns(_previewing);
            _dialogTitle.text = _previewing.Name;
            _dialogRarity.text = _previewing.Rarity.ToString().ToUpperInvariant();
            _dialogRarity.color = EmoteCardSurface.Accent(_previewing.Rarity);
            _dialogAction.text = owned ? (_previewing.Free ? "FREE" : "OWNED") : _previewing.Price.ToString();
            _dialogAction.color = Color.white;
            _dialogBuy.interactable = !owned;
            _dialogGem.enabled = !owned && _gemSprite != null;
            if (_dialogGem.enabled)
            {
                _dialogAction.ForceMeshUpdate();
                float width = _dialogAction.preferredWidth + 30f;
                Place(_dialogGem.rectTransform, (180f - width) * .5f, 15f, 24f, 26f);
                Place(_dialogAction.rectTransform, (180f - width) * .5f + 30f, 0f, width - 30f, 55f);
                _dialogAction.alignment = TextAlignmentOptions.Left;
            }
            else
            {
                Place(_dialogAction.rectTransform, 3f, 0f, 174f, 55f);
                _dialogAction.alignment = TextAlignmentOptions.Center;
            }
        }

        private void PlayPreview()
        {
            var model = HomeModel();
            var animator = model != null ? model.GetComponentInChildren<Animator>() : null;
            var player = EmotePlayer.For(animator);
            if (player != null && _previewing != null && player.Play(_previewing))
                _replayAt = Time.unscaledTime + _previewing.Duration + .8f;
        }

        private void Buy()
        {
            if (_previewing == null || EmoteCatalog.Owns(_previewing)) return;
            if (!EmoteCatalog.TryBuy(_previewing))
            {
                GameAudio.Play(SoundCue.RepRejected);
                StartCoroutine(Shake(_dialogBuy.transform));
                _dialogRarity.text = "NOT ENOUGH GEMS";
                _dialogRarity.color = new Color32(255, 110, 110, 255);
                _rarityRestoreAt = Time.unscaledTime + 1.6f;
                return;
            }
            GameAudio.Play(SoundCue.Purchase);
            _shop.Refresh();
            Refresh();
            PlayPreview();
        }

        private System.Collections.IEnumerator Shake(Transform target)
        {
            var rest = target.localPosition;
            for (float t = 0f; t < .35f; t += Time.unscaledDeltaTime)
            {
                target.localPosition = rest + Vector3.right * Mathf.Sin(t * 60f) * 8f * (1f - t / .35f);
                yield return null;
            }
            target.localPosition = rest;
        }

        public void ClosePreview()
        {
            if (_dialog != null) _dialog.SetActive(false);
            var player = HomeModel()?.GetComponentInChildren<EmotePlayer>();
            if (player != null) player.Stop();
            _previewing = null;
        }

        private void Update()
        {
            if (_previewing == null || !_dialog.activeSelf) return;
            if (!_shop.IsOpen) { ClosePreview(); return; }
            if (_rarityRestoreAt > 0f && Time.unscaledTime >= _rarityRestoreAt) { _rarityRestoreAt = 0f; RefreshDialog(); }
            if (_replayAt > 0f && Time.unscaledTime >= _replayAt) PlayPreview();
        }

        // ── Helpers (the shop's top-left, y-down placement) ─────────────────────────────

        private TextMeshProUGUI Text(RectTransform parent, string value, float size, Color color)
        {
            var text = Rect("Label", parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) text.font = _font;
            if (_fontMaterial != null) text.fontSharedMaterial = _fontMaterial;
            text.text = value; text.fontSize = size; text.fontStyle = FontStyles.Bold; text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = true; text.fontSizeMin = 9f; text.fontSizeMax = size;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset); rect.offsetMax = new Vector2(-inset, -inset);
        }
    }

    /// <summary>Emote card: dark keyline, rarity-coloured face, dark price band — the gem-pack
    /// card's construction in the emote's rarity colour.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EmoteCardSurface : MaskableGraphic
    {
        private EmoteRarity _rarity;

        public EmoteRarity Rarity { get => _rarity; set { _rarity = value; SetVerticesDirty(); } }

        public static Color32 Accent(EmoteRarity rarity) => rarity switch
        {
            EmoteRarity.Rare => new Color32(92, 214, 255, 255),
            EmoteRarity.Epic => new Color32(214, 132, 255, 255),
            EmoteRarity.Legendary => new Color32(255, 214, 64, 255),
            _ => new Color32(200, 214, 240, 255),
        };

        private static (Color32 center, Color32 edge, Color32 band) Palette(EmoteRarity rarity) => rarity switch
        {
            EmoteRarity.Rare => (new Color32(120, 222, 255, 255), new Color32(40, 140, 236, 255), new Color32(18, 70, 150, 255)),
            EmoteRarity.Epic => (new Color32(222, 150, 255, 255), new Color32(140, 64, 222, 255), new Color32(78, 28, 140, 255)),
            EmoteRarity.Legendary => (new Color32(255, 232, 110, 255), new Color32(246, 160, 24, 255), new Color32(150, 84, 10, 255)),
            _ => (new Color32(214, 226, 246, 255), new Color32(146, 166, 206, 255), new Color32(62, 78, 122, 255)),
        };

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            float unit = r.width / 106f;
            var (center, edge, band) = Palette(_rarity);
            var ink = new Color32(4, 10, 24, 255);
            Rounded(vh, r, 8 * unit, false, ink, ink);
            var inner = new Rect(r.x + 2 * unit, r.y + 5 * unit, r.width - 4 * unit, r.height - 7 * unit);
            Rounded(vh, inner, 6 * unit, false, center, edge);
            var top = new Rect(inner.x, inner.yMax - 24 * unit, inner.width, 24 * unit);
            Rounded(vh, top, 6 * unit, false, band, band, squareBottom: true);
            inner.height = 26 * unit;
            Rounded(vh, inner, 6 * unit, true, band, band);
        }

        private static void Rounded(VertexHelper vh, Rect r, float radius, bool squareTop, Color32 centerTint, Color32 edgeTint,
            bool squareBottom = false)
        {
            int first = vh.currentVertCount;
            vh.AddVert(r.center, centerTint, Vector2.zero);
            for (int corner = 0; corner < 4; corner++)
            {
                bool right = corner == 0 || corner == 3, top = corner >= 2;
                float cr = squareTop && top || squareBottom && !top ? 0 : radius;
                var c = new Vector2(right ? r.xMax - cr : r.xMin + cr, top ? r.yMax - cr : r.yMin + cr);
                for (int j = 0; j <= 12; j++)
                {
                    float angle = (-corner * 90 - j * 90f / 12) * Mathf.Deg2Rad;
                    vh.AddVert(c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * cr, edgeTint, Vector2.zero);
                }
            }
            int count = vh.currentVertCount - first - 1;
            for (int i = 0; i < count; i++) vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % count);
        }
    }
}

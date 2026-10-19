using System.Collections.Generic;
using System.Linq;
using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// The ARENAS section of the shop: one card per arena sold for gems, between the hero offers and
    /// the gem packs. A card opens a buy dialog; a bought arena is picked in the avatar screen's
    /// ARENAS tab, which the dialog offers to open.
    ///
    /// <para>Cards are copies of the authored hero offer card and the dialog is a copy of the gem
    /// pack dialog, so the section looks like the rest of the shop without a scene edit.</para>
    /// </summary>
    public sealed class ShopArenaSection : MonoBehaviour
    {
        private const float CardColumn = 184f, RowGap = 18f, HeaderToCards = 44f, SectionGap = 38f;
        private static readonly Vector2 ArtSize = new Vector2(154f, 156f), DialogArtSize = new Vector2(220f, 112f);

        private sealed class Card
        {
            public string Id;
            public Button Button;
            public TextMeshProUGUI Action;
        }

        private ShopScreen _shop;
        private readonly List<Card> _cards = new List<Card>();
        private GameObject _dialog;
        private RawImage _dialogArt;
        private TextMeshProUGUI _title, _price, _status, _description, _actionLabel;
        private Button _action;
        private string _showing;
        private bool _bought;
        private float _statusRestoreAt;

        public bool DialogOpen => _dialog != null && _dialog.activeSelf;

        public static ShopArenaSection Install(ShopScreen shop)
        {
            var catalog = ArenaCatalog.Instance;
            if (shop == null || shop.Content == null || shop.Offers.Length == 0 || shop.Packs.Length == 0 || catalog == null) return null;
            var arenas = catalog.Arenas.Where(a => a.Access == ArenaAccess.Purchase && a.Home != null).OrderBy(a => a.Price).ToArray();
            if (arenas.Length == 0) return null;
            var section = shop.gameObject.GetComponent<ShopArenaSection>();
            if (section == null) section = shop.gameObject.AddComponent<ShopArenaSection>();
            section.Build(shop, arenas);
            return section;
        }

        private void Build(ShopScreen shop, ArenaDefinition[] arenas)
        {
            _shop = shop;
            var content = shop.Content;
            var packHeader = (RectTransform)content.GetChild(Mathf.Max(0, shop.Packs[0].transform.GetSiblingIndex() - 1));
            float top = -packHeader.anchoredPosition.y;
            int rows = (arenas.Length + 1) / 2;
            float cardHeight = ((RectTransform)shop.Offers[0].transform).rect.height;
            float height = HeaderToCards + rows * cardHeight + (rows - 1) * RowGap + SectionGap;

            // Make room: everything from the gem packs' header down moves below the new section.
            foreach (RectTransform child in content)
                if (-child.anchoredPosition.y >= top - .5f) child.anchoredPosition -= new Vector2(0f, height);
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, content.rect.height + height);

            int sibling = packHeader.GetSiblingIndex();
            var header = Instantiate(packHeader, content);
            header.name = "ARENAS";
            header.SetSiblingIndex(sibling++);
            header.anchoredPosition = new Vector2(header.anchoredPosition.x, -top);
            header.gameObject.SetActive(true);
            var headerLabel = header.GetComponentInChildren<TextMeshProUGUI>(true);
            if (headerLabel != null) headerLabel.text = "ARENAS";

            var template = shop.Offers[0].gameObject;
            var origin = new Vector2(((RectTransform)template.transform).anchoredPosition.x, -(top + HeaderToCards));
            bool wasActive = template.activeSelf;
            template.SetActive(false);
            for (int i = 0; i < arenas.Length; i++)
            {
                var card = BuildCard(template, arenas[i]);
                card.Button.transform.SetSiblingIndex(sibling++);
                ((RectTransform)card.Button.transform).anchoredPosition = origin + new Vector2(i % 2 * CardColumn, -(i / 2) * (cardHeight + RowGap));
                card.Button.gameObject.SetActive(true);
                _cards.Add(card);
            }
            template.SetActive(wasActive);
            BuildDialog();
        }

        /// <summary>A hero offer card with the hero preview swapped for the arena's art.
        /// Built from an inactive template, so the live hero preview never wakes up.</summary>
        private Card BuildCard(GameObject template, ArenaDefinition arena)
        {
            var root = Instantiate(template, template.transform.parent);
            root.name = arena.Id + "ArenaOffer";
            foreach (string part in new[] { "LivePortrait", "Portrait", "Glow", "Info", "Unknown", "LockIcon" })
            {
                var child = root.transform.Find(part);
                if (child != null) DestroyImmediate(child.gameObject);
            }
            var mask = ArenaUi.Rect(root.transform, "ArenaArt", ArtSize, Vector2.zero);
            mask.anchorMin = mask.anchorMax = mask.pivot = new Vector2(0, 1);
            mask.anchoredPosition = new Vector2(6, -31);
            var surface = root.transform.Find("CleanCard");
            mask.SetSiblingIndex(surface != null ? surface.GetSiblingIndex() + 1 : 0);
            var shape = mask.gameObject.AddComponent<ArenaRoundedGraphic>();
            shape.Radius = 10; shape.raycastTarget = false;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var art = ArenaUi.Rect(mask, "Artwork", Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            ArenaUi.Stretch(art.rectTransform);
            art.raycastTarget = false;
            AvatarCollectionScreen.FrameArena(art, arena.Home, ArtSize);
            root.transform.Find("Name").GetComponent<TextMeshProUGUI>().text = arena.Title.ToUpperInvariant();
            var button = root.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            string id = arena.Id;
            button.onClick.AddListener(() => Open(id));
            return new Card { Id = id, Button = button, Action = root.transform.Find("Action").GetComponent<TextMeshProUGUI>() };
        }

        /// <summary>The gem pack dialog, reused: arena art instead of the gems, BUY instead of OK.
        /// Tapping outside the panel closes it.</summary>
        private void BuildDialog()
        {
            _dialog = Instantiate(_shop.PackDialog, _shop.PackDialog.transform.parent);
            _dialog.name = "ArenaDialog";
            var dim = _dialog.AddComponent<Button>();
            dim.transition = Selectable.Transition.None;
            dim.onClick.AddListener(CloseDialog);
            var panel = _dialog.transform.Find("Panel");
            panel.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // swallow taps
            _title = panel.Find("Title").GetComponent<TextMeshProUGUI>();
            _price = panel.Find("Price").GetComponent<TextMeshProUGUI>();
            _status = panel.Find("Availability").GetComponent<TextMeshProUGUI>();
            _description = panel.Find("Description").GetComponent<TextMeshProUGUI>();

            var gems = (RectTransform)panel.Find("Gems");
            var mask = ArenaUi.Rect(panel, "ArenaArt", DialogArtSize, Vector2.zero);
            mask.anchorMin = mask.anchorMax = new Vector2(.5f, 1f);
            mask.pivot = new Vector2(.5f, 1f);
            mask.anchoredPosition = new Vector2(0f, gems.anchoredPosition.y + 4f);
            mask.SetSiblingIndex(gems.GetSiblingIndex());
            var shape = mask.gameObject.AddComponent<ArenaRoundedGraphic>();
            shape.Radius = 12; shape.raycastTarget = false;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            _dialogArt = ArenaUi.Rect(mask, "Artwork", Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            ArenaUi.Stretch(_dialogArt.rectTransform);
            _dialogArt.raycastTarget = false;
            DestroyImmediate(gems.gameObject);

            _action = panel.Find("Close").GetComponent<Button>();
            _action.name = "Action";
            _action.onClick = new Button.ButtonClickedEvent();
            _action.onClick.AddListener(Act);
            var actionRect = (RectTransform)_action.transform;
            actionRect.anchoredPosition -= new Vector2(35f, 0f);
            actionRect.sizeDelta += new Vector2(70f, 0f);
            _actionLabel = _action.GetComponentInChildren<TextMeshProUGUI>(true);
            var labelRect = _actionLabel.rectTransform;
            labelRect.sizeDelta += new Vector2(70f, 0f);
            _dialog.SetActive(false);
        }

        private void Open(string id)
        {
            var arena = ArenaCatalog.Instance.Find(id);
            if (arena == null) return;
            if (ArenaProfile.Owns(id)) { OpenArenas(); return; }
            _showing = id;
            _bought = false;
            _statusRestoreAt = 0f;
            AvatarCollectionScreen.FrameArena(_dialogArt, arena.Home, DialogArtSize);
            _title.text = arena.Title.ToUpperInvariant();
            _dialog.SetActive(true);
            _dialog.transform.SetAsLastSibling();
            _dialog.GetComponentInChildren<UiTactile>()?.Reveal();
            RefreshDialog();
        }

        private void RefreshDialog()
        {
            var arena = ArenaCatalog.Instance.Find(_showing);
            if (arena == null) return;
            _price.text = _bought ? "UNLOCKED!" : $"{arena.Price:N0} <sprite name=\"gem\">";
            _status.text = _bought ? "IT'S IN YOUR ARENAS" : "HOME BACKGROUND";
            _status.color = Color.white;
            _description.text = _bought ? "Pick it there to make it your home." : "Shows behind your hero on the home screen.";
            _actionLabel.text = _bought ? "OPEN ARENAS" : "BUY";
        }

        private void Act()
        {
            if (_showing == null) return;
            if (_bought) { OpenArenas(); return; }
            if (!ArenaProfile.TryBuy(_showing))
            {
                GameAudio.Play(SoundCue.RepRejected);
                StartCoroutine(Shake(_action.transform));
                _status.text = "NOT ENOUGH GEMS";
                _status.color = new Color32(255, 110, 110, 255);
                _statusRestoreAt = Time.unscaledTime + 1.6f;
                return;
            }
            GameAudio.Play(SoundCue.Purchase);
            _bought = true;
            _statusRestoreAt = 0f;
            _shop.Refresh();
            RefreshDialog();
        }

        private void OpenArenas()
        {
            CloseDialog();
            _shop.Hide();
            _shop.Collection.ShowArenas();
        }

        public void CloseDialog()
        {
            if (_dialog != null) _dialog.SetActive(false);
            _showing = null;
        }

        public void Refresh()
        {
            foreach (var card in _cards)
            {
                var arena = ArenaCatalog.Instance.Find(card.Id);
                card.Action.text = !ArenaProfile.Owns(card.Id) ? $"{arena.Price:N0} <sprite name=\"gem\">"
                    : ArenaProfile.SelectedId == card.Id ? "SELECTED" : "OWNED";
            }
        }

        public void Reveal(float delay)
        {
            for (int i = 0; i < _cards.Count; i++) _cards[i].Button.GetComponent<UiTactile>()?.Reveal(delay + i * .05f);
        }

        /// <summary>Shop notices: each arena on sale, and each one bought.</summary>
        public static IEnumerable<string> Notices()
        {
            var catalog = ArenaCatalog.Instance;
            if (catalog == null) yield break;
            foreach (var arena in catalog.Arenas)
            {
                if (arena.Access != ArenaAccess.Purchase || arena.Home == null) continue;
                yield return $"arena:{arena.Id}:{arena.Price}";
                if (ArenaProfile.Owns(arena.Id)) yield return "owned-arena:" + arena.Id;
            }
        }

        private void Update()
        {
            if (!DialogOpen) return;
            if (!_shop.IsOpen) { CloseDialog(); return; }
            if (_statusRestoreAt > 0f && Time.unscaledTime >= _statusRestoreAt) { _statusRestoreAt = 0f; RefreshDialog(); }
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
    }
}

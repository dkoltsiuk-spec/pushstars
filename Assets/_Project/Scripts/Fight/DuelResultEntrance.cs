using System.Collections;
using System.Collections.Generic;
using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Outcome hierarchy and a short, unscaled trophy-first result reveal.</summary>
    public sealed class DuelResultEntrance : MonoBehaviour
    {
        private sealed class Item
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public Vector2 Position;
            public Vector3 Scale;
            public float Alpha;
            public bool Opponent;
            public bool Next;
        }

        private readonly List<Item> _items = new();
        private RectTransform _composition, _cup;
        private Vector2 _origin, _cupPosition;
        private Quaternion _cupRotation;
        private Button _next;
        private bool _nextInteractable;
        private Coroutine _routine;

        // Shared with the scene authoring pass so Edit Mode and Play Mode use the same layout.
        public static void ApplyLayout(RectTransform root)
        {
            Place(root, "OpponentPortrait", 288, 190, 194, 310);
            Place(root, "PlayerPortrait", 103, 606, 205, 340);
            Place(root, "OpponentGroundShadow", 288, 338, 145, 22);
            Place(root, "PlayerGroundShadow", 103, 768, 166, 24);
            Place(root, "OpponentNamePlate", 112, 94, 215, 49);
            Place(root, "PlayerNamePlate", 268, 480, 230, 78);
            Place(root, "OpponentReps", 80, 207, 140, 110);
            Place(root, "PlayerReps", 307, 607, 150, 120);
            Place(root, "OpponentResultFormCaption", 72, 267, 122, 21);
            Place(root, "OpponentResultForm", 72, 291, 122, 32);
            Place(root, "OpponentResultTempoCaption", 72, 316, 122, 21);
            Place(root, "OpponentResultTempo", 72, 340, 122, 32);
            Place(root, "PlayerResultFormCaption", 330, 666, 76, 21);
            Place(root, "PlayerResultForm", 330, 692, 76, 32);
            Place(root, "PlayerResultTempoCaption", 330, 720, 76, 21);
            Place(root, "PlayerResultTempo", 330, 746, 76, 32);
            Place(root, "OpponentFlag", 28, 132, 27, 20);
            Place(root, "OpponentAura", 56, 132, 23, 23);
            Place(root, "OpponentLevel", 82, 132, 23, 23);
            Place(root, "PlayerFlag", 310, 522, 27, 20);
            Place(root, "PlayerAura", 337, 522, 23, 23);
            Place(root, "PlayerLevel", 363, 522, 23, 23);
            Place(root, "BannerPlate", 195, 398, 260, 215);
            Place(root, "Continue", 195, 796, 126, 56);
            var cup = root.Find("BannerPlate");
            if (cup != null)
            {
                cup.SetAsFirstSibling();
                var image = cup.GetComponent<Image>();
                image.sprite = Resources.Load<Sprite>("Results/Win");
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.raycastTarget = false;
                image.color = Color.white;
                var title = cup.GetComponentInChildren<TextMeshProUGUI>(true);
                if (title != null) title.gameObject.SetActive(false);
            }
        }

        private static void Place(Transform root, string name, float x, float y, float w, float h)
        {
            if (!(root.Find(name) is RectTransform rect)) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            rect.localScale = Vector3.one;
        }

        public void Play(RectTransform composition, bool win, bool draw, Button next)
        {
            ResetPresentation();
            _composition = composition;
            ApplyLayout(composition);
            _origin = composition.anchoredPosition;
            _cup = composition.Find("BannerPlate") as RectTransform;
            _cupPosition = _cup.anchoredPosition;
            _cupRotation = _cup.localRotation;
            var image = _cup.GetComponent<Image>();
            image.enabled = !draw;
            image.sprite = Resources.Load<Sprite>(win ? "Results/Win" : "Results/Lose");
            var title = _cup.GetComponentInChildren<TextMeshProUGUI>(true);
            if (title != null)
            {
                title.gameObject.SetActive(draw);
                if (draw) title.text = "DRAW";
            }
            _next = next;
            _nextInteractable = next != null && next.interactable;
            if (next != null) next.interactable = false;
            foreach (Transform child in composition)
            {
                bool opponent = child.name.StartsWith("Opponent");
                bool player = child.name.StartsWith("Player");
                bool isNext = next != null && child == next.transform;
                if (!opponent && !player && !isNext) continue;
                var rect = child as RectTransform;
                var group = child.GetComponent<CanvasGroup>();
                if (group == null) group = child.gameObject.AddComponent<CanvasGroup>();
                var item = new Item { Rect = rect, Group = group, Position = rect.anchoredPosition,
                    Scale = rect.localScale, Alpha = group.alpha, Opponent = opponent, Next = isNext };
                _items.Add(item);
                bool lost = !draw && (opponent ? win : player && !win);
                float scale = lost ? (child.name.EndsWith("Portrait") ? .77f : .86f) : 1f;
                rect.localScale = item.Scale * scale;
                // Keep feet and text-column edges fixed while reducing their visual footprint.
                if (lost && child.name.EndsWith("Portrait"))
                    rect.anchoredPosition -= Vector2.up * rect.sizeDelta.y * (1f - scale) * .5f;
                else if (lost && !child.name.EndsWith("GroundShadow"))
                    rect.anchoredPosition += Vector2.right * (opponent ? -1 : 1) * rect.sizeDelta.x * (1f - scale) * .5f;
                group.alpha = 0;
            }
            _routine = StartCoroutine(Reveal(win, draw));
        }

        private IEnumerator Reveal(bool win, bool draw)
        {
            var targets = new Vector2[_items.Count];
            for (int i = 0; i < _items.Count; i++) targets[i] = _items[i].Rect.anchoredPosition;
            float elapsed = 0;
            bool impacted = false;
            while (elapsed < 1.35f)
            {
                float fly = Mathf.Clamp01(elapsed / .42f);
                float eased = 1f - Mathf.Pow(1f - fly, 3);
                _cup.anchoredPosition = _cupPosition + Vector2.up * Mathf.Lerp(480, 0, eased);
                _cup.localRotation = _cupRotation * Quaternion.Euler(0, 0, Mathf.Lerp(-22, 0, eased));
                float bounce = elapsed > .42f ? Mathf.Sin(Mathf.Clamp01((elapsed - .42f) / .22f) * Mathf.PI) * .12f : 0;
                _cup.localScale = Vector3.one * (Mathf.Lerp(.4f, 1f, eased) + bounce);
                if (!impacted && elapsed >= .42f)
                {
                    impacted = true;
                    Haptics.Medium();
                    GameAudio.Play(draw ? SoundCue.Confirm : win ? SoundCue.Victory : SoundCue.Back);
                }
                float shake = Mathf.Clamp01(1f - (elapsed - .42f) / .18f);
                _composition.anchoredPosition = _origin + (impacted ? new Vector2(Mathf.Sin(elapsed * 135), Mathf.Cos(elapsed * 109)) * (3f * shake) : Vector2.zero);
                for (int i = 0; i < _items.Count; i++)
                {
                    var item = _items[i];
                    float start = item.Next ? 1.05f : item.Opponent ? .68f : .57f;
                    float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01((elapsed - start) / .28f));
                    item.Group.alpha = item.Alpha * t;
                    item.Rect.anchoredPosition = targets[i] + new Vector2(item.Next ? 0 : item.Opponent ? 22 : -22, item.Next ? -12 : 0) * (1 - t);
                }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            _composition.anchoredPosition = _origin;
            _cup.anchoredPosition = _cupPosition;
            _cup.localRotation = _cupRotation;
            _cup.localScale = Vector3.one;
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].Group.alpha = _items[i].Alpha;
                _items[i].Rect.anchoredPosition = targets[i];
            }
            if (_next != null) _next.interactable = _nextInteractable;
            _routine = null;
        }

        public void ResetPresentation()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            if (_composition != null) _composition.anchoredPosition = _origin;
            if (_cup != null)
            {
                _cup.anchoredPosition = _cupPosition;
                _cup.localScale = Vector3.one;
                _cup.localRotation = _cupRotation;
            }
            foreach (var item in _items)
            {
                if (item.Rect == null) continue;
                item.Rect.anchoredPosition = item.Position;
                item.Rect.localScale = item.Scale;
                item.Group.alpha = item.Alpha;
            }
            _items.Clear();
            if (_next != null) _next.interactable = _nextInteractable;
        }

        private void OnDisable() => ResetPresentation();
    }
}

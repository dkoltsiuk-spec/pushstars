using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PushStars.Fight
{
    /// <summary>
    /// Which half is yours. A duel puts two bodies on one arena, and when both players picked the
    /// same hero the halves are copies of each other — the only tells were the yellow count and
    /// "you are the bottom one", which the screen never says. So, added at runtime on top of the
    /// scene's own HUD:
    /// <list type="bullet">
    /// <item>a YOU chip on your name plate, a yellow game-style plate like the rest of the UI —
    /// a generated nickname is not one a new player recognises yet;</item>
    /// <item>the opponent's half a shade darker, so yours is the lit one (their numbers stay
    /// bright — the dim sits under the HUD).</item>
    /// </list>
    /// </summary>
    public sealed partial class FightHud
    {
        private const float OpponentDimAlpha = .34f;
        /// <summary>Keeps the dim off the seam rail in the backdrop, which straddles the line between
        /// the halves — dimming half a rail reads as a rendering fault, not as emphasis.</summary>
        private const float DimSeamClearance = 10f;
        private const float OwnTagWidth = 50f, OwnTagHeight = 28f, OwnTagLeft = 2f, OwnTagGap = 6f;

        private Image _opponentDim;
        private bool _ownSideBuilt;

        /// <summary>A new duel: builds the cues once and dims the opponent's half.</summary>
        private void PrepareOwnSide()
        {
            BuildOwnSide();
            SetOpponentDim(true);
        }

        private void SetOpponentDim(bool on)
        {
            if (_opponentDim != null) _opponentDim.gameObject.SetActive(on);
        }

        private void BuildOwnSide()
        {
            if (_ownSideBuilt) return;
            _ownSideBuilt = true;
            BuildOwnTag();
            BuildOpponentDim();
        }

        private void BuildOwnTag()
        {
            if (_playerName == null || !(_playerName.transform.parent is RectTransform plate)) return;

            var tag = new GameObject("OwnTag", typeof(RectTransform), typeof(CanvasRenderer), typeof(RewardPlateGraphic))
                .GetComponent<RectTransform>();
            tag.SetParent(plate, false);
            tag.anchorMin = tag.anchorMax = new Vector2(0f, .5f);
            tag.pivot = new Vector2(0f, .5f);
            tag.anchoredPosition = new Vector2(OwnTagLeft, 1f);
            tag.sizeDelta = new Vector2(OwnTagWidth, OwnTagHeight);
            var chip = tag.GetComponent<RewardPlateGraphic>();
            chip.Top = new Color32(255, 228, 92, 255);
            chip.Bottom = new Color32(247, 174, 22, 255);
            chip.Border = new Color32(20, 14, 4, 255);
            chip.Shadow = new Color32(0, 0, 0, 170);
            chip.Radius = 7f;
            chip.BorderWidth = 2f;
            chip.ShadowOffset = 3f;
            chip.Slant = 6f;
            chip.Gloss = .3f;
            chip.raycastTarget = false;

            var label = new GameObject("OwnTagLabel", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.rectTransform.SetParent(tag, false);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(5f, 2f);
            label.rectTransform.offsetMax = new Vector2(-5f, -2f);
            label.font = _playerName.font;
            label.fontStyle = FontStyles.Bold;
            label.enableAutoSizing = true;
            label.fontSizeMin = 9f;
            label.fontSizeMax = 15f;
            label.characterSpacing = 2f;
            label.alignment = TextAlignmentOptions.Center;
            // White with a black keyline, like the counters and the buttons' captions.
            label.color = Color.white;
            label.outlineColor = Color.black;
            label.outlineWidth = .3f;
            label.raycastTarget = false;
            label.text = "YOU";

            // The name gives up the chip's room on the left and starts right after it, so the two
            // read as one label; long nicknames shrink instead of running under the chip.
            var nameRect = _playerName.rectTransform;
            nameRect.offsetMin = new Vector2(OwnTagLeft + OwnTagWidth + OwnTagGap, nameRect.offsetMin.y);
            _playerName.alignment = TextAlignmentOptions.Left;
            if (!_playerName.enableAutoSizing)
            {
                _playerName.fontSizeMax = _playerName.fontSize;
                _playerName.fontSizeMin = Mathf.Min(9f, _playerName.fontSize);
                _playerName.enableAutoSizing = true;
            }
        }

        private void BuildOpponentDim()
        {
            if (_opponentHalf == null) return;
            var dim = new GameObject("OwnSideDim", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            dim.SetParent(_opponentHalf.transform, false);
            dim.SetAsLastSibling();
            dim.anchorMin = Vector2.zero;
            dim.anchorMax = Vector2.one;
            dim.offsetMin = new Vector2(0f, DimSeamClearance);
            dim.offsetMax = Vector2.zero;
            _opponentDim = dim.GetComponent<Image>();
            _opponentDim.color = new Color(0f, 0f, 0f, OpponentDimAlpha);
            _opponentDim.raycastTarget = false;
        }
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PushStars.UI;

namespace PushStars.Fight
{
    public sealed partial class DuelReadyPanel
    {
        private RectTransform _composition;
        private RectTransform _safeBounds;
        private bool _layoutBuilt;

        // Share of the portrait box the figure fills, crown to sole. The boxes below are sized
        // around it: raising this alone would only crop tighter, not enlarge the figure.
        private const float PortraitBodyFill = 0.95f;
        // How the leftover room is split: a tenth under the soles, the rest as headroom. The
        // figures keep standing near the bottom of their boxes, where the composition puts them —
        // the spare height is sky above the head, not a gap the shoes float over.
        private const float PortraitFootShare = 0.1f;
        // Both names lean the same way — a small dynamic tilt, matching the mockup. Small on
        // purpose: the typeface is already a true italic, and the two slants compound.
        private const float NameTiltDegrees = 4f;
        // One line and no more: tall enough for a name at the top of the auto-size range, too
        // short for two at the bottom of it. See NameStyle for why that is the fit-to-width lever.
        private const float NameBand = 46f;

        // The contact shadow's ellipse, as a share of the figure's height. A touch tighter than
        // the menu stage's 168 × 40 under a figure of about 395: that one is a soft blur, and the
        // same footprint drawn with a hard edge reads as a much heavier slab.
        private const float ShadowWidthOfFigure = 0.40f;
        private const float ShadowHeightOfFigure = 0.09f;
        // How far the ellipse's centre rides ABOVE the sole line, as a share of its own height.
        // Lowering the figure onto its shadow cannot be done by lowering the figure: the ellipse
        // is placed from the soles and would follow it down. This is the offset between the two,
        // and it is what sinks the feet into the shadow rather than perching them on top of it.
        private const float ShadowRiseOfHeight = 0.30f;

        private Image _opponentFlag;
        private Image _playerFlag;
        private Image _opponentShadow;
        private Image _playerShadow;

        // Reference art's content, excluding the phone bezel and operating-system chrome.
        // A uniform scale keeps portraits, typography and the square VS medal undistorted.
        private void BuildReferenceLayout()
        {
            if (_layoutBuilt) return;
            _layoutBuilt = true;
            var root = (RectTransform)_root.transform;
            _safeBounds = root.Find("SafeArea") as RectTransform;
            if (_safeBounds == null) _safeBounds = root;

            var backdrop = new GameObject("PreparationBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(ReadyScreenGraphic));
            backdrop.transform.SetParent(root, false);
            backdrop.transform.SetAsFirstSibling();
            var bgRect = (RectTransform)backdrop.transform;
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = bgRect.offsetMax = Vector2.zero;
            backdrop.GetComponent<ReadyScreenGraphic>().raycastTarget = true;

            // The drifting lightning from the Main screen, layered over the gradient. Same builder,
            // so it is the exact field the home screen uses. Parented to the backdrop so it renders
            // above the gradient and behind every piece of the composition.
            var boltSprite = Theme != null ? Theme.IconLightningBG : null;
            if (boltSprite != null) LightningField.Build(bgRect, boltSprite);

            foreach (string name in new[] { "Watermark1", "Watermark2", "TapBlock" })
            {
                var old = root.Find(name);
                if (old != null) old.gameObject.SetActive(false);
            }

            _composition = new GameObject("PreparationComposition", typeof(RectTransform)).GetComponent<RectTransform>();
            _composition.SetParent(_safeBounds, false);
            _composition.anchorMin = _composition.anchorMax = new Vector2(0.5f, 0.5f);
            _composition.pivot = new Vector2(0.5f, 0.5f);
            _composition.sizeDelta = new Vector2(390f, 844f);

            // Contact shadows first, so they draw under the bodies standing on them. CopyPortrait
            // puts each one where that body's soles actually landed.
            _opponentShadow = GroundShadow("OpponentGroundShadow");
            _playerShadow = GroundShadow("PlayerGroundShadow");

            // These boxes set how large the two figures read: CopyPortrait fits each body to its
            // box rather than cropping a fixed window out of the stage render. Equal heights, so
            // the two fighters read at one scale. At this size the growth goes downward — heads
            // were already near the top edge — which lands the soles just short of the two things
            // below them, the VS medal at 365 and the READY button at 738. Each is pushed a little
            // further out to its own side, away from the other's column of numbers.
            _opponentAvatarImage = Portrait(_opponentAvatarImage, "OpponentPortrait", 212f, 8f, 158f, 368f);
            _playerAvatarImage = Portrait(_playerAvatarImage, "PlayerPortrait", -4f, 364f, 182f, 368f);

            // Flag chip beside the name: flag-then-name for the opponent (top-left), name-then-flag
            // for the player (bottom-right). Where the two actually sit is settled by RefreshFlags,
            // which is the only place that knows whether there is a chip to make room for.
            _opponentFlag = FlagChip(_opponentFlag, "OpponentFlag", 18f, 101f, 28f, 19f);
            NameStyle(_opponentName, right: false);
            _playerFlag = FlagChip(_playerFlag, "PlayerFlag", 347f, 458f, 28f, 19f);
            NameStyle(_playerName, right: true);

            TrophyStyle(_opponentTrophies, 18f, 172f, false);
            TrophyStyle(_playerTrophies, 241f, 526f, true);
            StatStyle(_opponentBest, "OpponentBestCaption", "MAX PUSHUP", 20f, 239f, false);
            StatStyle(_opponentWinRate, "OpponentWinRateCaption", "WIN RATE", 20f, 286f, false);
            StatStyle(_playerBest, "PlayerBestCaption", "MAX PUSHUP", 284f, 587f, true);
            StatStyle(_playerWinRate, "PlayerWinRateCaption", "WIN RATE", 284f, 646f, true);

            var medal = _safeBounds.Find("VsMedal") as RectTransform;
            if (medal != null)
            {
                Place(medal, 146f, 365f, 98f, 98f);
                var medalImage = medal.GetComponent<Image>();
                if (medalImage != null) { medalImage.preserveAspect = true; medalImage.raycastTarget = false; }
            }

            if (_readyButton != null) StyleReadyButton();
            FitComposition();
        }

        private void LateUpdate()
        {
            if (!_layoutBuilt || _root == null || !_root.activeInHierarchy) return;
            FitComposition();
            RefreshPortraits();
        }

        private void FitComposition()
        {
            float scale = Mathf.Min(_safeBounds.rect.width / 390f, _safeBounds.rect.height / 844f);
            if (scale > 0f) _composition.localScale = Vector3.one * scale;
        }

        /// <summary>The drawn button plate from the design system (<c>btn_start.png</c>) rather
        /// than the skewed quads this used to assemble in <see cref="ReadyScreenGraphic"/> — those
        /// were an approximation of this exact asset, down to the darker lip along the bottom.
        /// Sized at the sprite's own 333:172 so the parallelogram's slant renders as drawn.</summary>
        private void StyleReadyButton()
        {
            const float width = 124f, height = width * 172f / 333f;
            // Centred, with its foot where the old plate's was.
            Place((RectTransform)_readyButton.transform, (390f - width) * 0.5f, 802f - height, width, height);

            var plate = _readyButton.GetComponent<Image>();
            if (plate == null) plate = _readyButton.gameObject.AddComponent<Image>();
            plate.enabled = true;
            plate.type = Image.Type.Simple;
            plate.sprite = Theme != null ? Theme.BtnShape : null;
            // Pre-coloured art: white lets it through untinted. Without it, the button is never
            // blank — a flat yellow plate stands in.
            plate.color = plate.sprite != null ? Color.white : new Color32(255, 213, 17, 255);
            _readyButton.targetGraphic = plate;

            var label = _readyButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null) return;
            label.text = "READY";
            label.fontSize = 20f;
            label.enableAutoSizing = false;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.outlineColor = Color.black;
            label.outlineWidth = 0.2f;
            label.raycastTarget = false;
            // Centred on the plate's face, not on its box: the sprite's bottom ~9% is the darker
            // lip the button appears to sit on, and text centred over both reads low.
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, height * 0.09f);
            labelRect.offsetMax = new Vector2(-8f, 0f);
        }

        private RawImage Portrait(RawImage image, string name, float x, float y, float width, float height)
        {
            if (image == null)
                image = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            Place(image.rectTransform, x, y, width, height);
            image.gameObject.SetActive(true);
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>The flat ellipse under a fighter's soles: the design system's hard-edged
        /// circle, squashed by its own rect. Not <c>GroundShadow</c>, which is a soft blur — at
        /// this size that reads as a smudge the figure hovers in rather than as ground it stands
        /// on. Centre-pivoted, because it is placed from the sole line rather than from a corner.
        /// </summary>
        private Image GroundShadow(string name)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            var rect = image.rectTransform;
            rect.SetParent(_composition, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;
            image.sprite = Theme != null ? Theme.CircleShape : null;
            // Simple, never sliced: a nine-slice would hold the circle's corners at their drawn
            // size and stretch a straight middle between them, which is a capsule, not an ellipse.
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = new Color(0f, 0f, 0f, 0.5f);
            image.raycastTarget = false;
            image.enabled = image.sprite != null;
            return image;
        }

        private void RefreshPortraits()
        {
            CopyPortrait(_opponentAvatarImage, _opponentAvatarSource, _opponentShadow);
            CopyPortrait(_playerAvatarImage, _playerAvatarSource, _playerShadow);
        }

        /// <summary>Lays the contact shadow on this body's sole line, sized to how tall the figure
        /// ended up reading. Both come out of the crop just computed — the body's own rect mapped
        /// back through it into the portrait box — so the shadow tracks the figure instead of being
        /// a second set of coordinates to keep in step by hand.</summary>
        private void PlaceGroundShadow(Image shadow, RectTransform box, Rect uv, Rect body)
        {
            if (shadow == null || !shadow.enabled) return;
            Rect boxRect = box.rect;
            float figure = body.height / uv.height * boxRect.height;

            float height = figure * ShadowHeightOfFigure;
            shadow.rectTransform.anchoredPosition = new Vector2(
                box.anchoredPosition.x + (body.center.x - uv.x) / uv.width * boxRect.width,
                box.anchoredPosition.y - boxRect.height
                    + (body.yMin - uv.y) / uv.height * boxRect.height
                    + height * ShadowRiseOfHeight);
            shadow.rectTransform.sizeDelta = new Vector2(figure * ShadowWidthOfFigure, height);
        }

        /// <summary>The body whose stage renders into <paramref name="source"/>, matched by the
        /// texture itself rather than by a second serialized reference — the panel already holds
        /// every <see cref="FightAvatar"/> in the scene from <see cref="Show"/>.</summary>
        private FightAvatar AvatarBehind(RawImage source)
        {
            if (_preparationAvatars == null || source == null) return null;
            foreach (var avatar in _preparationAvatars)
                if (avatar != null && avatar.StageCamera != null &&
                    avatar.StageCamera.targetTexture == source.texture)
                    return avatar;
            return null;
        }

        private void CopyPortrait(RawImage target, RawImage source, Image shadow)
        {
            if (target == null) return;
            target.enabled = source != null && source.texture != null;
            if (shadow != null) shadow.gameObject.SetActive(target.enabled);
            if (!target.enabled) return;
            target.texture = source.texture;

            // Where the figure actually sits in that render. The stage camera fits a sphere around
            // its aim point, which leaves a third of the frame empty above the head — cropping a
            // fixed fraction off both ends (what this used to do) therefore spent the zoom on the
            // feet and cut them off. Fitting the body's own rect spends it on the empty sky instead.
            var avatar = AvatarBehind(source);
            Rect body;
            if (avatar == null || !avatar.TryGetBodyViewport(out body))
            {
                target.uvRect = new Rect(0f, 0f, 1f, 1f);
                if (shadow != null) shadow.gameObject.SetActive(false);
                return;
            }

            float texAspect = (float)source.texture.width / source.texture.height;
            float boxAspect = target.rectTransform.rect.width /
                              Mathf.Max(1f, target.rectTransform.rect.height);

            // Height first — the boxes are far taller than they are wide, so the figure's height is
            // what fills them; PortraitBodyFill leaves a little air top and bottom.
            float height = Mathf.Min(1f, body.height / PortraitBodyFill);
            float width = Mathf.Min(1f, height * boxAspect / texAspect);
            // A box wide enough to need more width than the render has re-derives the height from
            // it, so the body is never stretched to fill the box.
            height = Mathf.Min(height, width * texAspect / boxAspect);

            var uv = new Rect(
                Mathf.Clamp(body.center.x - width * 0.5f, 0f, 1f - width),
                Mathf.Clamp(body.yMin - (height - body.height) * PortraitFootShare, 0f, 1f - height),
                width, height);
            target.uvRect = uv;
            PlaceGroundShadow(shadow, target.rectTransform, uv, body);
        }

        private Image FlagChip(Image image, string name, float x, float y, float width, float height)
        {
            if (image == null)
                image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            Place(image.rectTransform, x, y, width, height);
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = false; // shown by RefreshFlags once a sprite is assigned
            return image;
        }

        private void RefreshFlags()
        {
            Apply(_opponentFlag, _opponentFlagSprite);
            Apply(_playerFlag, _playerFlagSprite);

            // Each name shares its column's edge with the block underneath it — trophies, MAX
            // PUSHUP, WIN RATE — and gives that edge up only to make room for a flag chip in front
            // of it. A boss carries no flag, and an indent held open for a chip that never arrives
            // is what knocked the opponent's name out of the column and pushed it over his body.
            NamePlace(_opponentName, _opponentFlagSprite != null ? 52f : 18f, 214f, 88f);
            NamePlace(_playerName, 138f, _playerFlagSprite != null ? 343f : 375f, 445f);

            static void Apply(Image image, Sprite sprite)
            {
                if (image == null) return;
                image.sprite = sprite;
                image.enabled = sprite != null;
            }
        }

        private void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.SetParent(_composition, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
        }

        private void NameStyle(TextMeshProUGUI text, bool right)
        {
            if (text == null) return;

            // The label already carries Rubik BoldItalic — a drawn italic, not a slanted upright.
            // Asking TMP for Bold|Italic on top of it stacked a synthetic shear and a synthetic
            // weight over letters that had both, which is what made the name read as bent rather
            // than as leaning. Faux styles stay available for a scene authored with the upright
            // face, so nothing built earlier loses the look.
            bool drawnItalic = text.font != null && text.font.name.Contains("Italic");
            text.fontStyle = drawnItalic ? FontStyles.Normal : FontStyles.Bold | FontStyles.Italic;

            text.color = new Color32(255, 211, 0, 255);
            text.enableAutoSizing = true;
            text.fontSizeMin = 22f; text.fontSizeMax = 38f;
            // Wrapping stays ON, and NameBand is what turns that into a fit-to-width. TMP's
            // auto-size only shrinks for text it cannot lay out; with wrapping off a long name is
            // laid out fine — as one line running clear off the side of its box, which is exactly
            // what it did. With wrapping on and a band too short to hold a second line, the only
            // way left to fit is narrower letters.
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = right ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
            text.outlineColor = Color.black; text.outlineWidth = 0.25f;
            text.raycastTarget = false;
        }

        /// <summary>Sits one name between its column's two edges. Rotated about the band's centre so
        /// the tilt leans the name in place instead of flinging it off its anchor.</summary>
        private void NamePlace(TextMeshProUGUI text, float left, float right, float y)
        {
            if (text == null) return;
            Place(text.rectTransform, left, y, right - left, NameBand);
            var rect = text.rectTransform;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2((left + right) * 0.5f, -(y + NameBand * 0.5f));
            rect.localRotation = Quaternion.Euler(0f, 0f, NameTiltDegrees);
        }

        private void TrophyStyle(TextMeshProUGUI text, float x, float y, bool right)
        {
            if (text == null) return;
            var row = text.transform.parent as RectTransform;
            Place(row, x, y, 136f, 47f);
            var icon = row.Find("Icon") as RectTransform;
            if (icon != null)
            {
                icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
                icon.pivot = new Vector2(0f, 0.5f);
                icon.anchoredPosition = Vector2.zero; icon.sizeDelta = new Vector2(43f, 39f);
            }
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            text.rectTransform.pivot = new Vector2(0f, 0.5f);
            text.rectTransform.anchoredPosition = new Vector2(48f, 0f);
            text.rectTransform.sizeDelta = new Vector2(88f, 47f);
            text.fontSizeMin = 26f; text.fontSizeMax = 44f; text.enableAutoSizing = true;
            text.color = new Color32(255, 215, 0, 255);
            text.alignment = right ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
            text.outlineColor = Color.black; text.outlineWidth = 0.23f;
        }

        private void StatStyle(TextMeshProUGUI text, string captionName, string caption, float x, float y, bool right)
        {
            if (text == null) return;
            var title = _safeBounds.Find(captionName)?.GetComponent<TextMeshProUGUI>();
            if (title != null)
            {
                Place(title.rectTransform, x, y, 90f, 13f);
                title.text = caption; title.fontSize = 9f; title.enableAutoSizing = false;
                title.fontStyle = FontStyles.Normal; title.color = new Color32(145, 135, 159, 255);
                title.outlineWidth = 0f;
                title.alignment = right ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
            }
            Place(text.rectTransform, x, y + 13f, 90f, 31f);
            text.fontSize = 26f; text.enableAutoSizing = false;
            text.color = Color.white; text.outlineColor = Color.black; text.outlineWidth = 0.2f;
            text.alignment = right ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
        }
    }
}

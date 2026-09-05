using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    public sealed partial class DuelReadyPanel
    {
        private RectTransform _composition;
        private RectTransform _safeBounds;
        private bool _layoutBuilt;

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

            _opponentAvatarImage = Portrait(_opponentAvatarImage, "OpponentPortrait", 207f, 53f, 136f, 322f);
            _playerAvatarImage = Portrait(_playerAvatarImage, "PlayerPortrait", 21f, 421f, 158f, 324f);

            NameStyle(_opponentName, 18f, 91f, 206f, 67f);
            NameStyle(_playerName, 170f, 448f, 214f, 74f);
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

            if (_readyButton != null)
            {
                Place((RectTransform)_readyButton.transform, 146f, 752f, 106f, 50f);
                var oldImage = _readyButton.GetComponent<Image>();
                if (oldImage != null) oldImage.enabled = false;
                var face = new GameObject("ReadyFace", typeof(RectTransform), typeof(CanvasRenderer), typeof(ReadyScreenGraphic));
                face.transform.SetParent(_readyButton.transform, false);
                face.transform.SetAsFirstSibling();
                var faceRect = (RectTransform)face.transform;
                faceRect.anchorMin = Vector2.zero; faceRect.anchorMax = Vector2.one;
                faceRect.offsetMin = faceRect.offsetMax = Vector2.zero;
                var graphic = face.GetComponent<ReadyScreenGraphic>();
                graphic.ButtonFace = true;
                _readyButton.targetGraphic = graphic;
                var label = _readyButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null)
                {
                    label.text = "READY";
                    label.fontSize = 18f;
                    label.enableAutoSizing = false;
                    label.fontStyle = FontStyles.Bold;
                    label.color = Color.white;
                    label.outlineColor = Color.black;
                    label.outlineWidth = 0.2f;
                    label.raycastTarget = false;
                }
            }
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

        private void RefreshPortraits()
        {
            CopyPortrait(_opponentAvatarImage, _opponentAvatarSource);
            CopyPortrait(_playerAvatarImage, _playerAvatarSource);
        }

        private static void CopyPortrait(RawImage target, RawImage source)
        {
            if (target == null) return;
            target.enabled = source != null && source.texture != null;
            if (!target.enabled) return;
            target.texture = source.texture;
            // Crop transparent side margins; never stretch the 3D body to fit a portrait box.
            float aspect = target.rectTransform.rect.width / target.rectTransform.rect.height;
            float crop = Mathf.Min(1f, aspect * source.texture.height / source.texture.width);
            target.uvRect = new Rect((1f - crop) * 0.5f, 0f, crop, 1f);
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

        private void NameStyle(TextMeshProUGUI text, float x, float y, float w, float h)
        {
            if (text == null) return;
            Place(text.rectTransform, x, y, w, h);
            text.fontStyle = FontStyles.Bold | FontStyles.Italic;
            text.color = new Color32(255, 211, 0, 255);
            text.enableAutoSizing = true;
            text.fontSizeMin = 20f; text.fontSizeMax = 33f;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.outlineColor = Color.black; text.outlineWidth = 0.25f;
            text.raycastTarget = false;
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

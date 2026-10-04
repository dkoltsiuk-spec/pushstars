using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PushStars.Core;

namespace PushStars.UI
{
    public static class ArenaUi
    {
        public static RectTransform Rect(Transform parent, string name, Vector2 size, Vector2 position)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false); r.sizeDelta = size; r.anchoredPosition = position; return r;
        }
        public static void Stretch(RectTransform r)
        { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        public static Image Image(Transform parent, string name, Vector2 size, Vector2 position, Color color, Sprite sprite = null)
        {
            var r = Rect(parent, name, size, position); var image = r.gameObject.AddComponent<Image>();
            image.color = color; image.sprite = sprite; image.raycastTarget = false; return image;
        }
        public static TextMeshProUGUI Label(Transform parent, string name, string text, Vector2 size, Vector2 position, float fontSize)
        {
            var r = Rect(parent, name, size, position); var label = r.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text; label.fontSize = fontSize; label.fontStyle = FontStyles.Bold; label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white; label.raycastTarget = false; label.enableAutoSizing = true; label.fontSizeMin = fontSize - 3; label.fontSizeMax = fontSize;
            return label;
        }
        public static Sprite Slice(Sprite source, Rect uv)
        {
            var r = source.rect;
            return Sprite.Create(source.texture, new Rect(r.x + r.width * uv.x, r.y + r.height * uv.y, r.width * uv.width, r.height * uv.height), new Vector2(.5f,.5f), 100);
        }
        public static RawImage Thumbnail(Transform parent, Sprite sprite, Vector2 size)
        {
            var r = Rect(parent, "ArtworkMask", size, Vector2.zero);
            var shape = r.gameObject.AddComponent<ArenaRoundedGraphic>(); shape.raycastTarget = false;
            r.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var raw = Rect(r, "Artwork", size, Vector2.zero).gameObject.AddComponent<RawImage>(); raw.raycastTarget = false;
            SetThumbnail(raw, sprite); return raw;
        }
        public static void SetThumbnail(RawImage image, Sprite sprite)
        {
            if (sprite == null) return;
            image.texture = sprite.texture;
            var rect = sprite.textureRect;
            float h = Mathf.Min(rect.height, rect.width * image.rectTransform.sizeDelta.y / image.rectTransform.sizeDelta.x);
            image.uvRect = new Rect(rect.x / sprite.texture.width, (rect.y + (rect.height-h)*.35f) / sprite.texture.height,
                rect.width / sprite.texture.width, h / sprite.texture.height);
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Clipped, staggered upward drift, matching the home lightning motion.</summary>
    public sealed class ModeSkullField : MonoBehaviour
    {
        [SerializeField] private float _speed = 16f;
        [SerializeField] private float _wrapHeight = 612f;
        [SerializeField] private float _top = 306f;
        private RectTransform[] _tiles;
        private Image[] _images;

        private void Awake()
        {
            _tiles = new RectTransform[transform.childCount];
            _images = new Image[_tiles.Length];
            for (int i = 0; i < _tiles.Length; i++)
            {
                _tiles[i] = (RectTransform)transform.GetChild(i);
                _images[i] = _tiles[i].GetComponent<Image>();
            }
        }

        private void Update()
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                var p = _tiles[i].anchoredPosition;
                p.y += _speed * Time.unscaledDeltaTime;
                if (p.y > _top) p.y -= _wrapHeight;
                _tiles[i].anchoredPosition = p;
                // The supplied PNG already contains its translucent artwork.
                float edge = Mathf.Abs(p.y) / _top;
                _images[i].color = new Color(1f, 1f, 1f, 1f - .75f * Mathf.SmoothStep(.7f, 1f, edge));
            }
        }
    }
}

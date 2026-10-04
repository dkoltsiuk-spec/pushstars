using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Supplied red/blue artwork for the two-fighter screens.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ReadyScreenGraphic : MaskableGraphic
    {
        [SerializeField] private Texture2D _backgroundOverride;
        private Texture2D _background;

        public void SetBackground(Texture2D texture)
        {
            _backgroundOverride = texture;
            SetMaterialDirty();
        }
        public override Texture mainTexture => _backgroundOverride != null ? _backgroundOverride : _background != null
            ? _background : (_background = Resources.Load<Texture2D>("Rewards/DuelBackground"));

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            DrawBackground(vh, rectTransform.rect);
        }

        internal static void DrawBackground(VertexHelper vh, Rect r)
        {
            vh.Clear();
            vh.AddVert(new Vector3(r.xMin, r.yMin), Color.white, new Vector2(0, 0));
            vh.AddVert(new Vector3(r.xMax, r.yMin), Color.white, new Vector2(1, 0));
            vh.AddVert(new Vector3(r.xMax, r.yMax), Color.white, new Vector2(1, 1));
            vh.AddVert(new Vector3(r.xMin, r.yMax), Color.white, new Vector2(0, 1));
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
        }
    }
}

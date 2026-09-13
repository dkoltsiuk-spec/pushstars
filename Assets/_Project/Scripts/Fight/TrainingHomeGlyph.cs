using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Small house glyph with the same solid black backing as the labels.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TrainingHomeGlyph : MaskableGraphic
    {
        private static readonly Vector2[] Shape = { new Vector2(0, .5f), new Vector2(.5f, 1), new Vector2(1, .5f), new Vector2(.85f, .5f), new Vector2(.85f, 0), new Vector2(.15f, 0), new Vector2(.15f, .5f) };
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); Fan(vh, 1, Color.black); Fan(vh, .73f, Color.white);
            var r = GetPixelAdjustedRect(); int start = vh.currentVertCount;
            foreach (var p in new[] { new Vector2(.43f, .13f), new Vector2(.57f, .13f), new Vector2(.57f, .38f), new Vector2(.43f, .38f) })
                vh.AddVert(new Vector3(r.x + p.x * r.width, r.y + p.y * r.height), Color.black, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
        }
        private void Fan(VertexHelper vh, float scale, Color tint)
        {
            var r = GetPixelAdjustedRect(); int start = vh.currentVertCount;
            vh.AddVert(r.center, tint, Vector2.zero);
            foreach (var p in Shape) vh.AddVert(r.center + Vector2.Scale(p - Vector2.one * .5f, r.size) * scale, tint, Vector2.zero);
            for (int i = 0; i < Shape.Length; i++) vh.AddTriangle(start, start + i + 1, start + (i + 1) % Shape.Length + 1);
        }
    }
}

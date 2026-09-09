using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    // UI-native geometry keeps the gradient crisp at any resolution — no sprite to resample.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ReadyScreenGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            // Vertical gradient only — pink-red at the top, through violet, to deep blue at the
            // bottom. The drifting lightning that used to be faked here with static triangles is a
            // real animated LightningField now, layered over this by DuelReadyPanel.
            Color bottom = new Color32(13, 18, 136, 255);
            Color middle = new Color32(60, 49, 84, 255);
            Color top = new Color32(104, 21, 43, 255);
            const int rows = 40;
            for (int y = 0; y < rows; y++)
            {
                float a = y / (float)rows, b = (y + 1f) / rows;
                Color ca = a < 0.55f ? Color.Lerp(bottom, middle, a / 0.55f) : Color.Lerp(middle, top, (a - 0.55f) / 0.45f);
                Color cb = b < 0.55f ? Color.Lerp(bottom, middle, b / 0.55f) : Color.Lerp(middle, top, (b - 0.55f) / 0.45f);
                int n = vh.currentVertCount;
                vh.AddVert(new Vector3(r.xMin, r.yMin + r.height * a), ca, Vector2.zero);
                vh.AddVert(new Vector3(r.xMax, r.yMin + r.height * a), ca, Vector2.zero);
                vh.AddVert(new Vector3(r.xMax, r.yMin + r.height * b), cb, Vector2.zero);
                vh.AddVert(new Vector3(r.xMin, r.yMin + r.height * b), cb, Vector2.zero);
                vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n, n + 2, n + 3);
            }
        }
    }
}

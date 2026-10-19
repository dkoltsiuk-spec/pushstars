using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Procedural ledge between the boss and player halves: a lumpy cap over a coloured band, short drips
    /// and a soft shadow. Defaults are the ice island's snow over ice with icicles; the lava island recolours it
    /// into a basalt crust over glowing magma.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class IslandLedgeGraphic : MaskableGraphic
    {
        public Color Cap = new Color32(240, 249, 255, 255), CapShade = new Color32(188, 220, 244, 255);
        public Color BandTop = new Color32(98, 170, 222, 255), BandBottom = new Color32(26, 68, 128, 255);
        public Color Shadow = new Color32(6, 18, 40, 130);
        [Range(0, 1)] public float CapDepth = .42f;
        public float ShadowDepth = 16, DripSpacing = 46;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect r = rectTransform.rect;
            float capBase = r.yMax - r.height * CapDepth;
            int columns = Mathf.Max(8, Mathf.CeilToInt(r.width / 6));
            Color32 clear = new Color(Shadow.r, Shadow.g, Shadow.b, 0);
            for (int i = 0; i <= columns; i++)
            {
                float x = Mathf.Lerp(r.xMin, r.xMax, i / (float)columns);
                float lump = Mathf.Sin(x * .047f + .6f) * 2.6f + Mathf.Sin(x * .113f + 2.1f) * 1.6f + Mathf.Sin(x * .29f) * .6f;
                // Six rows per column; the shadow, band and cap quads sit between rows 0-1, 2-3 and 4-5.
                mesh.AddVert(new Vector3(x, r.yMin - ShadowDepth), clear, Vector2.zero);
                mesh.AddVert(new Vector3(x, r.yMin), Shadow, Vector2.zero);
                mesh.AddVert(new Vector3(x, r.yMin), BandBottom, Vector2.zero);
                mesh.AddVert(new Vector3(x, capBase), BandTop, Vector2.zero);
                mesh.AddVert(new Vector3(x, capBase), CapShade, Vector2.zero);
                mesh.AddVert(new Vector3(x, r.yMax + lump), Cap, Vector2.zero);
                if (i == columns) continue;
                int a = i * 6, b = a + 6;
                for (int row = 0; row < 6; row += 2)
                {
                    mesh.AddTriangle(a + row, a + row + 1, b + row + 1);
                    mesh.AddTriangle(b + row + 1, b + row, a + row);
                }
            }
            // Short drips hang into the player half at uneven, deterministic spacing.
            for (float x = r.xMin + DripSpacing * .4f; x < r.xMax - 6; x += DripSpacing)
            {
                float h = Mathf.Abs(Mathf.Sin(x * 12.9898f) * 43758.5453f) % 1;
                float cx = x + (h - .5f) * DripSpacing * .5f, width = 4 + h * 4, length = 6 + h * 11;
                int start = mesh.currentVertCount;
                mesh.AddVert(new Vector3(cx - width, r.yMin + 1), BandBottom, Vector2.zero);
                mesh.AddVert(new Vector3(cx + width, r.yMin + 1), BandBottom, Vector2.zero);
                mesh.AddVert(new Vector3(cx + width * .15f, r.yMin - length), new Color(BandTop.r, BandTop.g, BandTop.b, .55f), Vector2.zero);
                mesh.AddTriangle(start, start + 2, start + 1);
            }
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Small vector mesh: tapered lightning, layered medal, shockwave and seeded debris.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RepMilestoneGraphic : MaskableGraphic
    {
        private float _time, _unit, _pop, _exit;
        private Vector2 _centre;
        private static readonly Color Gold = new Color32(255, 201, 48, 255);
        private static readonly Color LightGold = new Color32(255, 241, 163, 255);
        private static readonly Color Ink = new Color32(38, 22, 74, 255);
        private static readonly Color Purple = new Color32(137, 92, 255, 255);
        private static readonly Color Cyan = new Color32(80, 238, 226, 255);

        // Uneven, authored silhouette stations. Each pair shares vertices with its neighbours:
        // there are no independently capped line segments at the lightning's bends.
        private static readonly float[] BoltY = { 0, .13f, .30f, .335f, .52f, .555f, .76f, .80f, .93f, 1 };
        private static readonly float[] BoltX = { 0, -9, 20, -2, -28, -9, 15, -12, -1, 0 };
        private static readonly float[] BoltWidth = { 0, 4, 10, 5, 17, 8, 24, 14, 32, 0 };
        private readonly Vector2[] _edgeLeft = new Vector2[12];
        private readonly Vector2[] _edgeRight = new Vector2[12];

        public void Draw(float seconds, Vector2 centre, float unit, float pop, float exit)
        { _time = seconds; _centre = centre; _unit = unit; _pop = pop; _exit = exit; SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_time <= 0 || _time >= RepMilestoneEffect.Duration) return;
            float hit = Mathf.Max(0, _time - .24f);
            float fade = 1 - _exit * _exit;
            if (_time < .64f) Lightning(vh);
            if (_time < .24f) return;
            float expand = 1 - Mathf.Pow(1 - Mathf.Clamp01(hit / .55f), 3);
            Ring(vh, _centre, (65 + 113 * expand) * _unit, (1 - expand) * 13 * _unit + _unit, Tint(Cyan, (1 - expand) * .85f));
            // Rotating, tapered sun rays behind the emblem.
            float rayFade = (1 - Mathf.Clamp01(hit / .8f)) * .7f;
            for (int i = 0; i < 10; i++)
            {
                float angle = i * Mathf.PI / 5 + hit * .18f;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var normal = new Vector2(-direction.y, direction.x);
                Triangle(vh, _centre + direction * 65 * _unit + normal * 8 * _unit,
                    _centre + direction * (110 + expand * 50) * _unit,
                    _centre + direction * 65 * _unit - normal * 8 * _unit, Tint(Gold, rayFade));
            }
            float s = Mathf.Max(.01f, _pop) * _unit * (1 - .1f * _exit);
            Vector2 c = _centre + Vector2.up * (_exit * _exit * 38 * _unit);
            Medal(vh, c + Vector2.down * 8 * s, 101 * s, 84 * s, Tint(Ink, fade));
            Medal(vh, c, 98 * s, 81 * s, Tint(Gold, fade));
            Medal(vh, c + Vector2.up * 3 * s, 88 * s, 73 * s, Tint(LightGold, fade));
            Medal(vh, c, 82 * s, 68 * s, Tint(Purple, fade));
            Medal(vh, c + Vector2.up * 6 * s, 74 * s, 62 * s, Tint(new Color32(113, 69, 232, 255), fade));
            // Fixed seeds avoid visual jitter and Unity's shared random state.
            for (int i = 0; i < 26; i++)
            {
                float life = Mathf.Clamp01(hit / (i % 3 == 0 ? .95f : .72f));
                float angle = i * 2.399963f;
                float distance = (65 + (65 + i % 5 * 13) * (1 - Mathf.Pow(1 - life, 2))) * _unit;
                var p = _centre + new Vector2(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance - 60 * life * life * _unit);
                float size = (i % 3 == 0 ? 6 : 3.5f) * _unit * (1 - life);
                Color tint = Tint(i % 3 == 0 ? Cyan : i % 3 == 1 ? Gold : LightGold, (1 - life) * fade);
                if (i % 3 == 0) Star(vh, p, size * 1.7f, size * .35f, tint);
                else Spark(vh, p, size * .65f, size * 2.3f, angle + life * 2, tint);
            }
        }

        private void Lightning(VertexHelper vh)
        {
            float head = Mathf.Clamp01(_time / .23f);
            head = 1 - Mathf.Pow(1 - head, 2);
            float tail = Mathf.Pow(Mathf.Clamp01((_time - .27f) / .37f), 1.5f);
            if (head <= tail) return;
            float intensity = Mathf.Clamp01(_time / .045f) * (1 - Mathf.Clamp01((_time - .48f) / .16f));
            float swell = 1 + .22f * Mathf.Sin(Mathf.Clamp01((_time - .14f) / .23f) * Mathf.PI);
            int count = 0;
            BoltStation(tail, head, tail, swell, ref count);
            for (int i = 1; i < BoltY.Length - 1; i++)
                if (BoltY[i] > tail && BoltY[i] < head) BoltStation(BoltY[i], head, tail, swell, ref count);
            BoltStation(head, head, tail, swell, ref count);

            // Soft outer fringe and a narrow bright heart follow the SAME connected contour.
            Ribbon(vh, count, 1.22f, Tint(Gold, 0), Tint(Gold, intensity * .14f), true);
            Ribbon(vh, count, 1f, Tint(new Color32(255, 179, 28, 255), intensity), Tint(Gold, intensity), false);
            Ribbon(vh, count, .43f, Tint(LightGold, intensity), Tint(new Color32(255, 253, 222, 255), intensity), false);

            // Brief forks peel away from the main stroke, with needle tips, not square caps.
            float fork = Mathf.Sin(Mathf.Clamp01((_time - .14f) / .4f) * Mathf.PI);
            if (fork > .01f)
            {
                Fork(vh, new Vector2(-14, -135), new Vector2(-54, -157), new Vector2(-40, -181), new Vector2(-75, -204), 8 * fork, Tint(Gold, intensity * fork));
                Fork(vh, new Vector2(6, -75), new Vector2(44, -105), new Vector2(37, -124), new Vector2(68, -142), 6 * fork, Tint(LightGold, intensity * fork));
            }
        }

        private void BoltStation(float y, float head, float tail, float swell, ref int count)
        {
            int segment = 0;
            while (segment < BoltY.Length - 2 && y > BoltY[segment + 1]) segment++;
            float blend = Mathf.InverseLerp(BoltY[segment], BoltY[segment + 1], y);
            float x = Mathf.Lerp(BoltX[segment], BoltX[segment + 1], blend);
            float width = Mathf.Lerp(BoltWidth[segment], BoltWidth[segment + 1], blend);
            // Coherent deformation: the silhouette flexes; its joints never separate.
            x += Mathf.Sin(y * 11 + _time * 19) * 3 * Mathf.Sin(y * Mathf.PI);
            float tip = head < .999f ? Mathf.Clamp01((head - y) / .1f) : 1;
            float taper = Mathf.Clamp01((y - tail) / .14f);
            width *= swell * tip * taper;
            var p = _centre + new Vector2(x, -280 * (1 - y)) * _unit;
            _edgeLeft[count] = p + Vector2.left * width * _unit;
            _edgeRight[count] = p + Vector2.right * width * _unit;
            count++;
        }

        private void Ribbon(VertexHelper vh, int count, float scale, Color left, Color right, bool fringe)
        {
            int start = vh.currentVertCount;
            for (int i = 0; i < count; i++)
            {
                var middle = (_edgeLeft[i] + _edgeRight[i]) * .5f;
                var half = (_edgeRight[i] - _edgeLeft[i]) * .5f;
                if (fringe)
                {
                    // Four connected rows feather each edge to transparent.
                    var feather = Vector2.right * (2 * _unit);
                    vh.AddVert(middle - half * scale - feather, left, Vector2.zero);
                    vh.AddVert(middle - half, right, Vector2.zero);
                    vh.AddVert(middle + half, right, Vector2.zero);
                    vh.AddVert(middle + half * scale + feather, left, Vector2.zero);
                }
                else
                {
                    vh.AddVert(middle - half * scale, left, Vector2.zero);
                    vh.AddVert(middle + half * scale, right, Vector2.zero);
                }
            }
            int stride = fringe ? 4 : 2;
            for (int i = 0; i < count - 1; i++)
                for (int j = 0; j < stride - 1; j++)
                {
                    int n = start + i * stride + j;
                    vh.AddTriangle(n, n + stride, n + stride + 1);
                    vh.AddTriangle(n, n + stride + 1, n + 1);
                }
        }

        private void Fork(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 tip, float width, Color tint)
        {
            a = _centre + a * _unit; b = _centre + b * _unit;
            c = _centre + c * _unit; tip = _centre + tip * _unit;
            Vector2 offset = Vector2.right * width * _unit;
            Triangle(vh, a - offset, a + offset, b + offset * .55f, tint);
            Triangle(vh, a - offset, b + offset * .55f, b - offset * .55f, tint);
            Triangle(vh, b - offset * .55f, b + offset * .55f, c + offset * .3f, tint);
            Triangle(vh, b - offset * .55f, c + offset * .3f, c - offset * .3f, tint);
            Triangle(vh, c - offset * .3f, c + offset * .3f, tip, tint);
        }

        private static Color Tint(Color c, float alpha) { c.a = Mathf.Clamp01(alpha); return c; }
        private static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color tint)
        {
            int n = vh.currentVertCount;
            vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero); vh.AddVert(c, tint, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2);
        }
        private static void Spark(VertexHelper vh, Vector2 c, float x, float y, float angle, Color tint)
        {
            var a = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)); var b = new Vector2(-a.y, a.x);
            Triangle(vh, c - b * y, c - a * x, c + b * y, tint);
            Triangle(vh, c - b * y, c + b * y, c + a * x, tint);
        }
        private static void Medal(VertexHelper vh, Vector2 c, float outer, float inner, Color tint)
        {
            for (int i = 0; i < 20; i++)
            {
                float a = i * Mathf.PI / 10 + Mathf.PI / 2, b = (i + 1) * Mathf.PI / 10 + Mathf.PI / 2;
                float ra = i % 2 == 0 ? outer : inner, rb = i % 2 == 0 ? inner : outer;
                Triangle(vh, c, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ra, c + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * rb, tint);
            }
        }
        private static void Star(VertexHelper vh, Vector2 c, float outer, float inner, Color tint)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4, b = (i + 1) * Mathf.PI / 4;
                Triangle(vh, c, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? outer : inner),
                    c + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * (i % 2 == 0 ? inner : outer), tint);
            }
        }
        private static void Ring(VertexHelper vh, Vector2 c, float radius, float width, Color tint)
        {
            for (int i = 0; i < 64; i++)
            {
                float a = i * Mathf.PI / 32, b = (i + 1) * Mathf.PI / 32;
                var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); var q = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                Triangle(vh, c + p * radius, c + q * radius, c + q * (radius - width), tint);
                Triangle(vh, c + p * radius, c + q * (radius - width), c + p * (radius - width), tint);
            }
        }
    }
}

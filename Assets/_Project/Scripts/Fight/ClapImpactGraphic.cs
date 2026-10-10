using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>
    /// The duel's clap push-up landing as one procedural mesh: the screen-wide flash when the
    /// clap is confirmed, then — once the character's palms are back on the floor — the
    /// shockwave that leaves them and rolls up the floor into the opponent's half, the red
    /// hit over that half when the front arrives, and — on the last paid clap — a bolt out of
    /// the sky onto the opponent. Before the landing it only darkens the edges of the player's half while
    /// the hands are in the air. No textures; white plus the Aura violet, nothing else.
    /// <see cref="ClapImpactEffect"/> owns the clock and feeds every frame through
    /// <see cref="Draw"/>, in this graphic's own local units.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ClapImpactGraphic : MaskableGraphic
    {
        public const float FlashSec = .09f, RingSec = .52f, HitSec = .34f, BoltSec = .42f;
        // The wave lies on the floor: a circle seen from the stage camera's low angle.
        private const float Squash = .62f, RingStart = 30f, RingEnd = 700f;
        private const int Segments = 56;
        private static readonly Color Core = new Color(1f, .98f, 1f), Violet = new Color(.62f, .3f, 1f);
        private static readonly Color Hurt = new Color(1f, .14f, .2f), Shade = new Color(.02f, .01f, .07f);
        // Sideways kinks of the bolt, as a share of its length; both ends stay on their fighter.
        private static readonly float[] BoltKink = { 0f, .13f, -.07f, .16f, -.14f, .06f, -.12f, .1f, -.04f, 0f };

        private float _anticipation, _flash = -1f, _age = -1f, _unit = 1f, _hitAt;
        private bool _finisher;
        private Vector2 _origin, _target;
        private Rect _player, _opponent;
        // The bolt lands when the wave does, so the two read as one blow.
        private float BoltAge => _age - _hitAt;

        /// <summary>Seconds after the landing at which the wave front has travelled
        /// <paramref name="distance"/> straight up the screen from the palms.</summary>
        public static float FrontReachSec(float distance, float unit)
        {
            float e = Mathf.Clamp01((distance / (Squash * unit) - RingStart) / (RingEnd - RingStart));
            return (1f - Mathf.Pow(1f - e, 1f / 3f)) * RingSec;
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        /// <param name="anticipation">0–1: hands in the air, palms not planted yet.</param>
        /// <param name="flash">Seconds since the clap was confirmed; negative before it.</param>
        /// <param name="age">Seconds since the palms planted; negative before it.</param>
        /// <param name="hitAt">Age at which the wave reaches the opponent's half.</param>
        public void Draw(float anticipation, float flash, float age, bool finisher, float unit, float hitAt,
            Vector2 origin, Vector2 target, Rect player, Rect opponent)
        {
            _anticipation = anticipation; _flash = flash; _age = age; _finisher = finisher; _unit = unit; _hitAt = hitAt;
            _origin = origin; _target = target; _player = player; _opponent = opponent;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_anticipation > .001f && _age < 0f) Anticipation(vh);
            if (_age >= 0f)
            {
                OpponentHit(vh);
                Wave(vh, _age, 1f);
                Wave(vh, _age - .07f, .55f);
                if (_finisher) Bolt(vh);
                Flare(vh);
            }
            float flashSec = _finisher ? FlashSec * 1.5f : FlashSec;
            if (_flash >= 0f && _flash < flashSec)
            {
                var r = rectTransform.rect;
                // Kept low: the project blends in linear space, where this much white already
                // reads as a camera flash, and a full white-out five times a fight is too much.
                Quad(vh, r, Tint(Core, (_finisher ? .32f : .22f) * (1f - _flash / flashSec)));
            }
        }

        /// <summary>The player's half closes in at the sides while the body is off the floor.</summary>
        private void Anticipation(VertexHelper vh)
        {
            float a = _anticipation;
            Quad(vh, _player, Tint(Shade, .14f * a));
            float band = _player.width * .42f;
            Color edge = Tint(Shade, .52f * a), clear = Tint(Shade, 0f);
            Gradient(vh, new Rect(_player.xMin, _player.yMin, band, _player.height), edge, clear, true);
            Gradient(vh, new Rect(_player.xMax - band, _player.yMin, band, _player.height), clear, edge, true);
        }

        private void Wave(VertexHelper vh, float age, float strength)
        {
            if (age <= 0f || age >= RingSec) return;
            float x = age / RingSec, e = 1f - (1f - x) * (1f - x) * (1f - x);
            float radius = Mathf.Lerp(RingStart, RingEnd, e) * _unit;
            float alpha = Mathf.Pow(1f - x, 1.3f) * strength;
            float width = Mathf.Lerp(30f, 7f, e) * _unit;
            Ellipse(vh, radius, width * 2.6f, Tint(Violet, .5f * alpha));
            Ellipse(vh, radius, width, Tint(Core, alpha));
        }

        /// <summary>A soft-edged ring: clear inside, full at <paramref name="radius"/>, clear outside.</summary>
        private void Ellipse(VertexHelper vh, float radius, float width, Color tint)
        {
            int first = vh.currentVertCount;
            Color clear = Tint(tint, 0f);
            for (int i = 0; i <= Segments; i++)
            {
                float a = i * 2f * Mathf.PI / Segments;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Squash);
                vh.AddVert(_origin + dir * Mathf.Max(0f, radius - width), clear, Vector2.zero);
                vh.AddVert(_origin + dir * radius, tint, Vector2.zero);
                vh.AddVert(_origin + dir * (radius + width * .5f), clear, Vector2.zero);
                if (i == 0) continue;
                int v = first + i * 3;
                vh.AddTriangle(v - 3, v, v + 1); vh.AddTriangle(v - 3, v + 1, v - 2);
                vh.AddTriangle(v - 2, v + 1, v + 2); vh.AddTriangle(v - 2, v + 2, v - 1);
            }
        }

        /// <summary>The hit spark where the palms land: a wide, flat four-point star.</summary>
        private void Flare(VertexHelper vh)
        {
            const float sec = .2f;
            if (_age >= sec) return;
            float u = _age / sec, fade = (1f - u) * (1f - u);
            float wide = Mathf.Lerp(150f, 250f, u) * _unit, tall = 34f * fade * _unit;
            Diamond(vh, _origin, wide * 1.15f, tall * 2.2f, Tint(Violet, .45f * fade));
            Diamond(vh, _origin, wide, tall, Tint(Core, fade));
            Diamond(vh, _origin, tall * 1.1f, Mathf.Lerp(70f, 120f, u) * fade * _unit, Tint(Core, fade));
        }

        /// <summary>The wave arrives: the opponent's half takes it from the seam upwards.</summary>
        private void OpponentHit(VertexHelper vh)
        {
            float since = _age - _hitAt;
            if (_opponent.width <= 0f || since < 0f || since >= HitSec) return;
            float fade = 1f - since / HitSec;
            float a = (_finisher ? .5f : .36f) * fade * fade;
            Gradient(vh, _opponent, Tint(Hurt, a), Tint(Hurt, a * .25f), false);
        }

        private void Bolt(VertexHelper vh)
        {
            float age = BoltAge;
            if (_opponent.width <= 0f || age < 0f || age >= BoltSec) return;
            float head = Mathf.Clamp01(age / .06f);
            float fade = 1f - Mathf.Clamp01((age - .14f) / (BoltSec - .14f));
            // Re-strikes: the kinks jump twice while it burns out instead of sliding.
            float flicker = age < .1f ? 1f : age < .22f ? -.7f : .5f;
            // Down from above the opponent's half, never across the player's own body.
            var from = new Vector2(_target.x + 46f * _unit, _opponent.yMax + 12f * _unit);
            Vector2 along = _target - from, side = new Vector2(-along.y, along.x);
            float width = 11f * _unit * (.6f + .4f * fade);
            Ribbon(vh, from, along, side, head, flicker, width * 3.4f, Tint(Violet, .42f * fade));
            Ribbon(vh, from, along, side, head, flicker, width * 1.7f, Tint(Violet, .8f * fade));
            Ribbon(vh, from, along, side, head, flicker, width, Tint(Core, fade));
        }

        private static void Ribbon(VertexHelper vh, Vector2 from, Vector2 along, Vector2 side, float head, float flicker, float width, Color tint)
        {
            int first = vh.currentVertCount, last = BoltKink.Length - 1;
            Vector2 across = side.normalized;
            for (int i = 0; i <= last; i++)
            {
                float t = head * i / last;
                Vector2 p = from + along * t + side * (BoltKink[i] * flicker);
                float half = .5f * width * (i == 0 || i == last ? .25f : 1f);
                vh.AddVert(p + across * half, tint, Vector2.zero);
                vh.AddVert(p - across * half, tint, Vector2.zero);
                if (i == 0) continue;
                int v = first + i * 2;
                vh.AddTriangle(v - 2, v, v - 1);
                vh.AddTriangle(v - 1, v, v + 1);
            }
        }

        private static void Quad(VertexHelper vh, Rect r, Color tint) => Gradient(vh, r, tint, tint, false);

        /// <summary>Two-colour quad: first→second runs left→right when <paramref name="horizontal"/>,
        /// bottom→top otherwise.</summary>
        private static void Gradient(VertexHelper vh, Rect r, Color first, Color second, bool horizontal)
        {
            if (first.a <= .001f && second.a <= .001f) return;
            int n = vh.currentVertCount;
            vh.AddVert(new Vector2(r.xMin, r.yMin), first, Vector2.zero);
            vh.AddVert(new Vector2(r.xMin, r.yMax), horizontal ? first : second, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMax), second, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMin), horizontal ? second : first, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2);
            vh.AddTriangle(n, n + 2, n + 3);
        }

        private static void Diamond(VertexHelper vh, Vector2 c, float halfWidth, float halfHeight, Color tint)
        {
            if (tint.a <= .001f) return;
            int n = vh.currentVertCount;
            vh.AddVert(c + Vector2.left * halfWidth, tint, Vector2.zero);
            vh.AddVert(c + Vector2.up * halfHeight, tint, Vector2.zero);
            vh.AddVert(c + Vector2.right * halfWidth, tint, Vector2.zero);
            vh.AddVert(c + Vector2.down * halfHeight, tint, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2);
            vh.AddTriangle(n, n + 2, n + 3);
        }

        private static Color Tint(Color c, float alpha) { c.a = Mathf.Clamp01(alpha); return c; }
    }
}

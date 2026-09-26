using PushStars.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>
    /// The clap push-up's double claw strike: two crossing swipes of three tapered claw marks,
    /// white-hot core over a red glow, drawn on in a flick and fading out. Procedural mesh — no
    /// textures. <see cref="Play"/> takes Content units; the graphic's own rect is ignored.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ClawSlashGraphic : MaskableGraphic
    {
        private const float DrawSec = .09f, SecondSwipeDelay = .12f, HoldSec = .16f, FadeSec = .38f;
        private const int Samples = 14;
        private static readonly Color Core = new Color(1f, .97f, .95f), Glow = new Color(.96f, .06f, .12f);

        private Vector2 _center;
        private float _length, _startedAt = -100f;
        private bool _secondSounded;

        /// <summary>Seconds since the strike started; negative/large when idle.</summary>
        private float Age => Time.unscaledTime - _startedAt;
        public const float Duration = SecondSwipeDelay + DrawSec + HoldSec + FadeSec;
        public bool Playing => Age < Duration;
        private bool _meshLive;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        public void Play(Vector2 center, float length)
        {
            _center = center;
            _length = length;
            _startedAt = Time.unscaledTime;
            _secondSounded = false;
            GameAudio.Play(SoundCue.AuraWhoosh, 1.25f);
            SetVerticesDirty();
        }

        private void Update()
        {
            if (!Playing)
            {
                if (_meshLive) { _meshLive = false; SetVerticesDirty(); } // clear the last frame
                return;
            }
            _meshLive = true;
            if (!_secondSounded && Age >= SecondSwipeDelay)
            {
                _secondSounded = true;
                GameAudio.Play(SoundCue.AuraWhoosh, 1.45f);
            }
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (!Playing) return;
            // First swipe top-right → bottom-left, the second crosses it top-left → bottom-right.
            Swipe(mesh, Age, -128f);
            Swipe(mesh, Age - SecondSwipeDelay, -52f);
        }

        private void Swipe(VertexHelper mesh, float age, float angleDeg)
        {
            if (age <= 0f) return;
            float draw = Mathf.Clamp01(age / DrawSec);
            float head = 1f - (1f - draw) * (1f - draw) * (1f - draw); // ease-out flick
            float fade = 1f - Mathf.Clamp01((age - DrawSec - HoldSec) / FadeSec);
            if (fade <= 0f) return;
            // Just drawn: a hot flash; while fading the glow blooms a little wider.
            float flash = 1f + .6f * (1f - Mathf.Clamp01((age - DrawSec) / .1f)) * (draw >= 1f ? 1f : 0f);
            float bloom = 1f + .5f * (1f - fade);

            float a = angleDeg * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var side = new Vector2(-dir.y, dir.x);
            float spacing = _length * .14f;
            for (int m = -1; m <= 1; m++)
            {
                float len = _length * (m == 0 ? 1f : .84f);
                Vector2 mid = _center + side * (m * spacing) + dir * (m * _length * .03f);
                Vector2 from = mid - dir * (len * .5f), to = mid + dir * (len * .5f);
                float width = _length * (m == 0 ? .04f : .032f);
                Stroke(mesh, from, to, side, head, width * 3.6f * bloom, WithAlpha(Glow, .38f * fade));
                Stroke(mesh, from, to, side, head, width * 1.9f, WithAlpha(Glow, .85f * fade));
                Stroke(mesh, from, to, side, head, width * flash, WithAlpha(Core, fade));
            }
        }

        /// <summary>A claw mark from <paramref name="from"/> drawn up to <paramref name="head"/> ∈
        /// [0,1] of its length, bowed slightly and tapered to points at both ends.</summary>
        private static void Stroke(VertexHelper mesh, Vector2 from, Vector2 to, Vector2 side,
            float head, float width, Color color)
        {
            if (head <= .02f || color.a <= .001f) return;
            float bow = Vector2.Distance(from, to) * .06f;
            int first = mesh.currentVertCount;
            for (int i = 0; i <= Samples; i++)
            {
                float t = head * i / Samples;
                Vector2 p = Vector2.Lerp(from, to, t) + side * (bow * Mathf.Sin(Mathf.PI * t));
                // Full-length taper, so the drawing tip is already sharp mid-flick.
                float half = .5f * width * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(t)), .55f);
                if (i == Samples) half = Mathf.Min(half, .5f * width * .35f);
                mesh.AddVert(p + side * half, color, Vector2.zero);
                mesh.AddVert(p - side * half, color, Vector2.zero);
                if (i == 0) continue;
                int v = first + i * 2;
                mesh.AddTriangle(v - 2, v, v - 1);
                mesh.AddTriangle(v - 1, v, v + 1);
            }
        }

        private static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    }
}

using System;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>
    /// A rep's blow made visible: a glowing orb with a comet tail flies from the player's avatar
    /// to the boss and bursts on arrival, so it is clear who is hitting whom. Procedural mesh,
    /// no textures; <see cref="Launch"/> takes Content units (the graphic's own rect is ignored).
    /// Up to <see cref="Slots"/> bolts fly at once; the oldest is dropped when all are busy.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class StrikeBoltGraphic : MaskableGraphic
    {
        public const float FlightSec = .17f;
        private const float BurstSec = .24f, TailSec = .11f;
        private const int Slots = 4, TailSamples = 10, Ring = 18;
        private static readonly Color Core = new Color(1f, .98f, .9f), Glow = new Color(1f, .72f, .16f);

        private struct Bolt
        {
            public Vector2 From, Control, To;
            public float StartedAt, Size;
            public bool Landed, Live;
            public Action OnLand;
        }

        private readonly Bolt[] _bolts = new Bolt[Slots];
        private int _next, _side = 1;
        private bool _meshLive;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        /// <summary>Fly from <paramref name="from"/> to <paramref name="to"/>; <paramref name="onLand"/>
        /// fires the moment the bolt arrives. <paramref name="size"/> is the orb's diameter.</summary>
        public void Launch(Vector2 from, Vector2 to, float size, Action onLand)
        {
            var offset = to - from;
            // Alternate which way the flight bows so consecutive reps do not trace one line.
            _side = -_side;
            var control = (from + to) * .5f + new Vector2(-offset.y, offset.x).normalized * (offset.magnitude * .16f * _side);
            int slot = _next++ % Slots;
            _bolts[slot] = new Bolt { From = from, Control = control, To = to, StartedAt = Time.unscaledTime, Size = size, OnLand = onLand, Live = true };
            SetVerticesDirty();
        }

        private void Update()
        {
            bool any = false;
            for (int i = 0; i < Slots; i++)
            {
                if (!_bolts[i].Live) continue;
                float age = Time.unscaledTime - _bolts[i].StartedAt;
                if (!_bolts[i].Landed && age >= FlightSec)
                {
                    _bolts[i].Landed = true;
                    var land = _bolts[i].OnLand; _bolts[i].OnLand = null;
                    land?.Invoke();
                }
                if (age >= FlightSec + BurstSec) { _bolts[i].Live = false; continue; }
                any = true;
            }
            if (any) { _meshLive = true; SetVerticesDirty(); }
            else if (_meshLive) { _meshLive = false; SetVerticesDirty(); }
        }

        private Vector2 PointAt(in Bolt bolt, float t)
        {
            float u = 1f - t;
            return u * u * bolt.From + 2f * u * t * bolt.Control + t * t * bolt.To;
        }

        // Accelerates into the target: a strike, not a drift.
        private static float Eased(float t) => t * t * (1.7f - .7f * t);

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            for (int i = 0; i < Slots; i++)
            {
                if (!_bolts[i].Live) continue;
                float age = Time.unscaledTime - _bolts[i].StartedAt;
                if (age < FlightSec) DrawFlight(mesh, _bolts[i], age);
                else DrawBurst(mesh, _bolts[i], (age - FlightSec) / BurstSec);
            }
        }

        private void DrawFlight(VertexHelper mesh, in Bolt bolt, float age)
        {
            float head = Eased(Mathf.Clamp01(age / FlightSec));
            var pos = PointAt(bolt, head);
            // Tail: where the orb was a moment ago, thinning and fading the further back it goes.
            float tailStart = Eased(Mathf.Clamp01((age - TailSec) / FlightSec));
            for (int pass = 0; pass < 2; pass++)
            {
                float width = bolt.Size * (pass == 0 ? .95f : .5f);
                var color = pass == 0 ? Glow : Core;
                float alpha = pass == 0 ? .5f : .95f;
                Ribbon(mesh, bolt, tailStart, head, width, color, alpha);
            }
            Disc(mesh, pos, bolt.Size * 1.25f, Glow, .35f);
            Disc(mesh, pos, bolt.Size * .78f, Glow, .9f);
            Disc(mesh, pos, bolt.Size * .46f, Core, 1f);
        }

        /// <summary>Tapered strip along the flight path from <paramref name="from"/> to
        /// <paramref name="to"/> (path parameters), thin and transparent at the back.</summary>
        private void Ribbon(VertexHelper mesh, in Bolt bolt, float from, float to, float width, Color color, float alpha)
        {
            if (to - from < .01f) return;
            int first = mesh.currentVertCount;
            for (int i = 0; i <= TailSamples; i++)
            {
                float k = i / (float)TailSamples;
                float t = Mathf.Lerp(from, to, k);
                var p = PointAt(bolt, t);
                var ahead = PointAt(bolt, Mathf.Min(1f, t + .02f));
                var behind = PointAt(bolt, Mathf.Max(0f, t - .02f));
                var dir = (ahead - behind).normalized;
                var side = new Vector2(-dir.y, dir.x);
                float half = .5f * width * k;
                var c = color; c.a = alpha * k * k;
                mesh.AddVert(p + side * half, c, Vector2.zero);
                mesh.AddVert(p - side * half, c, Vector2.zero);
                if (i == 0) continue;
                int v = first + i * 2;
                mesh.AddTriangle(v - 2, v, v - 1);
                mesh.AddTriangle(v - 1, v, v + 1);
            }
        }

        /// <summary>Impact: a white flash, an expanding ring and short spokes flying outward.</summary>
        private void DrawBurst(VertexHelper mesh, in Bolt bolt, float u)
        {
            float fade = 1f - u;
            float size = bolt.Size;
            Disc(mesh, bolt.To, size * (1.2f + 1.2f * u), Core, .7f * fade * fade);
            float radius = size * (.9f + 2.8f * (1f - (1f - u) * (1f - u)));
            RingStroke(mesh, bolt.To, radius, size * .34f * fade, Glow, .9f * fade);
            for (int i = 0; i < 8; i++)
            {
                float angle = (i / 8f + .06f * bolt.StartedAt) * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var side = new Vector2(-dir.y, dir.x);
                float inner = radius * .65f, outer = radius * (1f + .55f * (1f - u));
                float half = size * .09f * fade;
                var c = Core; c.a = fade;
                int v = mesh.currentVertCount;
                mesh.AddVert(bolt.To + dir * inner + side * half, c, Vector2.zero);
                mesh.AddVert(bolt.To + dir * inner - side * half, c, Vector2.zero);
                var tip = c; tip.a = 0f;
                mesh.AddVert(bolt.To + dir * outer, tip, Vector2.zero);
                mesh.AddTriangle(v, v + 2, v + 1);
            }
        }

        private static void Disc(VertexHelper mesh, Vector2 center, float diameter, Color color, float alpha)
        {
            if (alpha <= .001f) return;
            color.a = alpha;
            int centerIndex = mesh.currentVertCount;
            mesh.AddVert(center, color, Vector2.zero);
            for (int i = 0; i < Ring; i++)
            {
                float a = i / (float)Ring * Mathf.PI * 2f;
                mesh.AddVert(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (diameter * .5f), color, Vector2.zero);
            }
            for (int i = 0; i < Ring; i++)
                mesh.AddTriangle(centerIndex, centerIndex + 1 + i, centerIndex + 1 + (i + 1) % Ring);
        }

        private static void RingStroke(VertexHelper mesh, Vector2 center, float radius, float width, Color color, float alpha)
        {
            if (alpha <= .001f || width <= .01f) return;
            color.a = alpha;
            int first = mesh.currentVertCount;
            for (int i = 0; i < Ring; i++)
            {
                float a = i / (float)Ring * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                mesh.AddVert(center + dir * (radius + width * .5f), color, Vector2.zero);
                mesh.AddVert(center + dir * (radius - width * .5f), color, Vector2.zero);
            }
            for (int i = 0; i < Ring; i++)
            {
                int a = first + i * 2, b = first + (i + 1) % Ring * 2;
                mesh.AddTriangle(a, b, a + 1);
                mesh.AddTriangle(a + 1, b, b + 1);
            }
        }
    }
}

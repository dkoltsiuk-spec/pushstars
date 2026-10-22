using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>
    /// A confetti explosion drawn as one UI mesh: paper bits fly out of a point, tumble (their width
    /// flips with a spin phase, like paper turning over), fall under gravity and fade. Stretch it over
    /// the screen and call <see cref="Burst"/>; it draws nothing while no confetti is alive.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ConfettiBurst : MaskableGraphic
    {
        private const float Gravity = 1100f, Drag = 1.6f, Life = 3.8f, FadeTime = .8f;

        private static readonly Color32[] Palette =
        {
            new Color32(255, 214, 46, 255), new Color32(255, 92, 140, 255), new Color32(80, 214, 255, 255),
            new Color32(140, 255, 90, 255), new Color32(255, 255, 255, 255), new Color32(176, 112, 255, 255),
            new Color32(255, 150, 40, 255),
        };

        private struct Bit
        {
            public Vector2 Position, Velocity, Size;
            public float Angle, Spin, Flip, FlipSpeed, Age;
            public Color32 Color;
        }

        private readonly List<Bit> _bits = new List<Bit>();

        public static ConfettiBurst Create(Transform parent)
        {
            var go = new GameObject("Confetti", typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var confetti = go.AddComponent<ConfettiBurst>();
            confetti.raycastTarget = false;
            return confetti;
        }

        /// <summary>Throws <paramref name="count"/> bits from <paramref name="origin"/> (local to this
        /// rect), aimed around <paramref name="direction"/> (degrees, 90 = up) within <paramref name="spread"/>.</summary>
        public void Burst(Vector2 origin, int count, float direction = 90f, float spread = 360f, float speed = 900f)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = (direction + Random.Range(-spread, spread) * .5f) * Mathf.Deg2Rad;
                float power = speed * Random.Range(.45f, 1.1f);
                _bits.Add(new Bit
                {
                    Position = origin + Random.insideUnitCircle * 8f,
                    Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * power,
                    Size = new Vector2(Random.Range(5f, 9f), Random.Range(9f, 15f)),
                    Angle = Random.Range(0f, 360f),
                    Spin = Random.Range(-540f, 540f),
                    Flip = Random.Range(0f, Mathf.PI * 2f),
                    FlipSpeed = Random.Range(6f, 14f),
                    Age = Random.Range(0f, .25f),
                    Color = Palette[Random.Range(0, Palette.Length)],
                });
            }
            SetVerticesDirty();
        }

        private void Update()
        {
            if (_bits.Count == 0) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            for (int i = _bits.Count - 1; i >= 0; i--)
            {
                var bit = _bits[i];
                bit.Age += dt;
                if (bit.Age >= Life) { _bits.RemoveAt(i); continue; }
                bit.Velocity *= Mathf.Exp(-Drag * dt);
                bit.Velocity.y -= Gravity * dt * .55f;
                // Paper flutters sideways as it falls.
                bit.Position += (bit.Velocity + new Vector2(Mathf.Sin(bit.Flip) * 40f, 0)) * dt;
                bit.Angle += bit.Spin * dt;
                bit.Flip += bit.FlipSpeed * dt;
                _bits[i] = bit;
            }
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            foreach (var bit in _bits)
            {
                var color = bit.Color;
                color.a = (byte)(255 * Mathf.Clamp01((Life - bit.Age) / FadeTime));
                // Turning paper: width follows the flip, and the back side is a little darker.
                float flip = Mathf.Cos(bit.Flip);
                if (flip < 0) { color.r = (byte)(color.r * .8f); color.g = (byte)(color.g * .8f); color.b = (byte)(color.b * .8f); }
                float halfW = bit.Size.x * .5f * Mathf.Max(.15f, Mathf.Abs(flip)), halfH = bit.Size.y * .5f;
                float rad = bit.Angle * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
                Vector2 right = new Vector2(cos, sin) * halfW, up = new Vector2(-sin, cos) * halfH;
                int start = vh.currentVertCount;
                vh.AddVert(bit.Position - right - up, color, Vector2.zero);
                vh.AddVert(bit.Position - right + up, color, Vector2.zero);
                vh.AddVert(bit.Position + right + up, color, Vector2.zero);
                vh.AddVert(bit.Position + right - up, color, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }
        }
    }
}

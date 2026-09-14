using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Bounded UI particle pool, drawn below HUD. All positions use Content units.</summary>
    public sealed class ForestParticleEffects : MonoBehaviour
    {
        public Texture2D[] Leaves, Petals;
        public Rect[] LeafUvs, PetalUvs;
        public Material ParticleMaterial;
        public const int Capacity = 64;
        public int ActivePetals { get; private set; }
        public int HitBursts { get; private set; }
        public int FallBursts { get; private set; }
        private sealed class Particle
        {
            public RawImage Image;
            public bool Active, Leaf;
            public float Age, Life, Angle, Spin, Phase, Opacity;
            public Vector2 Position, Velocity;
        }
        private readonly Particle[] _pool = new Particle[Capacity];
        private System.Random _random = new System.Random(7183);
        private RectTransform _rect;
        private float _nextLeaf, _fallIn = -1;
        private bool _fallQueued;
        private Vector2 _fallOrigin;
        private float Range(float min, float max) => Mathf.Lerp(min, max, (float)_random.NextDouble());

        private void OnEnable() { if (Application.isPlaying) ResetSimulation(); }
        private void OnDisable()
        {
            foreach (var p in _pool) if (p != null) { p.Active = false; if (p.Image) p.Image.gameObject.SetActive(false); }
            _fallIn = -1; _fallQueued = false; ActivePetals = 0;
        }
        private void Update() { if (Application.isPlaying) Advance(Mathf.Min(Time.unscaledDeltaTime, .05f)); }

        private void EnsurePool()
        {
            if (_rect == null) _rect = (RectTransform)transform;
            for (int i = 0; i < Capacity; i++)
            {
                if (_pool[i] != null) continue;
                var go = new GameObject("Particle" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                go.layer = gameObject.layer; go.transform.SetParent(transform, false);
                var image = go.GetComponent<RawImage>(); image.raycastTarget = false; image.material = ParticleMaterial;
                _pool[i] = new Particle { Image = image }; go.SetActive(false);
            }
        }

        public void ResetSimulation()
        {
            EnsurePool(); OnDisable(); _random = new System.Random(7183);
            HitBursts = FallBursts = 0; _nextLeaf = 1.7f;
            for (int i = 0; i < 5; i++) SpawnLeaf(true);
        }

        private Particle Allocate(bool leaf)
        {
            EnsurePool();
            foreach (var p in _pool) if (!p.Active) { p.Active = true; p.Leaf = leaf; p.Age = 0; return p; }
            // Petals may replace an old petal, but ambient leaves never replace impact particles.
            if (!leaf)
            {
                Particle oldest = null;
                foreach (var p in _pool) if (!p.Leaf && (oldest == null || p.Age > oldest.Age)) oldest = p;
                if (oldest != null) { oldest.Age = 0; return oldest; }
            }
            return null;
        }

        private void SetImage(Particle p, Texture2D texture, Rect uv, float length)
        {
            p.Image.texture = texture; p.Image.uvRect = uv;
            float aspect = texture.width * uv.width / (texture.height * uv.height);
            p.Image.rectTransform.sizeDelta = aspect >= 1 ? new Vector2(length, length / aspect) : new Vector2(length * aspect, length);
            p.Image.gameObject.SetActive(true);
            Draw(p);
        }

        private void SpawnLeaf(bool seed)
        {
            if (Leaves == null || Leaves.Length == 0) return;
            int live = 0; foreach (var item in _pool) if (item != null && item.Active && item.Leaf) live++;
            if (live >= 10) return;
            var p = Allocate(true); if (p == null) return;
            Rect r = _rect.rect;
            p.Position = new Vector2(Range(r.xMin + 14, r.xMax - 14), seed ? Range(r.yMin + 60, r.yMax - 35) : r.yMax + 22);
            p.Velocity = new Vector2(Range(-9, 9), -Range(28, 45));
            p.Life = (p.Position.y - r.yMin + 40) / -p.Velocity.y;
            p.Angle = Range(-180, 180); p.Spin = Range(-35, 35); p.Phase = Range(0, 6.28f); p.Opacity = Range(.45f, .7f);
            int index = _random.Next(Leaves.Length);
            SetImage(p, Leaves[index], LeafUvs[index], Range(13, 23));
        }

        public void Hit(Vector2 origin)
        {
            if (!isActiveAndEnabled || _fallQueued) return;
            HitBursts++; Burst(origin, false);
        }

        public void QueueFall(Vector2 ground, float delay)
        {
            if (!isActiveAndEnabled || _fallQueued) return;
            _fallQueued = true; _fallOrigin = ground; _fallIn = Mathf.Max(0, delay);
        }

        private void Burst(Vector2 origin, bool fall)
        {
            if (Petals == null || Petals.Length == 0) return;
            int count = fall ? 28 : 11;
            for (int i = 0; i < count; i++)
            {
                var p = Allocate(false); if (p == null) break;
                p.Position = origin + new Vector2(Range(fall ? -30 : -9, fall ? 30 : 9), Range(-4, 4));
                p.Velocity = new Vector2(Range(fall ? -155 : -85, fall ? 155 : 85), Range(fall ? 85 : 45, fall ? 170 : 125));
                p.Life = fall ? Range(1.25f, 1.9f) : Range(.75f, 1.25f);
                p.Angle = Range(-180, 180); p.Spin = Range(-230, 230); p.Phase = Range(0, 6.28f); p.Opacity = Range(.7f, .95f);
                // The whole flower is a rare accent on the ground burst; hits use loose petals.
                int index = fall && i == 0 ? Petals.Length - 1 : _random.Next(Mathf.Min(2, Petals.Length));
                SetImage(p, Petals[index], PetalUvs[index], index == 2 ? 17 : Range(7, 12));
            }
            CountPetals();
        }

        public void Advance(float delta)
        {
            if (!isActiveAndEnabled || delta <= 0) return;
            EnsurePool();
            if (_fallIn >= 0)
            {
                _fallIn -= delta;
                if (_fallIn <= 0) { _fallIn = -1; FallBursts++; Burst(_fallOrigin, true); }
            }
            _nextLeaf -= delta;
            if (_nextLeaf <= 0) { SpawnLeaf(false); _nextLeaf = Range(2.1f, 3.3f); }
            foreach (var p in _pool)
            {
                if (!p.Active) continue;
                p.Age += delta;
                if (p.Age >= p.Life) { p.Active = false; p.Image.gameObject.SetActive(false); continue; }
                if (!p.Leaf) p.Velocity.y -= 185 * delta;
                p.Position += p.Velocity * delta;
                p.Angle += p.Spin * delta;
                Draw(p);
            }
            CountPetals();
        }

        private void CountPetals()
        {
            ActivePetals = 0;
            foreach (var p in _pool) if (p != null && p.Active && !p.Leaf) ActivePetals++;
        }

        private void Draw(Particle p)
        {
            float flutter = Mathf.Sin(p.Age * (p.Leaf ? 1.8f : 5) + p.Phase);
            var rect = p.Image.rectTransform;
            rect.anchoredPosition = p.Position + Vector2.right * (flutter * (p.Leaf ? 12 : 3));
            rect.localRotation = Quaternion.Euler(0, 0, p.Angle + flutter * 12);
            rect.localScale = new Vector3(.4f + .6f * Mathf.Abs(Mathf.Cos(p.Age * 2 + p.Phase)), 1, 1);
            float fade = Mathf.Min(Mathf.Clamp01(p.Age / .18f), Mathf.Clamp01((p.Life - p.Age) / .4f));
            float tint = p.Leaf ? .8f : .92f;
            p.Image.color = new Color(tint, tint, tint, p.Opacity * fade);
        }
    }
}

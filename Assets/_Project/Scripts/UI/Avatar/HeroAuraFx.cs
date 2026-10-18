using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Energy marks around the hero for the top aura tiers, in the style of cartoon energy orbs:
    /// crescent swooshes orbiting the body and whipping around a forearm or shin, sparkles drifting off,
    /// a rare straight energy streak and a short crackle of lightning along the limbs and torso
    /// (never the head).
    ///
    /// Everything is drawn as tapered strokes (<see cref="AuraFxMesh"/>) on two layers that copy the
    /// portrait's rect: one just behind the hero, one just in front. A swoosh's far half goes on
    /// the back layer and its near half on the front, so it really circles the body. Anchors come
    /// from the humanoid bones of the character the portrait's camera renders, projected into the
    /// portrait, so the marks follow the idle animation and whichever hero is equipped.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(2150)]
    public sealed class HeroAuraFx : MonoBehaviour
    {
        public sealed class Style
        {
            public Color Core = Color.white, Edge, Glow;
            /// <summary>Average seconds between spawns; 0 turns that kind off.</summary>
            public float OrbitEvery = 1.4f, LimbEvery = 2.3f, StreakEvery = 2.4f, BoltEvery = 2.8f;
            public float SparklesPerSecond = 6f;
            /// <summary>Sparkle size multiplier.</summary>
            public float SparkleScale = 1f;
            /// <summary>Faceted blade-like arcs and streaks instead of smooth crescents.</summary>
            public bool Sharp;
        }

        /// <summary>10K: sparkles only — a few white stars drifting up off the body.</summary>
        public static readonly Style White = new Style
        {
            Edge = new Color(.82f, .9f, 1f),
            Glow = new Color(.7f, .85f, 1f),
            OrbitEvery = 0f, LimbEvery = 0f, StreakEvery = 0f, BoltEvery = 0f,
            SparklesPerSecond = 5f, SparkleScale = 1.4f,
        };

        /// <summary>50K: the gentle first step — an occasional smooth swoosh and a few sparkles,
        /// no streaks or lightning (those arrive at 250K).</summary>
        public static readonly Style Blue = new Style
        {
            Edge = new Color(.45f, .82f, 1f),
            Glow = new Color(.1f, .45f, 1f),
            OrbitEvery = 2f, LimbEvery = 3.2f, StreakEvery = 0f, BoltEvery = 0f,
            SparklesPerSecond = 3f,
        };

        public static readonly Style Violet = new Style
        {
            Edge = new Color(.86f, .5f, 1f),
            Glow = new Color(.6f, .16f, 1f),
        };

        /// <summary>1M: crimson-violet, lightning almost never stops, lots of particles, sharp cuts.</summary>
        public static readonly Style Dark = new Style
        {
            Core = new Color(1f, .82f, .95f),
            Edge = new Color(.95f, .15f, .45f),
            Glow = new Color(.5f, .02f, .3f),
            OrbitEvery = 1.2f, LimbEvery = 1.8f, StreakEvery = .7f, BoltEvery = .3f,
            SparklesPerSecond = 12f, Sharp = true,
        };

        private enum B { Hips, Chest, Head, LShoulder, RShoulder, LElbow, RElbow, LHand, RHand, LHip, RHip, LKnee, RKnee, LFoot, RFoot, Count }

        private static readonly HumanBodyBones[] BoneMap =
        {
            HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        };

        // Joints lightning runs between: along the arms, the legs and down the sides of the torso.
        // Never the head — the owner wants energy on the body and limbs, not in the face.
        private static readonly B[,] BoltPairs =
        {
            { B.LHand, B.LElbow }, { B.RHand, B.RElbow }, { B.LElbow, B.LShoulder }, { B.RElbow, B.RShoulder },
            { B.LFoot, B.LKnee }, { B.RFoot, B.RKnee }, { B.LKnee, B.LHip }, { B.RKnee, B.RHip },
            { B.LShoulder, B.LHip }, { B.RShoulder, B.RHip },
        };

        // Limbs a swoosh can whip around: forearms and shins (from joint, to joint).
        private static readonly B[,] Limbs = { { B.LElbow, B.LHand }, { B.RElbow, B.RHand }, { B.LKnee, B.LFoot }, { B.RKnee, B.RFoot } };

        private static readonly float[] OrbitHeights = { .12f, .35f, .58f }; // hips → head; below the shoulders
        private static readonly B[] StreakBones = { B.LHand, B.RHand, B.LElbow, B.RElbow, B.LKnee, B.RKnee, B.LFoot, B.RFoot };
        private static readonly B[] SparkleBones = { B.LHand, B.RHand, B.LElbow, B.RElbow, B.LShoulder, B.RShoulder, B.Hips, B.LKnee, B.RKnee, B.LFoot, B.RFoot };

        private sealed class Swoosh
        {
            public bool Limb;
            public int LimbIndex;
            public float Height, Radius, Ratio, Tilt, Dir, Start, Speed, Arc, Age, Life, Width;
            public int Seed;
        }

        private sealed class Sparkle { public Vector2 Pos, Vel; public float Age, Life, Size, Spin; public bool Front; }
        private sealed class Streak { public Vector2 From, Dir; public float Length, Age, Life, Width; }
        private sealed class Bolt { public int Pair; public float Age, Life; public int Seed; }

        private RawImage _portrait;
        private AuraFxMesh _back, _front;
        private Material _material;
        private Style _style;

        private Camera _camera;
        private Animator _animator;
        private readonly Transform[] _bones = new Transform[(int)B.Count];
        private readonly Vector2[] _p = new Vector2[(int)B.Count];
        private float _scale, _searchAt;
        private Vector2 _centre;

        private readonly List<Swoosh> _swooshes = new List<Swoosh>();
        private readonly List<Sparkle> _sparkles = new List<Sparkle>();
        private readonly List<Streak> _streaks = new List<Streak>();
        private readonly List<Bolt> _bolts = new List<Bolt>();
        private readonly List<Vector2> _points = new List<Vector2>(48);
        private readonly List<bool> _near = new List<bool>(48);
        private float _nextOrbit, _nextLimb, _nextStreak, _nextBolt, _sparkleDebt;

        public bool Visible => _style != null;

        public static HeroAuraFx Create(RawImage portrait)
        {
            if (portrait == null || portrait.transform.parent == null) return null;
            var shader = Resources.Load<Shader>("AuraFxGlow");
            if (shader == null) { Debug.LogWarning("[HeroAuraFx] Resources/AuraFxGlow.shader is missing."); return null; }
            var material = new Material(shader) { name = "AuraFxGlow (runtime)" };
            var back = MakeLayer("HeroAuraFxBack", portrait, material);
            var front = MakeLayer("HeroAuraFxFront", portrait, material);
            var fx = back.gameObject.AddComponent<HeroAuraFx>();
            fx._portrait = portrait;
            fx._back = back;
            fx._front = front;
            fx._material = material;
            fx.Layout();
            return fx;
        }

        private static AuraFxMesh MakeLayer(string name, RawImage portrait, Material material)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(AuraFxMesh));
            go.layer = portrait.gameObject.layer;
            go.transform.SetParent(portrait.transform.parent, false);
            var mesh = go.GetComponent<AuraFxMesh>();
            mesh.raycastTarget = false;
            mesh.material = material;
            return mesh;
        }

        public void Show(Style style)
        {
            if (_style == style) return;
            _style = style;
            _nextOrbit = .2f; _nextLimb = .9f; _nextStreak = 1.2f; _nextBolt = 1.6f;
        }

        public void Hide()
        {
            if (_style == null) return;
            _style = null;
            _swooshes.Clear(); _sparkles.Clear(); _streaks.Clear(); _bolts.Clear();
            SetLayers(false);
        }

        private void SetLayers(bool on)
        {
            if (_back != null && _back.enabled != on) _back.enabled = on;
            if (_front != null && _front.enabled != on) _front.enabled = on;
        }

        private void LateUpdate()
        {
            if (_portrait == null) { Destroy(gameObject); return; }
            bool on = _style != null && _portrait.isActiveAndEnabled && _portrait.color.a > .01f && FindRig();
            SetLayers(on);
            if (!on) return;

            Layout();
            ProjectBones();
            float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
            Spawn(dt);

            _back.Clear();
            _front.Clear();
            DrawSwooshes(dt);
            DrawSparkles(dt);
            DrawStreaks(dt);
            DrawBolts(dt);
            _back.Commit();
            _front.Commit();
        }

        // ── Layout and rig ────────────────────────────────────────────────────────────────────

        private void Layout()
        {
            var from = _portrait.rectTransform;
            Copy(from, _back.rectTransform);
            Copy(from, _front.rectTransform);

            int p = from.GetSiblingIndex(), b = _back.transform.GetSiblingIndex();
            int behind = b < p ? p - 1 : p;
            if (b != behind) _back.transform.SetSiblingIndex(behind);
            p = from.GetSiblingIndex();
            int f = _front.transform.GetSiblingIndex();
            int ahead = f < p ? p : p + 1;
            if (f != ahead) _front.transform.SetSiblingIndex(ahead);

            var tint = new Color(1f, 1f, 1f, _portrait.color.a);
            if (_back.color != tint) { _back.color = tint; _front.color = tint; }
        }

        private static void Copy(RectTransform from, RectTransform to)
        {
            to.anchorMin = from.anchorMin;
            to.anchorMax = from.anchorMax;
            to.pivot = from.pivot;
            to.offsetMin = from.offsetMin;
            to.offsetMax = from.offsetMax;
            to.localRotation = from.localRotation;
            to.localScale = from.localScale;
        }

        /// <summary>The camera drawing the portrait's texture and the humanoid it frames.</summary>
        private bool FindRig()
        {
            var texture = _portrait.texture;
            if (_camera != null && _animator != null && _camera.targetTexture == texture && _animator.isActiveAndEnabled) return true;
            if (Time.unscaledTime < _searchAt) return false;
            _searchAt = Time.unscaledTime + .5f;

            _camera = null;
            foreach (var cam in Camera.allCameras)
                if (cam.targetTexture != null && cam.targetTexture == texture) { _camera = cam; break; }
            if (_camera == null) return false;

            _animator = null;
            float nearest = float.MaxValue;
            foreach (var a in FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                if (!a.isActiveAndEnabled || !a.isHuman || (_camera.cullingMask & (1 << a.gameObject.layer)) == 0) continue;
                var hips = a.GetBoneTransform(HumanBodyBones.Hips);
                if (hips == null) continue;
                var vp = _camera.WorldToViewportPoint(hips.position);
                if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f || vp.z >= nearest) continue;
                nearest = vp.z;
                _animator = a;
            }
            if (_animator == null) return false;

            for (int i = 0; i < BoneMap.Length; i++) _bones[i] = _animator.GetBoneTransform(BoneMap[i]);
            if (_bones[(int)B.Chest] == null) _bones[(int)B.Chest] = _animator.GetBoneTransform(HumanBodyBones.Spine);
            foreach (var bone in _bones) if (bone == null) { _animator = null; return false; }
            return true;
        }

        private void ProjectBones()
        {
            var rect = _portrait.rectTransform.rect;
            var uv = _portrait.uvRect;
            bool wide = _portrait.TryGetComponent<AvatarWideImage>(out var widen) && widen.enabled;
            float width = rect.width * (wide ? AvatarWideCamera.WidthMultiplier : 1f);
            for (int i = 0; i < _p.Length; i++)
            {
                var vp = _camera.WorldToViewportPoint(_bones[i].position);
                float u = (vp.x - uv.x) / uv.width, v = (vp.y - uv.y) / uv.height;
                _p[i] = new Vector2(rect.center.x + (u - .5f) * width, rect.yMin + v * rect.height);
            }
            _centre = _p[(int)B.Chest];
            _scale = Mathf.Max(Vector2.Distance(_p[(int)B.LShoulder], _p[(int)B.RShoulder]),
                Vector2.Distance(_p[(int)B.Hips], _p[(int)B.Head]) * .3f, 1f);
        }

        // ── Spawning ──────────────────────────────────────────────────────────────────────────

        private static bool Due(ref float timer, float every, float dt)
        {
            if (every <= 0f) return false;
            timer -= dt;
            if (timer > 0f) return false;
            timer = every * Random.Range(.7f, 1.3f);
            return true;
        }

        private void Spawn(float dt)
        {
            if (Due(ref _nextOrbit, _style.OrbitEvery, dt))
                _swooshes.Add(new Swoosh
                {
                    Height = OrbitHeights[Random.Range(0, OrbitHeights.Length)], Radius = Random.Range(.85f, 1.05f),
                    Ratio = .26f, Tilt = Random.Range(-16f, 16f) * Mathf.Deg2Rad, Dir = Random.value < .5f ? 1f : -1f,
                    Start = Random.Range(0f, Mathf.PI * 2f), Speed = 3.4f, Arc = 1.9f, Life = .95f, Width = .1f, Seed = Random.Range(0, 100000),
                });
            if (Due(ref _nextLimb, _style.LimbEvery, dt))
                _swooshes.Add(new Swoosh
                {
                    Limb = true, LimbIndex = Random.Range(0, Limbs.GetLength(0)), Radius = .55f, Ratio = .38f,
                    Dir = Random.value < .5f ? 1f : -1f, Start = Random.Range(0f, Mathf.PI * 2f),
                    Speed = 6f, Arc = 2.3f, Life = .6f, Width = .15f, Seed = Random.Range(0, 100000),
                });
            if (Due(ref _nextStreak, _style.StreakEvery, dt))
            {
                var from = _p[(int)StreakBones[Random.Range(0, StreakBones.Length)]];
                // Out to the side the limb is on, rising a little.
                Vector2 dir = new Vector2(Mathf.Sign(from.x - _centre.x), .5f).normalized;
                dir = Rotate(dir, Random.Range(-20f, 20f) * Mathf.Deg2Rad);
                _streaks.Add(new Streak { From = from + dir * _scale * .25f, Dir = dir, Length = _scale * Random.Range(.6f, .9f), Life = .32f, Width = _scale * .035f });
            }
            if (Due(ref _nextBolt, _style.BoltEvery, dt))
                _bolts.Add(new Bolt { Pair = Random.Range(0, BoltPairs.GetLength(0)), Life = .22f, Seed = Random.Range(0, 100000) });

            _sparkleDebt += dt * _style.SparklesPerSecond;
            while (_sparkleDebt >= 1f)
            {
                _sparkleDebt -= 1f;
                int bone = (int)SparkleBones[Random.Range(0, SparkleBones.Length)];
                _sparkles.Add(new Sparkle
                {
                    Pos = _p[bone] + Random.insideUnitCircle * _scale * .35f,
                    Vel = new Vector2(Random.Range(-.35f, .35f), Random.Range(.45f, 1f)) * _scale,
                    Life = Random.Range(.5f, .9f), Size = _scale * Random.Range(.05f, .09f) * _style.SparkleScale,
                    Spin = Random.Range(-3f, 3f), Front = Random.value < .65f,
                });
            }
        }

        // ── Drawing ───────────────────────────────────────────────────────────────────────────

        private void DrawSwooshes(float dt)
        {
            for (int s = _swooshes.Count - 1; s >= 0; s--)
            {
                var w = _swooshes[s];
                w.Age += dt;
                float a = w.Age / w.Life;
                if (a >= 1f) { _swooshes.RemoveAt(s); continue; }

                Vector2 centre;
                float rx, tilt;
                if (w.Limb)
                {
                    Vector2 joint = _p[(int)Limbs[w.LimbIndex, 0]], end = _p[(int)Limbs[w.LimbIndex, 1]];
                    Vector2 arm = end - joint;
                    centre = Vector2.Lerp(joint, end, .55f);
                    rx = Mathf.Max(arm.magnitude * w.Radius, _scale * .25f);
                    // A ring around the forearm or shin: its long axis runs across the limb.
                    tilt = Mathf.Atan2(arm.y, arm.x) + Mathf.PI * .5f;
                }
                else
                {
                    centre = Vector2.Lerp(_p[(int)B.Hips], _p[(int)B.Head], w.Height);
                    rx = _scale * w.Radius;
                    tilt = w.Tilt;
                }

                // The head sweeps on; the arc grows out of it, then the tail catches up.
                float head = w.Start + w.Dir * w.Speed * w.Age;
                float arc = w.Arc * Smooth(0f, .3f, a) * (1f - Smooth(.55f, 1f, a));
                if (arc < .05f) continue;
                // Sharp style: few vertices, each nudged in or out, so the arc is a faceted cut.
                int n = _style.Sharp ? 7 : 26;
                _points.Clear();
                _near.Clear();
                for (int i = 0; i < n; i++)
                {
                    float phi = head - w.Dir * arc * (1f - (float)i / (n - 1));
                    float r = _style.Sharp ? rx * (1f + (Hash(w.Seed + i) - .5f) * .16f) : rx;
                    var local = new Vector2(Mathf.Cos(phi) * r, Mathf.Sin(phi) * r * w.Ratio);
                    _points.Add(centre + Rotate(local, tilt));
                    // Seen from slightly above, the lower half of the ring is the near half.
                    _near.Add(Mathf.Sin(phi) < 0f);
                }
                float width = rx * w.Width, alpha = 1f - Smooth(.85f, 1f, a);
                DrawSplit(width, _style.Sharp ? AuraFxMesh.Profile.Blade : AuraFxMesh.Profile.Crescent, alpha);
            }
        }

        /// <summary>Draws _points as one stroke, near runs on the front layer and far runs on the
        /// back one; neighbouring runs share their boundary point so the stroke stays joined.</summary>
        private void DrawSplit(float width, AuraFxMesh.Profile profile, float alpha)
        {
            int n = _points.Count, start = 0;
            for (int i = 1; i <= n; i++)
            {
                if (i < n && _near[i] == _near[start]) continue;
                int end = Mathf.Min(i, n - 1);
                int count = end - start + 1;
                var layer = _near[start] ? _front : _back;
                float from = (float)start / (n - 1), to = (float)end / (n - 1);
                layer.Stroke(_points, start, count, from, to, width * 2.6f, profile, _style.Glow, _style.Glow, alpha * .35f);
                layer.Stroke(_points, start, count, from, to, width, profile, _style.Core, _style.Edge, alpha);
                start = i;
            }
        }

        private void DrawSparkles(float dt)
        {
            for (int i = _sparkles.Count - 1; i >= 0; i--)
            {
                var s = _sparkles[i];
                s.Age += dt;
                float a = s.Age / s.Life;
                if (a >= 1f) { _sparkles.RemoveAt(i); continue; }
                s.Pos += s.Vel * dt;
                s.Vel *= 1f - dt * .8f;
                float size = s.Size * Mathf.Sin(Mathf.PI * a);
                (s.Front ? _front : _back).Sparkle(s.Pos, size, s.Spin * s.Age, _style.Core, _style.Edge, 1f);
            }
        }

        private void DrawStreaks(float dt)
        {
            for (int i = _streaks.Count - 1; i >= 0; i--)
            {
                var s = _streaks[i];
                s.Age += dt;
                float a = s.Age / s.Life;
                if (a >= 1f) { _streaks.RemoveAt(i); continue; }
                float reach = s.Length * 1.7f;
                float headAt = reach * EaseOut(a), tailAt = reach * EaseOut(Mathf.Clamp01((a - .25f) / .75f));
                if (headAt - tailAt < 1f) continue;
                _points.Clear();
                for (int k = 0; k < 6; k++) _points.Add(s.From + s.Dir * Mathf.Lerp(tailAt, headAt, k / 5f));
                var profile = _style.Sharp ? AuraFxMesh.Profile.Blade : AuraFxMesh.Profile.Taper;
                _front.Stroke(_points, 0, 6, 0f, 1f, s.Width * 2.6f, profile, _style.Glow, _style.Glow, .35f);
                _front.Stroke(_points, 0, 6, 0f, 1f, s.Width, profile, _style.Core, _style.Edge, 1f);
            }
        }

        private void DrawBolts(float dt)
        {
            for (int i = _bolts.Count - 1; i >= 0; i--)
            {
                var b = _bolts[i];
                b.Age += dt;
                if (b.Age >= b.Life) { _bolts.RemoveAt(i); continue; }

                Vector2 from = _p[(int)BoltPairs[b.Pair, 0]], to = _p[(int)BoltPairs[b.Pair, 1]];
                // Lift the arc sideways off the body so it is not hidden behind the limb it follows.
                from.x += Mathf.Sign(from.x - _centre.x) * _scale * .12f;
                to.x += Mathf.Sign(to.x - _centre.x) * _scale * .12f;
                Vector2 along = to - from, across = new Vector2(-along.y, along.x).normalized;
                int frame = Mathf.FloorToInt(b.Age / .05f);
                // Every few frames the path re-jags and briefly dims: a crackle, not a slide.
                float alpha = frame % 3 == 2 ? .4f : 1f;
                const int n = 8;
                _points.Clear();
                for (int k = 0; k < n; k++)
                {
                    float t = k / (n - 1f);
                    float jag = k == 0 || k == n - 1 ? 0f : (Hash(b.Seed + frame * 31 + k) - .5f) * 2f;
                    _points.Add(from + along * t + across * jag * _scale * .08f);
                }
                float width = _scale * .022f;
                _front.Stroke(_points, 0, n, 0f, 1f, width * 3f, AuraFxMesh.Profile.Even, _style.Glow, _style.Glow, alpha * .5f);
                _front.Stroke(_points, 0, n, 0f, 1f, width, AuraFxMesh.Profile.Even, _style.Core, _style.Edge, alpha);

                // A short fork off the middle.
                Vector2 root = _points[3];
                Vector2 fork = Rotate(along.normalized, (Hash(b.Seed + frame) > .5f ? 1f : -1f) * .9f) * _scale * .22f;
                _points.Clear();
                _points.Add(root);
                _points.Add(root + fork * .5f + across * (Hash(b.Seed + frame * 7) - .5f) * _scale * .06f);
                _points.Add(root + fork);
                _front.Stroke(_points, 0, 3, 0f, 1f, width * .8f, AuraFxMesh.Profile.Taper, _style.Core, _style.Edge, alpha);
            }
        }

        private static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        private static float Hash(int n)
        {
            n = (n << 13) ^ n;
            return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483648f;
        }

        private void OnDestroy()
        {
            if (_front != null) Destroy(_front.gameObject);
            if (_material != null) Destroy(_material);
        }
    }
}

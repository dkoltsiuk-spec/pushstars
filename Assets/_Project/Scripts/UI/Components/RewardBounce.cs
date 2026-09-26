using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Looping "collect me" hop for a ready map reward: crouch, jump with a stretch, land with a
    /// squash, rest. Unscaled time; when <see cref="Active"/> is off it snaps back to the authored
    /// pose so a collected or locked reward sits still. Pivot the target at its bottom edge so
    /// the squash reads as landing on the platform.
    /// </summary>
    public sealed class RewardBounce : MonoBehaviour
    {
        public bool Active;
        [Min(.2f)] public float Period = 1.15f;
        public float Height = 16;
        private RectTransform _rect;
        private Vector2 _position;
        private Vector3 _scale;
        private bool _captured;
        private float _time;

        private void Capture()
        {
            if (_captured) return;
            _rect = (RectTransform)transform;
            _position = _rect.anchoredPosition; _scale = _rect.localScale; _captured = true;
        }

        private void OnEnable() { Capture(); _time = 0; }
        private void OnDisable() => Restore();

        public void Restore()
        {
            if (!_captured) return;
            _rect.anchoredPosition = _position; _rect.localScale = _scale; _time = 0;
        }

        private void Update()
        {
            Capture();
            if (!Active) { if (_time != 0) Restore(); return; }
            _time += Time.unscaledDeltaTime;
            Pose(Mathf.Repeat(_time, Period) / Period);
        }

        /// <summary>Applies the hop at a normalized phase in [0, 1) (validation renders use it directly).</summary>
        public void Pose(float t)
        {
            Capture();
            float lift = 0, sx = 1, sy = 1;
            if (t < .12f)
            {   // crouch
                float k = Mathf.Sin(t / .12f * Mathf.PI * .5f);
                sx = 1 + .10f * k; sy = 1 - .12f * k;
            }
            else if (t < .52f)
            {   // airborne arc, stretched on the way up
                float k = (t - .12f) / .40f;
                lift = Mathf.Sin(k * Mathf.PI);
                float stretch = Mathf.Clamp01(1 - k * 2);
                sx = 1 - .06f * stretch; sy = 1 + .10f * stretch;
            }
            else if (t < .66f)
            {   // landing squash
                float k = Mathf.Sin((t - .52f) / .14f * Mathf.PI);
                sx = 1 + .12f * k; sy = 1 - .14f * k;
            }
            _rect.anchoredPosition = _position + Vector2.up * (Height * lift);
            _rect.localScale = new Vector3(_scale.x * sx, _scale.y * sy, _scale.z);
        }
    }
}

using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Slowly floats a background icon pattern upward, forever: every child drifts up, sways a
    /// little, optionally spins, and wraps from the top of the pattern back to the bottom, so the
    /// lattice never runs out. Children are authored in the scene (centre-anchored, laid out over
    /// <see cref="Period"/> units of height), this only moves them.
    /// </summary>
    public sealed class FloatingPattern : MonoBehaviour
    {
        public float Speed = 10;   // units per second, upward
        public float Period = 1120; // height of the authored lattice; must cover the screen
        public float Sway = 3;     // sideways drift, units
        public float Wobble = 3;   // rotation sway, degrees
        public float Spin;         // continuous turn, degrees per second (alternates per icon)

        RectTransform[] _icons;
        Vector2[] _base;
        float[] _phase, _angle;

        void Awake()
        {
            int count = transform.childCount;
            _icons = new RectTransform[count]; _base = new Vector2[count];
            _phase = new float[count]; _angle = new float[count];
            for (int i = 0; i < count; i++)
            {
                _icons[i] = (RectTransform)transform.GetChild(i);
                _base[i] = _icons[i].anchoredPosition;
                _angle[i] = _icons[i].localEulerAngles.z;
                _phase[i] = i * 2.399f; // golden-angle spread so neighbours never move in step
            }
        }

        void Update()
        {
            float t = Time.unscaledTime, half = Period * .5f;
            for (int i = 0; i < _icons.Length; i++)
            {
                float y = Mathf.Repeat(_base[i].y + Speed * t + half, Period) - half;
                float x = _base[i].x + Sway * Mathf.Sin(t * .55f + _phase[i]);
                _icons[i].anchoredPosition = new Vector2(x, y);
                float spin = Spin * t * (i % 2 == 0 ? 1 : -1);
                _icons[i].localEulerAngles = new Vector3(0, 0, _angle[i] + spin + Wobble * Mathf.Sin(t * .8f + _phase[i]));
            }
        }
    }
}

using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Gentle continuous "breathing" scale — a cheap way to keep a hero element alive
    /// (e.g. the VS badge on the matchmaking screen) without an Animator. Sine-driven
    /// on unscaled time so it keeps pulsing during pauses/countdowns.
    ///
    /// Put this on a node whose scale nothing else drives (the entrance pop should target
    /// a parent wrapper, not this object) so the two animations don't fight per-frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class PulseScale : MonoBehaviour
    {
        [SerializeField] private float _amplitude = 0.028f; // ±2.8 %
        [SerializeField] private float _speed     = 1.7f;   // radians/sec

        private Vector3 _base = Vector3.one;

        private void OnEnable() => _base = transform.localScale;

        private void Update()
        {
            float s = 1f + Mathf.Sin(Time.unscaledTime * _speed) * _amplitude;
            transform.localScale = _base * s;
        }
    }
}

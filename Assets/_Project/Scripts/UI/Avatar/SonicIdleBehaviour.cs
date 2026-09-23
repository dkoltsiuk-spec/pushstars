using UnityEngine;

namespace PushStars.UI
{
    /// <summary>Shared by Sonic and Robot. The imported accent contains only the first third.</summary>
    public sealed class SonicIdleBehaviour : MonoBehaviour
    {
        private Animator _animator;
        private float _nextAccent;
        private bool _accent;
        private const float Blend = .35f;

        private void OnEnable()
        {
            _animator = GetComponentInChildren<Animator>();
            _accent = false;
            _nextAccent = Time.time + .5f;
        }

        private void Update()
        {
            if (_animator == null || !_animator.isActiveAndEnabled ||
                _animator.runtimeAnimatorController == null || _animator.IsInTransition(0)) return;
            var state = _animator.GetCurrentAnimatorStateInfo(0);
            if (_accent && state.IsName("OffensiveIntro"))
            {
                if (state.normalizedTime < 1f - Blend / Mathf.Max(state.length, Blend)) return;
                _animator.CrossFadeInFixedTime("Idle", Blend, 0);
                _accent = false;
                _nextAccent = Time.time + Random.Range(12f, 19f);
            }
            else if (!_accent && state.IsName("Idle") && Time.time >= _nextAccent)
            {
                _animator.CrossFadeInFixedTime("OffensiveIntro", Blend, 0, 0f);
                _accent = true;
            }
        }
    }
}

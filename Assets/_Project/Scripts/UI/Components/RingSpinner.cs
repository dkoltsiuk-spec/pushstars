using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Simple constant-velocity rotator used by the matchmaking screen's dashed ring
    /// (and potentially any other purely-cosmetic spinner). Decoupled from
    /// LoadingVsRing because that component owns multiple wired references for the
    /// real Phase 14 state machine.
    /// </summary>
    [DisallowMultipleComponent]
    public class RingSpinner : MonoBehaviour
    {
        [SerializeField] private float _degreesPerSecond = 60f;

        private void Update()
        {
            transform.Rotate(0f, 0f, -_degreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}

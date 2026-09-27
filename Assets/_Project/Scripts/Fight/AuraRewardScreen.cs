using System.Collections;
using PushStars.Core;
using PushStars.UI.Layout;
using UnityEngine;

namespace PushStars.Fight
{
    /// <summary>Presents Aura a fight already credited (shown after the reward summary whenever the
    /// summary carries Aura, with or without a case). Showing it grants nothing. A tap collects:
    /// the stamp plays out, then the flow continues to this result's case or home.</summary>
    [DefaultExecutionOrder(350)]
    public sealed class AuraRewardScreen : MonoBehaviour
    {
        [SerializeField] private AuraStampPresentation _stamp;
        [SerializeField] private FightScreen _caseDestination = FightScreen.CaseOpening;
        [SerializeField] private FightScreen _homeDestination = FightScreen.Home;
        private bool _busy = true;

        public AuraStampPresentation Stamp => _stamp;
        public bool IsCollectable => !_busy;

        private IEnumerator Start()
        {
            // Stream the next screen now, on the black first frames before the stamp drops: any
            // loading hitch just holds the dark a beat longer, never the animation.
            if (HasCase) FightScreenNavigation.CaseId = FightScreenNavigation.AwardedCaseId;
            FightScreenNavigation.Preload(HasCase ? _caseDestination : _homeDestination);
            yield return ScreenTransition.Settle();
            var summary = FightScreenNavigation.RewardSummary;
            long aura = summary.Aura;
            string[] moments = summary.AuraMoments;
            if (aura == 0 && FightScreenNavigation.IsPreview) { aura = FightScreenNavigation.SampleAura; moments = FightScreenNavigation.SampleAuraMoments; }
            _stamp.Configure(aura, moments);
            float time = 0, reveal = Mathf.Max(AuraStampPresentation.RevealSeconds, _stamp.MomentsRevealSeconds);
            while (time < reveal)
            {
                if (ScreenLayoutRoot.IsAnyEditing) { yield return null; continue; }
                time += Time.unscaledDeltaTime;
                _stamp.Sample(time);
                yield return null;
            }
            _stamp.Settle();
            _busy = false;
        }

        /// <summary>Full-screen tap. Ignored until the reveal has landed, and only accepted once.</summary>
        public void Collect()
        {
            if (_busy || ScreenLayoutRoot.IsAnyEditing) return;
            _busy = true;
            // A transition, not a confirmation: the whoosh follows the skull into the camera.
            GameAudio.Play(SoundCue.AuraWhoosh);
            StartCoroutine(Leave());
        }

        private IEnumerator Leave()
        {
            // The cover closes once the skull is gone, over the tail of the stamp's ease-out and the
            // whoosh's sub drop. Only this result's award, never an unrelated pending case.
            if (HasCase) FightScreenNavigation.CaseId = FightScreenNavigation.AwardedCaseId;
            FightScreenNavigation.Navigate(HasCase ? _caseDestination : _homeDestination,
                AuraStampPresentation.ClaimSeconds - LeaveAt, ScreenTransition.Reveal.Iris, coverDelay: LeaveAt);
            // Keep animating until the scene is gone.
            for (float elapsed = 0; ; elapsed += Time.unscaledDeltaTime)
            {
                _stamp.SampleClaim(elapsed);
                yield return null;
            }
        }

        private const float LeaveAt = .55f;

        private static bool HasCase => FightScreenNavigation.IsPreview ? FightScreenNavigation.RewardSummary.HasCase
            : !string.IsNullOrEmpty(FightScreenNavigation.AwardedCaseId);
    }
}

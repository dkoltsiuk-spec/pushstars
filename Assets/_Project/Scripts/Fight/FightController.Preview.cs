using UnityEngine;
using PushStars.Core;
using PushStars.CV;
using PushStars.UI.Layout;

namespace PushStars.Fight
{
    public sealed partial class FightController
    {
        public const string ScreenPreviewKey = "PushStars.FightScreenPreview";
        private bool _screenPreview;
        private bool _layoutPaused;
        private bool _sessionBeforeLayout;
        private float _layoutPausedAt;

        private void Awake()
        {
#if UNITY_EDITOR
            if (!FightRequest.HasRequest)
                DisablePreviewTracking();
#endif
        }

        private void DisablePreviewTracking()
        {
            if (_session == null) return;
            _session.enabled = false;
            foreach (var behaviour in _session.GetComponents<MonoBehaviour>())
                if (behaviour is IPoseSource source)
                {
                    source.StopTracking();
                    behaviour.enabled = false;
                }
        }

        private bool UpdateLayoutPause()
        {
            bool editing = ScreenLayoutRoot.IsAnyEditing;
            if (editing && !_layoutPaused)
            {
                _layoutPaused = true;
                _layoutPausedAt = Time.time;
                _sessionBeforeLayout = _session != null && _session.enabled;
                if (_session != null) _session.enabled = false;
            }
            else if (!editing && _layoutPaused)
            {
                _layoutPaused = false;
                float held = Time.time - _layoutPausedAt;
                _liveStartTime += held;
                _countdownEndTime += held;
                _sceneStartTime += held;
                if (_paused) _pausedAt += held;
                if (_session != null) _session.enabled = _sessionBeforeLayout;
            }
            return editing;
        }

        private bool TryStartScreenPreview()
        {
#if UNITY_EDITOR
            if (FightRequest.HasRequest) return false;
            _screenPreview = true;
            _phase = Phase.Finished;
            DisablePreviewTracking();
            SetDebugPanels(false);
            var training = _hud.GetComponent<TrainingScreen>();
            if (training != null)
            {
                _hud.ConfigureSolo("PUSHUP"); _hud.SetScoresVisible(true);
                training.BindPreview(); return true;
            }
            if (_debugButton != null) _debugButton.gameObject.SetActive(false);
            _hud.ConfigureDuel("OSKAT009", "BEASTCORE_DEV");
            _hud.HideBanner();
            _hud.HideCountdown();
            _hud.SetScoresVisible(true);
            _hud.SetPlayerReps(21); _hud.SetOpponentReps(18);
            _hud.SetPlayerForm(90); _hud.SetOpponentForm(85);
            _hud.SetPlayerTempo(33.3f); _hud.SetOpponentTempo(1.6f);
            _hud.SetTimer(28);
            if (_exitButton != null)
            {
                _exitButton.onClick.RemoveAllListeners();
                _exitButton.onClick.AddListener(() => FightScreenNavigation.Preview(FightScreen.Results));
            }
            StartCoroutine(SamplePreviewPose());
            return true;
#else
            return false;
#endif
        }

        private void StopScreenPreview() { }

        private System.Collections.IEnumerator SamplePreviewPose()
        {
            yield return null;
            foreach (var avatar in FindObjectsByType<FightAvatar>(FindObjectsSortMode.None))
            {
                foreach (var behaviour in avatar.GetComponents<MonoBehaviour>())
                    if (behaviour is IAvatarAnimator) behaviour.enabled = false;
                if (avatar.Character == null) continue;
                var animator = avatar.Character.GetComponentInChildren<Animator>();
                if (animator == null) continue;
                animator.enabled = true;
                animator.Play("PushUp", 0, .5f);
                animator.Update(0);
                animator.speed = 0;
            }
        }
    }
}

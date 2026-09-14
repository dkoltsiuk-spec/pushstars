using System.Linq;
using PushStars.Core;
using PushStars.CV;
using UnityEngine;

namespace PushStars.Fight
{
    /// <summary>Orc presentation only. Reps, difficulty, rewards and the CV player stay unchanged.</summary>
    public sealed class BossAvatarPresentation : MonoBehaviour
    {
        private Animator _animator;
        private BossOpponent _boss;
        private PushupSession _player;
        private FightController _fight;
        private string _rest = "StandIdle";
        private float _returnAt;
        private bool _live, _terminal, _started;
        private int _attacks;
        private int _hits;
        private string _nextResultState;

        private void Start()
        {
            _animator = GetComponent<Animator>();
            _started = true;
            string scene = gameObject.scene.name;
            if (scene == "FightResults" && !FightScreenNavigation.IsPreview)
            {
                _terminal = true;
                var result = FightScreenNavigation.Result;
                if (result.Draw) Play("StandIdle", false);
                else if (result.Win) Play("Defeat", false);
                else
                {
                    _nextResultState = "Laugh";
                    Play("Victory", true);
                }
                return;
            }
            if (scene == "FightPreparation") { Play("WarmUp", true); return; }
            _fight = InScene<FightController>();
            _boss = InScene<BossOpponent>();
            _player = InScene<PushupSession>();
            if (_boss != null) _boss.OnRep += Attack;
            if (_player != null) _player.OnRep += Hit;
            Play(_rest, false);
        }

        private T InScene<T>() where T : Component => FindObjectsByType<T>(FindObjectsSortMode.None)
            .FirstOrDefault(c => c.gameObject.scene == gameObject.scene);

        private void Update()
        {
            if (!_started || _animator == null) return;
            if (_terminal) { UpdateResult(Time.time); return; }
            bool live = _fight != null && _fight.IsBossBattleLive;
            if (live != _live)
            {
                _live = live; _rest = live ? "BattleIdle" : "StandIdle";
                Play(_rest, false);
            }
            if (_returnAt > 0 && Time.time >= _returnAt) Play(_rest, false);
        }

        private void UpdateResult(float now)
        {
            if (_returnAt <= 0 || now < _returnAt || string.IsNullOrEmpty(_nextResultState)) return;
            string next = _nextResultState;
            Play(next, next == "Laugh");
            _nextResultState = next == "Laugh" ? "StandIdle" : null;
        }

        private void Attack(int reps)
        {
            if (_fight != null && _fight.IsBossBattleLive && !_terminal)
                Play(++_attacks % 2 == 0 ? "Kick" : "Punch", true);
        }
        private void Hit(int reps)
        {
            if (_fight != null && _fight.IsBossBattleLive && !_terminal)
                Play(++_hits % 2 == 0 ? "Reaction" : "Hit", true);
        }
        public float BeginDefeat()
        {
            if (_animator == null) _animator = GetComponent<Animator>();
            _terminal = true; _returnAt = 0; _nextResultState = null;
            Play("Defeat", false);
            var clip = _animator != null && _animator.runtimeAnimatorController != null
                ? _animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name == "Defeat") : null;
            return clip != null ? clip.length : .6f;
        }
        private void Play(string state, bool returnToIdle)
        {
            if (_animator == null || !_animator.HasState(0, Animator.StringToHash(state))) return;
            _animator.speed = 1;
            _animator.CrossFadeInFixedTime(state, .12f, 0, 0);
            var clip = _animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name == state);
            _returnAt = returnToIdle ? Time.time + (clip != null ? clip.length : 1) : 0;
        }
        private void OnDestroy()
        {
            if (_boss != null) _boss.OnRep -= Attack;
            if (_player != null) _player.OnRep -= Hit;
        }
    }
}

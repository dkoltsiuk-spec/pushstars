using UnityEngine;

namespace PushStars.Fight
{
    public sealed partial class FightHud
    {
        private const float RepPop = .35f, ClapRepPop = .85f;

        /// <summary>The pieces of the duel layout a clap push-up's landing moves: the two
        /// fighters' bands and the clock on the seam between them.</summary>
        internal void ClapImpactTargets(out RectTransform player, out RectTransform opponent, out RectTransform clock)
        {
            player = _playerHalf;
            opponent = _showOpponent && _opponentHalf != null ? _opponentHalf.transform as RectTransform : null;
            clock = _showOpponent && _timerPlate != null ? _timerPlate.transform as RectTransform : null;
        }

        /// <summary>The count jumps harder for a clap than for a plain rep.</summary>
        public void PunchPlayerReps()
        {
            _playerRepsPopTime = Time.time;
            _playerRepsPop = ClapRepPop;
        }
    }
}

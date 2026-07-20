using System;

namespace PushStars.Fight
{
    /// <summary>
    /// A stream of opponent reps during a duel — THE seam phase 12.5 plugs into: today the only
    /// implementation is <see cref="BossOpponent"/> (scripted timeline); later a ghost-recording
    /// player and a pacer bot implement the same contract and the fight screen doesn't change.
    ///
    /// The feed is tick-driven (the controller owns the clock) so it pauses/finishes exactly with
    /// the duel timer and never drifts from it.
    /// </summary>
    public interface IOpponentFeed
    {
        string DisplayName { get; }
        int Reps { get; }

        /// <summary>Raised with the new total each time the opponent completes a rep.</summary>
        event Action<int> OnRep;

        /// <summary>Reset to zero reps; the next <see cref="Tick"/> starts a fresh duel.</summary>
        void Begin();

        /// <summary>Advance to <paramref name="elapsedSec"/> seconds since the duel started.</summary>
        void Tick(float elapsedSec);
    }
}

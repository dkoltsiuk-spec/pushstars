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

        /// <summary>Total reps this opponent will do across the duel. Shown on the ready card as
        /// the target — for a ghost it is the player's own record, which they already know, and
        /// knowing what it takes to win is the point of the screen.</summary>
        int ExpectedReps { get; }

        /// <summary>Mean FORM (0..100) of the set being replayed, 0 when the opponent has none to
        /// report. A scripted boss does not — it has no technique, only a timeline.</summary>
        float FormPercent { get; }

        /// <summary>Mean seconds per rep, 0 when unknown.</summary>
        float SecondsPerRep { get; }

        /// <summary>Raised with the new total each time the opponent completes a rep.</summary>
        event Action<int> OnRep;

        /// <summary>Reset to zero reps; the next <see cref="Tick"/> starts a fresh duel.</summary>
        void Begin();

        /// <summary>Advance to <paramref name="elapsedSec"/> seconds since the duel started.</summary>
        void Tick(float elapsedSec);
    }
}

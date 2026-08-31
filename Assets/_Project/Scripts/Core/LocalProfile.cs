using UnityEngine;

namespace PushStars.Core
{
    /// <summary>
    /// The offline mirror of the numbers a duel changes: trophies, best set, lifetime reps.
    /// Same role and same lifetime as <see cref="OfflineXpBank"/> — the client keeps playing and
    /// scoring without a backend, and phase 11.5's sync drains this into <c>users/{uid}</c>, after
    /// which the server is authoritative and this becomes a cache.
    ///
    /// <para>Trophies are seeded once, from the onboarding level test: a newcomer who can do 40
    /// push-ups should not start on the same rung as one who can do 5.</para>
    /// </summary>
    public static class LocalProfile
    {
        private const string KeyTrophies  = "profile.trophies";
        private const string KeyBestReps  = "profile.best_reps";
        private const string KeyTotalReps = "profile.total_reps";
        private const string KeySeeded    = "profile.seeded";

        public static int Trophies
        {
            get => PlayerPrefs.GetInt(KeyTrophies, 0);
            private set { PlayerPrefs.SetInt(KeyTrophies, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        /// <summary>Best reps in a single 60-second set.</summary>
        public static int BestReps
        {
            get => PlayerPrefs.GetInt(KeyBestReps, 0);
            private set { PlayerPrefs.SetInt(KeyBestReps, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        public static int TotalReps
        {
            get => PlayerPrefs.GetInt(KeyTotalReps, 0);
            private set { PlayerPrefs.SetInt(KeyTotalReps, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        public static League League => Leagues.ForTrophies(Trophies);

        /// <summary>Puts the player on the ladder rung their level test earned. Idempotent: a second
        /// level test improves the best-set record but never re-seeds trophies the player has since
        /// won or lost.</summary>
        public static void SeedFromLevelTest(int reps)
        {
            RecordSet(reps);
            if (PlayerPrefs.GetInt(KeySeeded, 0) != 0) return;

            Trophies = FitnessTest.StartingTrophiesFor(FitnessTest.TierFor(reps));
            PlayerPrefs.SetInt(KeySeeded, 1);
            PlayerPrefs.Save();
        }

        /// <summary>Logs a finished set: lifetime reps always, best set when it beats the record.</summary>
        public static void RecordSet(int reps)
        {
            if (reps <= 0) return;
            TotalReps += reps;
            if (reps > BestReps) BestReps = reps;
        }

        /// <summary>Applies a duel outcome. Returns the signed trophy delta actually applied —
        /// a loss is clamped at zero trophies, so the number shown is the number that moved.</summary>
        public static int ApplyDuelResult(bool win, bool draw, bool ghost)
        {
            if (draw) return 0;

            int delta = win
                ? (ghost ? EconomyConfig.TrophyGhostWin : EconomyConfig.TrophyWin)
                : -(ghost ? EconomyConfig.TrophyGhostLoss : EconomyConfig.TrophyLoss);

            int before = Trophies;
            Trophies = before + delta;
            return Trophies - before;
        }

        /// <summary>Debug reset, alongside <see cref="OnboardingState.Reset"/>.</summary>
        public static void Reset()
        {
            PlayerPrefs.DeleteKey(KeyTrophies);
            PlayerPrefs.DeleteKey(KeyBestReps);
            PlayerPrefs.DeleteKey(KeyTotalReps);
            PlayerPrefs.DeleteKey(KeySeeded);
            PlayerPrefs.Save();
        }
    }
}

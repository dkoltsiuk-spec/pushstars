namespace PushStars.Core
{
    /// <summary>Strength bracket a player lands in after the 60-second level test.</summary>
    public enum FitnessTier
    {
        Beginner = 0,
        Amateur  = 1,
        Athlete  = 2,
        Champion = 3,
        Elite    = 4,
    }

    /// <summary>
    /// The onboarding level test: one 60-second push-up set whose rep count places the player in a
    /// <see cref="FitnessTier"/>. Pure math, no Unity — the same numbers have to be reproducible on
    /// the server when the test result starts seeding matchmaking.
    ///
    /// <para><b>Why a tier at all.</b> The rep count alone is the honest measure, but it says
    /// nothing to a first-time user: "21" is meaningless, "АТЛЕТ" is a place on a ladder. The tier
    /// is also what seeds the player's starting trophies, so a strong newcomer doesn't spend their
    /// first week beating opponents far below them.</para>
    /// </summary>
    public static class FitnessTest
    {
        /// <summary>Lower rep bound of each tier, ascending. Index == (int)tier.</summary>
        private static readonly int[] Thresholds = { 0, 10, 20, 35, 50 };

        /// <summary>Trophies a player starts with, by tier — the ladder position their test earned.
        /// Bronze spans 0–999 (see <see cref="Leagues"/>), so every tier still starts in Bronze and
        /// climbs out by playing; the test only decides how far into it they begin.</summary>
        private static readonly int[] StartingTrophies = { 0, 60, 150, 280, 420 };

        public static FitnessTier TierFor(int reps)
        {
            var tier = FitnessTier.Beginner;
            for (int i = Thresholds.Length - 1; i >= 0; i--)
            {
                if (reps >= Thresholds[i]) { tier = (FitnessTier)i; break; }
            }
            return tier;
        }

        public static int StartingTrophiesFor(FitnessTier tier)
            => StartingTrophies[Index(tier)];

        /// <summary>Reps needed to reach the next tier, or 0 at the top one.</summary>
        public static int RepsToNextTier(int reps)
        {
            int next = Index(TierFor(reps)) + 1;
            return next >= Thresholds.Length ? 0 : Thresholds[next] - reps;
        }

        public static string DisplayName(FitnessTier tier) => tier switch
        {
            FitnessTier.Amateur  => "ЛЮБИТЕЛЬ",
            FitnessTier.Athlete  => "АТЛЕТ",
            FitnessTier.Champion => "ЧЕМПИОН",
            FitnessTier.Elite    => "ЭЛИТА",
            _                    => "НОВИЧОК",
        };

        /// <summary>One line of context under the tier name on the result screen.</summary>
        public static string Blurb(FitnessTier tier) => tier switch
        {
            FitnessTier.Amateur  => "Хорошая база. Есть куда расти.",
            FitnessTier.Athlete  => "Крепкий результат — выше среднего.",
            FitnessTier.Champion => "Сильный уровень. Мало кто так может.",
            FitnessTier.Elite    => "Верхний эшелон. Ищи соперников под стать.",
            _                    => "Каждый с чего-то начинает. Дальше — только вверх.",
        };

        private static int Index(FitnessTier tier)
        {
            int i = (int)tier;
            return i < 0 ? 0 : (i >= Thresholds.Length ? Thresholds.Length - 1 : i);
        }
    }
}

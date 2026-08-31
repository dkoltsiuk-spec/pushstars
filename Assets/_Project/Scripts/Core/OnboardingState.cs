using UnityEngine;

namespace PushStars.Core
{
    /// <summary>
    /// What the player has already been through on this device: the intro pages, and the 60-second
    /// level test that produces their first <see cref="GhostRecord"/>. <see cref="AppBootstrap"/>
    /// reads these to decide where the app opens.
    ///
    /// <para>PlayerPrefs, not Firestore, on purpose: the very first launch has to route correctly
    /// before (and whether or not) the backend answers, and an offline install must not be sent
    /// through the intro twice. Phase 11.5 mirrors these flags to <c>users/{uid}/flags</c> so a
    /// reinstall restores them; the local copy stays the one the router reads.</para>
    /// </summary>
    public static class OnboardingState
    {
        private const string KeyIntroSeen     = "onboarding.intro_seen";
        private const string KeyLevelTestDone = "onboarding.level_test_done";
        private const string KeyLevelTestReps = "onboarding.level_test_reps";

        /// <summary>The intro pages have been read through to the end.</summary>
        public static bool IntroSeen
        {
            get => PlayerPrefs.GetInt(KeyIntroSeen, 0) != 0;
            set { PlayerPrefs.SetInt(KeyIntroSeen, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>The level test has been completed at least once.</summary>
        public static bool LevelTestDone => PlayerPrefs.GetInt(KeyLevelTestDone, 0) != 0;

        /// <summary>Reps scored in the level test (0 before it is taken).</summary>
        public static int LevelTestReps => PlayerPrefs.GetInt(KeyLevelTestReps, 0);

        public static FitnessTier Tier => FitnessTest.TierFor(LevelTestReps);

        /// <summary>True once the player has been through everything the first launch asks for.</summary>
        public static bool Complete => IntroSeen && LevelTestDone;

        /// <summary>Records the level test result. Called once, when the test's result screen shows.</summary>
        public static void CompleteLevelTest(int reps)
        {
            PlayerPrefs.SetInt(KeyLevelTestDone, 1);
            PlayerPrefs.SetInt(KeyLevelTestReps, Mathf.Max(0, reps));
            PlayerPrefs.Save();
        }

        /// <summary>Sends the next launch back through the intro and the level test. Wired to the
        /// debug entry in Settings so the whole first-run flow can be re-tested on device without
        /// deleting the app.</summary>
        public static void Reset()
        {
            PlayerPrefs.DeleteKey(KeyIntroSeen);
            PlayerPrefs.DeleteKey(KeyLevelTestDone);
            PlayerPrefs.DeleteKey(KeyLevelTestReps);
            PlayerPrefs.Save();
            GhostStore.Clear();
        }
    }
}

using System;
using System.IO;
using System.Linq;
using PushStars.Core;
using PushStars.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>Exercises the actual summary → case → aura scene actions with a wallet-free preview.</summary>
    [InitializeOnLoad]
    public static class AssessmentRewardPlayValidation
    {
        private const string Key = "PushStars.AssessmentRewardValidation";
        [Serializable] private sealed class Scenes { public UnityEditor.SceneManagement.SceneSetup[] Items; }
        private static int _step;
        private static int _frameDue;
        private static double _due, _deadline;
        private static string _wallet;
        private static float _fireClock;
        static AssessmentRewardPlayValidation() => EditorApplication.playModeStateChanged += State;
        [MenuItem("Tools/Push Stars/Rewards/Validate Assessment Flow In Play Mode")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes before Play validation.");
            SessionState.SetString(Key + ".scenes", JsonUtility.ToJson(new Scenes { Items = EditorSceneManager.GetSceneManagerSetup() }));
            SessionState.SetBool(Key, true);
            EditorSceneManager.OpenScene(FightScreenNavigation.ScenePath(FightScreen.RewardSummary));
            EditorApplication.isPlaying = true;
        }
        private static void State(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                Application.runInBackground = true;
                _wallet = PlayerPrefs.GetString("rewards.case_ledger.v1", "");
                _step = 0; _deadline = EditorApplication.timeSinceStartup + 40;
                Delay(.3);
                EditorApplication.update += Tick;
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update -= Tick;
                SessionState.SetBool(Key, false);
                var saved = JsonUtility.FromJson<Scenes>(SessionState.GetString(Key + ".scenes", ""));
                if (saved?.Items != null) EditorSceneManager.RestoreSceneManagerSetup(saved.Items);
            }
        }
        private static void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < _due) return;
            if (Time.frameCount < _frameDue && EditorApplication.timeSinceStartup < _deadline)
            { EditorApplication.QueuePlayerLoopUpdate(); return; }
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new InvalidOperationException("Assessment flow timed out");
                var screen = Object.FindFirstObjectByType<RewardScreen>();
                var aura = Object.FindFirstObjectByType<AuraRewardScreen>();
                switch (_step++)
                {
                    case 0:
                        FightScreenNavigation.PrepareAssessmentPreview();
                        SceneManager.LoadScene("RewardSummary"); Delay(2); break;
                    case 1:
                        Require(screen != null && screen.Screen == FightScreen.RewardSummary, "Summary scene missing");
                        Require(screen.SummaryUi.Title.text == "ASSESSMENT COMPLETE!" && screen.SummaryUi.TotalReps.text == "8", "Summary shows wrong result");
                        Require(screen.SummaryUi.EnergyXp.text == "+80" && !screen.SummaryUi.AuraGroup.activeInHierarchy && !screen.SummaryUi.TrophyGroup.activeSelf, "Summary still counts Aura or wrong rewards");
                        Require(!Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(t => t.name == "AssessmentBonus"), "Summary still announces Aura in the case");
                        Require(screen.SummaryUi.ContinueLabel.text == "CONTINUE", "Summary does not lead to the Aura screen");
                        screen.Home(); Delay(.9); break;
                    case 2:
                        Require(aura != null, "Aura was credited but its screen did not follow the summary");
                        Require(!aura.IsCollectable, "Aura reveal can be skipped");
                        aura.Collect(); Delay(2.2); break;
                    case 3:
                        Require(aura != null && aura.IsCollectable, "Early tap left the Aura screen or reveal never settled");
                        Require(aura.Stamp.Number.text == "+200" && aura.Stamp.SkullGroup.alpha > .99f, "Stamp or skull missing");
                        _fireClock = aura.Stamp.Skull.localScale.x;
                        Delay(.3); break;
                    case 4:
                        Require(!Mathf.Approximately(aura.Stamp.Skull.localScale.x, _fireClock), "Skull idle stopped after reveal");
                        aura.Collect(); aura.Collect();
                        // Exit plays, cover closes, next scene settles, iris opens, then the case pops in.
                        Delay(AuraStampPresentation.ClaimSeconds + 1.8f); break;
                    case 5:
                        Require(screen != null && screen.Screen == FightScreen.CaseOpening, "Aura screen did not continue to the case");
                        Require(screen.CaseUi.Rarity.text == "COMMON" && screen.CaseUi.Hint.text.StartsWith("Tap to upgrade"), "Welcome case is not an upgradable level-1 case");
                        Require(!Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(t => t.name == "AuraEnergy"), "Vortex still on the case screen");
                        screen.TapCase(); Delay(1.2); break;
                    case 6: screen.TapCase(); Delay(1.2); break;
                    case 7: screen.TapCase(); Delay(1.2); break;
                    case 8:
                        Require(screen.CaseUi.Hint.text == "Tap to open case", "Three taps did not ready the case");
                        screen.TapCase(); Delay(1.6); break;
                    case 9:
                        Require(screen != null && screen.Screen == FightScreen.CaseReward, "Case did not reveal prize");
                        Require(!Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(t => t.name == "AuraStamp"), "Stamp still on the gem prize");
                        Delay(1); break;
                    case 10:
                        Require(screen.PrizeUi.ClaimButton.interactable && screen.PrizeUi.Amount.text.StartsWith("×"), "Gem prize did not settle");
                        screen.ClaimPrize(); screen.ClaimPrize();
                        Require(!screen.PrizeUi.ClaimButton.interactable, "Duplicate claim not locked");
                        Require(PlayerPrefs.GetString("rewards.case_ledger.v1", "") == _wallet, "Preview changed real wallet");
                        Finish("PASS: summary (no Aura) → +200 AURA stamp and skull (reveal cannot be skipped, collect once) → level-1 case upgraded by taps → gem prize; preview leaves wallet unchanged.");
                        break;
                }
            }
            catch (Exception exception) { Debug.LogException(exception); Finish("FAIL: " + exception); }
        }
        private static void Finish(string report)
        {
            Directory.CreateDirectory("output/assessment-rewards");
            File.WriteAllText("output/assessment-rewards/play-validation.txt", report);
            Debug.Log("[AssessmentRewardPlayValidation] " + report);
            EditorApplication.update -= Tick;
            HomeRewardFlight.Clear();
            EditorApplication.isPlaying = false;
        }
        private static void Delay(double seconds)
        { _due = EditorApplication.timeSinceStartup + seconds; _frameDue = Time.frameCount + 3; }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}

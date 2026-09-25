using System;
using System.IO;
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
                switch (_step++)
                {
                    case 0:
                        FightScreenNavigation.PrepareAssessmentPreview();
                        SceneManager.LoadScene("RewardSummary"); Delay(2); break;
                    case 1:
                        Require(screen != null && screen.Screen == FightScreen.RewardSummary, "Summary scene missing");
                        Require(screen.SummaryUi.Title.text == "ASSESSMENT COMPLETE!" && screen.SummaryUi.TotalReps.text == "8", "Real summary bindings show wrong result: " + screen.SummaryUi.Title.text + "/" + screen.SummaryUi.TotalReps.text + "; preview aura=" + FightScreenNavigation.PreviewAura + "; mode=" + FightScreenNavigation.Result?.Mode);
                        Require(screen.SummaryUi.EnergyXp.text == "+80" && screen.SummaryUi.AssessmentBonus.activeSelf && !screen.SummaryUi.TrophyGroup.activeSelf, "Wrong displayed rewards");
                        Require(screen.SummaryUi.ContinueLabel.text == "OPEN CASE", "Wrong primary action");
                        screen.Home(); Delay(.9); break;
                    case 2:
                        Require(screen != null && screen.Screen == FightScreen.CaseOpening, "Summary did not open case");
                        Require(screen.CaseUi.Hint.text == "Tap to open case", "Welcome case unexpectedly requires upgrades");
                        screen.TapCase(); screen.TapCase(); Delay(1.05); break;
                    case 3:
                        Require(screen != null && screen.Screen == FightScreen.CaseReward, "Case did not reveal prize");
                        Require(!screen.PrizeUi.ClaimButton.interactable, "Claim unlocked before reveal");
                        screen.ClaimPrize(); Delay(1.8); break;
                    case 4:
                        Require(screen != null && screen.Screen == FightScreen.CaseReward, "Early tap skipped reveal");
                        Require(screen.PrizeUi.Amount.text == "+200" && screen.PrizeUi.ClaimButton.interactable, "Aura prize did not settle");
                        var rig = screen.GetComponent<AuraRewardPresentation>();
                        Require(rig.Energy.isActiveAndEnabled && rig.Energy.Power > .5f && rig.Title.text == "AURA" && rig.Flame.isActiveAndEnabled, "Aura presentation not active");
                        Require(!screen.PrizeUi.Note.gameObject.activeSelf, "Removed explanatory text returned");
                        Require(rig.Energy.material.shader.name == "PushStars/UI Aura Vortex", "Wrong fire shader");
                        _fireClock = rig.Energy.material.GetVector("_Flow").x;
                        Delay(.3); break;
                    case 5:
                        Require(screen.GetComponent<AuraRewardPresentation>().Energy.material.GetVector("_Flow").x > _fireClock, "Fire stopped after reveal");
                        screen.ClaimPrize(); screen.ClaimPrize();
                        Require(!screen.PrizeUi.ClaimButton.interactable, "Duplicate claim not locked");
                        Require(PlayerPrefs.GetString("rewards.case_ledger.v1", "") == _wallet, "Preview changed real wallet");
                        Finish("PASS: actual summary → opening → +200 Aura; fire shader continues flowing; explanatory text hidden; double taps ignored; reveal blocks early claim; preview leaves wallet unchanged.");
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

using System;
using System.IO;
using System.Reflection;
using PushStars.Core;
using PushStars.UI;
using PushStars.Fight;
using TMPro;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    [InitializeOnLoad]
    public static class AvatarUnlockPlayValidation
    {
        private const string Key = "AvatarUnlock.PlayValidation";
        private static int _step;
        private static double _next;
        private static double _rewardDeadline;
        private static CaseRewardLedger _ledger;
        private static object _originalLedger;
        static AvatarUnlockPlayValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Key, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                { _step = 0; _next = EditorApplication.timeSinceStartup + 3; EditorApplication.update += Tick; }
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    if (SessionState.GetBool(Key + "HadHome", false)) PlayerPrefs.SetInt(CharacterRoster.HomeAvatarKey, SessionState.GetInt(Key + "Home", 0));
                    else PlayerPrefs.DeleteKey(CharacterRoster.HomeAvatarKey);
                    PlayerPrefs.Save(); SessionState.SetBool(Key, false);
                }
            };
        }
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first");
            SessionState.SetBool(Key + "HadHome", PlayerPrefs.HasKey(CharacterRoster.HomeAvatarKey));
            SessionState.SetInt(Key + "Home", PlayerPrefs.GetInt(CharacterRoster.HomeAvatarKey));
            SessionState.SetBool(Key, true);
            Directory.CreateDirectory("output/avatar-collection");
            EditorApplication.isPlaying = true;
        }
        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 1.5;
            try
            {
                var screen = UnityEngine.Object.FindFirstObjectByType<AvatarCollectionScreen>();
                if (_step == 0)
                {
                    // Inject a disposable wallet: validation never grants currency/cards to the player.
                    _ledger = new CaseRewardLedger(null, () => 0, _ => { });
                    _originalLedger = typeof(CaseRewards).GetField("_ledger", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                    typeof(CaseRewards).GetField("_ledger", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, _ledger);
                    screen.Show();
                    Require(screen.Actions[3].text == "50K <sprite name=\"aura\"> TO GO", "Initial goal and Aura icon");
                    Require(screen.Cards[3].GetComponent<AvatarUnlockView>().Count.text == "0 / 50K", "Zero progress");
                }
                else if (_step == 1)
                {
                    AvatarCollectionValidation.Capture(screen, 780, 1688, "cards-empty.png");
                    for (int n = 0; n < 3; n++)
                    {
                        string id = "visual-" + n;
                        _ledger.TryGrantCase(id);
                        for (int tap = 0; tap < 3; tap++) _ledger.TryUpgrade(id, tap, out _);
                        _ledger.TryOpen(id, out _); _ledger.TryClaim(id, out _);
                    }
                    // 30 cards (30K) plus a 12K peak; the later loss lowers the balance, never the bar.
                    _ledger.ApplyAura(new[] { new AuraGrant("visual-win", 12000) });
                    _ledger.ApplyAura(new[] { new AuraGrant("visual-loss", -5000) });
                    screen.RefreshSelection();
                    Require(screen.Actions[3].text == "8K <sprite name=\"aura\"> TO GO", "Remaining Aura and icon");
                    Require(screen.Cards[3].GetComponent<AvatarUnlockView>().Count.text == "42K / 50K", "Partial progress");
                }
                else if (_step == 2)
                {
                    AvatarCollectionValidation.Capture(screen, 780, 1688, "cards-progress.png");
                    AvatarCollectionValidation.Capture(screen, 750, 1334, "cards-compact.png");
                    screen.ShowInfo(3);
                }
                else if (_step == 3)
                {
                    AvatarCollectionValidation.Capture(screen, 780, 1688, "robot-unlock-preview.png");
                    Require(!screen.PreviewPage.Action.interactable && screen.PreviewPage.Status.text.StartsWith("UNLOCKS AT 50K"), "Locked Aura hero is not buyable");
                    screen.PreviewPage.Action.onClick.Invoke();
                    Require(!_ledger.OwnsAvatar("robot") && _ledger.AuraBalance == 7000, "Aura was spent");
                    var unlock = _ledger.ApplyAura(new[] { new AuraGrant("visual-goal", 13000) });
                    Require(unlock.Unlocked.Contains("robot") && _ledger.OwnsAvatar("robot") && _ledger.AuraPeak == 20000, "Reaching the goal unlocks without spending");
                    screen.ShowInfo(3);
                    screen.PreviewPage.Action.onClick.Invoke();
                    Require(screen.Roster.IsRobot, "Unlock and equip");
                    screen.CloseInfo();
                }
                else if (_step == 4)
                {
                    Require(!screen.Cards[3].GetComponent<AvatarUnlockView>().ProgressRoot.activeSelf && screen.Actions[3].text == "SELECTED", "Unlocked UI");
                    screen.Filter(1);
                    Require(screen.Cards[3].gameObject.activeSelf && !screen.Cards[4].gameObject.activeSelf, "Owned filter");
                    screen.Filter(2);
                    Require(screen.Empty.gameObject.activeSelf, "Premium empty state");
                    _ledger.TryGrantCase("reward-visual");
                    for (int tap = 0; tap < 3; tap++) _ledger.TryUpgrade("reward-visual", tap, out _);
                    _ledger.TryOpen("reward-visual", out _);
                    Require(FightScreenNavigation.OpenCase("reward-visual"), "Open persisted reward screen");
                    _rewardDeadline = EditorApplication.timeSinceStartup + 20;
                    _next = EditorApplication.timeSinceStartup + 4;
                }
                else if (_step == 5)
                {
                    var reward = UnityEngine.Object.FindFirstObjectByType<RewardScreen>();
                    bool ready = reward != null && reward.GetComponentsInChildren<TextMeshProUGUI>().Any(t => t.text.Contains("10 GLADIATOR CARDS"));
                    if (!ready && EditorApplication.timeSinceStartup < _rewardDeadline) return;
                    Require(ready, "Hero cards missing from reward screen");
                    Require(reward.GetComponentsInChildren<UnityEngine.UI.Image>().Any(i => i.name == "HeroCardIcon" && i.sprite != null), "Hero reward icon missing");
                    ScreenCapture.CaptureScreenshot("output/avatar-collection/case-cards.png");
                }
                else if (_step == 6)
                {
                    UnityEngine.Object.FindFirstObjectByType<RewardScreen>().ClaimPrize();
                    Require(_ledger.CardsFor("gladiator") == 10 && _ledger.Find("reward-visual") == null, "Reward UI did not claim hero cards");
                }
                else
                {
                    File.WriteAllText("output/avatar-collection/unlocks-validation.txt", "PASS: zero/partial Aura progress, remaining-Aura labels, locked hero not buyable, loss keeps the bar, goal unlock without spending, equip Robot, progress hidden after unlock, owned/premium filters, persisted case reward icon/count and UI claim; mobile renders at 780x1688 and 750x1334. Test wallet discarded; saved selection restored.\n");
                    Finish(); return;
                }
                _step++;
            }
            catch (Exception exception)
            {
                File.WriteAllText("output/avatar-collection/unlocks-validation.txt", "FAIL: " + exception);
                Debug.LogException(exception); Finish();
            }
        }
        private static void Finish()
        {
            typeof(CaseRewards).GetField("_ledger", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, _originalLedger);
            EditorApplication.update -= Tick; EditorApplication.isPlaying = false;
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}

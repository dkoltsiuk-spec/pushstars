using System;
using PushStars.Core;
using PushStars.Services;
using PushStars.UI;
using UnityEngine;

namespace PushStars.Fight
{
    public sealed partial class FightController
    {
        private GhostMotionClip _motion = new GhostMotionClip();
        public bool TryGetRecordingClock(out byte phase, out float elapsed)
        {
            phase = GhostMotionClip.Live;
            elapsed = Mathf.Clamp(Time.time - _liveStartTime, 0, FightConfig.DuelDurationSec);
            if (_paused || _layoutPaused || _bossEnding || _screenPreview) return false;
            if (_phase == Phase.Live) return true;
            phase = GhostMotionClip.Setup;
            elapsed = Time.time - _sceneStartTime;
            return FightRequest.IsBotRecording && (_phase == Phase.WaitPlank || _phase == Phase.Countdown);
        }
        public GhostMotionClip RecordedMotion => FightRequest.IsBotRecording ? BotRecorderFlow.Motion : _motion;
        private bool FinishBotTest()
        {
            if (!FightRequest.IsBotTest) return false;
            _ghost?.StopPlayback();
            if (FightRequest.IsBotRecording)
            {
                try
                {
                    if (!RecordedMotion.HasPhase(GhostMotionClip.Live))
                        throw new InvalidOperationException("The avatar animation was not captured. Please record again.");
                    var record = NewRecord("bot-recording");
                    var bot = new BotRecording
                    {
                        id = Guid.NewGuid().ToString("N"), ownerUid = LeagueClient.Uid,
                        displayName = "TEST BOT", countryCode = string.IsNullOrEmpty(ProfileCountry.Code) ? "ZZ" : ProfileCountry.Code,
                        avatar = CharacterRoster.SavedHomeAvatar, gender = (int)CharacterRoster.SavedGender,
                        trophies = LocalProfile.Trophies, fight = record
                    };
                    BotRecordingStore.Save(bot);
                    BotRecorderFlow.Notice = $"SAVED ON DEVICE · {record.reps} REPS\nUploading recording…";
                    BotRecorderFlow.PendingUpload = bot;
                }
                catch (Exception e) { BotRecorderFlow.Notice = "RECORDING NOT SAVED\n" + e.Message; Debug.LogException(e); }
            }
            else BotRecorderFlow.Notice = $"TEST COMPLETE · YOU {_repTimes.Count} : BOT {_opponent.Reps}\nTest battles do not change rewards or rating.";
            BotRecorderFlow.ShowOnMain = true;
            FightRequest.Clear();
            FightScreenNavigation.ReturnTo(FightConfig.MainSceneName);
            return true;
        }
    }
    public static class BotRecorderFlow
    {
        public static GhostMotionClip Motion = new GhostMotionClip();
        public static bool ShowOnMain;
        public static string Notice = "Record a full 60-second duel, then replay the saved bot.";
        public static BotRecording PendingUpload;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Motion = new GhostMotionClip(); ShowOnMain = false; PendingUpload = null; }
    }
}

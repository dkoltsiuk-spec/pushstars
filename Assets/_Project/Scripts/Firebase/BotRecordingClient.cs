using System;
using System.Threading.Tasks;
using PushStars.Core;

namespace PushStars.Services
{
    public static class BotRecordingClient
    {
        [Serializable] public sealed class Access { public string uid; public bool enabled; }
        [Serializable] private sealed class SaveRequest { public BotRecording recording; }
        [Serializable] private sealed class Saved { public string id, cloudPath; public int frames; public bool saved; }
        [Serializable] private sealed class IdRequest { public string id; }
        [Serializable] private sealed class Downloaded { public BotRecording recording; }
        [Serializable] private sealed class Empty { }
        public static async Task<Access> GetAccess()
        {
            await FirebaseAuthService.EnsureReadyAsync();
            return await LeagueClient.Call<Empty, Access>("getBotRecorderAccess", new Empty(), LeagueClient.Uid);
        }
        public static async Task Upload(BotRecording bot)
        {
            await FirebaseAuthService.EnsureReadyAsync();
            if (string.IsNullOrEmpty(bot.ownerUid)) { bot.ownerUid = LeagueClient.Uid; BotRecordingStore.Save(bot); }
            if (bot.ownerUid != LeagueClient.Uid) throw new InvalidOperationException("Sign in to the recording owner's account.");
            var result = await LeagueClient.Call<SaveRequest, Saved>("saveBotRecording", new SaveRequest { recording = bot }, bot.ownerUid);
            if (!result.saved || result.id != bot.id || string.IsNullOrEmpty(result.cloudPath))
                throw new InvalidOperationException("Cloud save was not confirmed.");
            bot.cloudPath = result.cloudPath;
            BotRecordingStore.Save(bot);
        }
        public static async Task<BotRecording> Download(string id)
        {
            await FirebaseAuthService.EnsureReadyAsync();
            var result = await LeagueClient.Call<IdRequest, Downloaded>("getBotRecording", new IdRequest { id = id }, LeagueClient.Uid);
            if (result.recording == null || !result.recording.IsValid) throw new InvalidOperationException("Cloud recording is invalid.");
            var clip = GhostMotionClip.Decode(result.recording.fight.motionBase64);
            if (clip == null || !clip.HasPhase(GhostMotionClip.Live)) throw new InvalidOperationException("Cloud recording has no animation.");
            return result.recording;
        }
    }
}

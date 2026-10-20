using System.IO;
using System.Reflection;
using PushStars.Core;
using PushStars.CV;
using PushStars.Fight;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PushStars.Editor
{
    /// <summary>
    /// A whole live duel on the fight screen without phones or a network: the player is a scripted
    /// push-up (<see cref="MockPoseSource"/> in place of the camera), the
    /// opponent is a second <see cref="LiveDuelLink"/> over <see cref="LoopbackDuelTransport"/>
    /// that this tool plays — it planks, lets the host's shared start run, streams a pose at
    /// 12 Hz and scores a rep every 1.5 s. Every second it writes the fight's state to
    /// Logs/live-fight-play.txt, so a freeze shows as the log stopping.
    /// </summary>
    public static class LiveFightPlayTest
    {
        private const string Pending = "PushStars.LiveFightPlayTest.Pending";
        private const string LogPath = "Logs/live-fight-play.txt";
        private static LoopbackDuelTransport _peerNet;
        private static LiveDuelLink _peer, _me;
        private static float _last, _nextPose, _nextScore, _nextLog, _startedAt;
        private static int _peerReps, _myReps;
        /// <summary>The scripted opponent's pace; the same as the player's 2 s makes a draw.</summary>
        public static float PeerRepSec = 2f;
        public static int PeerReps = 20;
        private static float _nextMyRep = float.MaxValue;
        private static bool _running, _summaryLogged;
        private static string _scene = "", _pressed = "";
        private static float _sceneAt;
        private static readonly float[] Shots = { 3f, 8f, 11f, 12.5f, 15f, 20f };
        private static int _shots;

        /// <summary>Both stages' renders as PNGs in output/live-fight, to see the opponent.</summary>
        private static void Capture(string label)
        {
            Directory.CreateDirectory("output/live-fight");
            foreach (var avatar in Object.FindObjectsByType<FightAvatar>(FindObjectsSortMode.None))
            {
                var rt = avatar.StageCamera != null ? avatar.StageCamera.targetTexture : null;
                if (rt == null) continue;
                bool opponent = (bool)typeof(FightAvatar).GetField("_opponentStage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(avatar);
                avatar.StageCamera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                // Only the part of the render the screen shows.
                var stage = avatar.StageCamera.GetComponentInParent<CharacterStage>();
                Rect uv = stage != null ? stage.DisplayUv : new Rect(0, 0, 1, 1);
                var px = new Rect(uv.x * rt.width, uv.y * rt.height, uv.width * rt.width, uv.height * rt.height);
                var tex = new Texture2D(Mathf.Max(1, (int)px.width), Mathf.Max(1, (int)px.height), TextureFormat.RGBA32, false);
                tex.ReadPixels(px, 0, 0);
                tex.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes($"output/live-fight/{label}_{(opponent ? "opponent" : "player")}.png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                var anim = avatar.Character != null ? avatar.Character.GetComponentInChildren<Animator>() : null;
                Log($"shot {label} {(opponent ? "opp" : "me")} cam={avatar.StageCamera.transform.position} root={(anim != null ? anim.transform.localPosition.ToString() : "-")} rot={(anim != null ? anim.transform.localRotation.eulerAngles.ToString() : "-")}");
            }
        }

        [MenuItem("Tools/Push Stars/Live Duel/Play Live Fight (Loopback)")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[LiveFightPlay] Stop play mode first."); return; }
            SessionState.SetBool(Pending, true);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/_Project/Scenes/Fight.unity");
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                {
                    SessionState.SetBool(Pending, false);
                    EditorSceneManager.playModeStartScene = null;
                    Begin();
                }
                else if (state == PlayModeStateChange.ExitingPlayMode && _running) End();
            };
        }

        private static void Begin()
        {
            Application.runInBackground = true;
            Directory.CreateDirectory("Logs");
            File.WriteAllText(LogPath, "");
            var (host, guest) = LoopbackDuelTransport.Pair();
            _peerNet = host;
            _peer = new LiveDuelLink(host, "playtest0000000000000", "pd_playtest", true,
                new LiveDuelSeat { Uid = "peer", Name = "PEER", Hero = 0, Gender = 1, Country = "UA" });
            _me = new LiveDuelLink(guest, "playtest0000000000000", "pd_playtest", false,
                new LiveDuelSeat { Uid = "me", Name = "ME", Hero = 0, Gender = 0, Country = "MD" });
            float now = Time.realtimeSinceStartup;
            _peer.Open("t", now); _me.Open("t", now);
            LiveDuelLink.SetActive(_me);
            FightRequest.Live(_me, ranked: true);
            _shots = 0; _summaryLogged = false; _scene = _pressed = "";
            _last = _nextPose = _nextLog = _startedAt = now; _nextScore = _nextMyRep = float.MaxValue; _peerReps = _myReps = 0;
            _running = true;
            SceneManager.sceneLoaded += OnLoaded;
            EditorApplication.update += Pump;
            SceneManager.LoadScene(FightConfig.FightSceneName);
            Log("begin");
        }

        private static void End()
        {
            _running = false;
            SceneManager.sceneLoaded -= OnLoaded;
            EditorApplication.update -= Pump;
            _peer?.Leave(); _me?.Leave();
            Log("end");
        }

        /// <summary>The camera's pose source is swapped for the scripted push-up.</summary>
        private static void OnLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != FightConfig.FightSceneName) return;
            foreach (var session in Object.FindObjectsByType<PushupSession>(FindObjectsSortMode.None))
            {
                session.enabled = false;
                foreach (var behaviour in session.GetComponents<MonoBehaviour>())
                    if (behaviour is IPoseSource source && !(behaviour is MockPoseSource))
                    { source.StopTracking(); behaviour.enabled = false; }
                var mock = session.gameObject.AddComponent<MockPoseSource>();
                var type = typeof(PushupSession);
                type.GetField("_poseSourceBehaviour", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(session, mock);
                type.GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(session, mock);
                session.enabled = true;
                Log("pose source -> " + session.gameObject.name);
            }
        }

        private static void Pump()
        {
            if (!_running || !EditorApplication.isPlaying) return;
            float now = Time.realtimeSinceStartup;
            _peerNet.AdvanceClock(Mathf.RoundToInt((now - _last) * 1000f));
            _last = now;
            _peer.Tick(now);
            if (!_peer.Local.Armed && now - _startedAt > 4f) _peer.SetArmed(true);
            // The scripted player is not a camera plank, so the host names the shared start
            // itself once both are in the room, as it would for two planks.
            if (!_peer.HasStart && _me.PeerPresent && now - _startedAt > 6f)
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(LiveDuelLink).GetField("<StartAtMs>k__BackingField", flags).SetValue(_peer, unchecked(_peerNet.ServerTimeMs + LiveDuelLink.StartLeadMs));
                typeof(LiveDuelLink).GetField("<HasStart>k__BackingField", flags).SetValue(_peer, true);
                typeof(LiveDuelLink).GetMethod("SendStart", flags).Invoke(_peer, null);
                Log("host named the start");
            }
            var fightNow = Object.FindFirstObjectByType<FightController>();
            string phaseNow = fightNow != null ? typeof(FightController).GetField("_phase", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(fightNow)?.ToString() : "";
            if (phaseNow == "Live" && _nextMyRep == float.MaxValue) _nextMyRep = now + 2f;
            if (phaseNow == "Live" && now >= _nextMyRep && _myReps < 20)
            {
                _nextMyRep = now + 2f;
                var session = Object.FindFirstObjectByType<PushupSession>();
                var raise = typeof(PushupSession).GetField("OnRep", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(session) as System.Action<int>;
                Log($"my rep {_myReps + 1} (handlers {raise?.GetInvocationList().Length ?? 0})");
                raise?.Invoke(++_myReps);
                Log($"my rep {_myReps} done");
            }
            bool live = _peer.HasStart && _peer.SecondsUntilStart <= 0f;
            if (live && _nextScore == float.MaxValue) _nextScore = now + PeerRepSec;
            // The opponent's body is this player's own, echoed back: real frames from the
            // player stage, re-stamped on the opponent's clock — what a second phone would send.
            var heard = _peer.PeerPose.Clip.Frames;
            if (now >= _nextPose && _me.Online && heard.Count > 0)
            {
                _nextPose = now + LiveDuelLink.PoseIntervalSec;
                var src = heard[heard.Count - 1];
                _peer.SendPose(new GhostMotionClip.Frame
                {
                    phase = live ? GhostMotionClip.Live : GhostMotionClip.Setup, time = live ? -_peer.SecondsUntilStart : now - _startedAt,
                    bodyPosition = src.bodyPosition, bodyRotation = src.bodyRotation,
                    rootPosition = src.rootPosition, rootRotation = src.rootRotation, muscles = src.muscles
                });
            }
            if (_shots < Shots.Length && now - _startedAt >= Shots[_shots]) Capture($"{_shots++:00}_t{now - _startedAt:00}");
            if (live && now >= _nextScore && _peerReps < PeerReps)
            {
                _nextScore = now + PeerRepSec;
                _peer.SendScore(++_peerReps, 0, 80, -_peer.SecondsUntilStart);
            }
            if (!_summaryLogged && SceneManager.GetActiveScene().name != FightConfig.FightSceneName && FightScreenNavigation.Result != null)
            {
                _summaryLogged = true;
                var r = FightScreenNavigation.Result; var sum = FightScreenNavigation.RewardSummary;
                Log($"RESULT win={r.Win} draw={r.Draw} me={r.MyReps} opp={r.OppReps} trophies={r.Trophies} status={r.LeagueStatus} | " +
                    $"aura={sum.Aura} case={sum.HasCase} moments=[{string.Join("; ", sum.AuraMoments ?? new string[0])}] scene={SceneManager.GetActiveScene().name}");
            }
            // After the fight: press on through the reward screens like a player would.
            string scene = SceneManager.GetActiveScene().name;
            if (scene != _scene) { _scene = scene; _sceneAt = now; Log("scene -> " + scene); }
            if (scene == "RewardSummary" && now - _sceneAt > 5f && _pressed != scene)
            { _pressed = scene; Object.FindFirstObjectByType<RewardScreen>()?.Home(); Log("pressed continue on " + scene); }
            if (scene == "AuraReward" && now - _sceneAt > 8f && _pressed != scene)
            { _pressed = scene; Object.FindFirstObjectByType<AuraRewardScreen>()?.Collect(); Log("collected aura"); }
            if (now >= _nextLog)
            {
                _nextLog = now + 1f;
                var fight = Object.FindFirstObjectByType<FightController>();
                string phase = fight != null ? typeof(FightController).GetField("_phase", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(fight)?.ToString() : "-";
                var reps = fight != null ? typeof(FightController).GetField("_repTimes", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(fight) as System.Collections.ICollection : null;
                Log($"t={now - _startedAt:0.0} frame={Time.frameCount} phase={phase} myReps={reps?.Count} peerReps={_peerReps} " +
                    $"meArmed={_me.Local.Armed} start={_me.HasStart} until={_me.SecondsUntilStart:0.0} peerSeen={_peer.PeerReps} " +
                    $"meStatus={_me.Status} peerPresent={_me.PeerPresent}");
            }
        }

        private static void Log(string line) => File.AppendAllText(LogPath, line + "\n");
    }
}

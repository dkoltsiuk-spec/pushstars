using System;
using System.IO;
using PushStars.Core;
using PushStars.Fight;
using PushStars.UI;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>
    /// The live duel without a network: two <see cref="LiveDuelLink"/>s joined by a
    /// <see cref="LoopbackDuelTransport"/> run the whole room protocol — introductions, the ready
    /// check, the shared start on the server clock, the score and pose streams, a dropped
    /// connection inside and past the reconnect window, a deliberate exit, and leaving after the
    /// bell — plus the pose codec and the <see cref="LiveOpponent"/> feed the fight screen reads.
    /// </summary>
    public static class LiveDuelRegression
    {
        private const float Frame = 1f / 30f;

        [MenuItem("Tools/Push Stars/Live Duel/Validate Live Duel")]
        public static void Run()
        {
            Codec();
            Protocol();
            Lobby();
            Streams();
            Disconnects();
            Opponent();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/live-duel-regression.txt",
                "PASS: pose codec, protocol validation, lobby + ready + shared start, score/pose streams, " +
                "reconnect inside the window, forfeit past it, deliberate exit, leaving after the bell, version mismatch, LiveOpponent feed.\n");
            Debug.Log("[LiveDuelRegression] PASS");
        }

        private sealed class Pair
        {
            public LoopbackDuelTransport HostNet, GuestNet;
            public LiveDuelLink Host, Guest;
            public float Now;
            public void Step(float seconds = Frame)
            {
                for (float t = 0; t < seconds - 1e-4f; t += Frame)
                {
                    Now += Frame;
                    HostNet.AdvanceClock(Mathf.RoundToInt(Frame * 1000));
                    Host.Tick(Now); Guest.Tick(Now);
                }
            }
        }

        private static Pair NewPair()
        {
            var (h, g) = LoopbackDuelTransport.Pair();
            var p = new Pair { HostNet = h, GuestNet = g };
            p.Host = new LiveDuelLink(h, "m1", "pd_m1", true, new LiveDuelSeat { Uid = "alice", Name = "ALICE", Hero = 7, Gender = 1, Country = "RU" });
            p.Guest = new LiveDuelLink(g, "m1", "pd_m1", false, new LiveDuelSeat { Uid = "bob", Name = "BOB", Hero = 2 });
            p.Host.Open("t", 0); p.Guest.Open("t", 0);
            p.Step(.1f);
            return p;
        }

        private static GhostMotionClip.Frame Pose(byte phase, float time, float muscle)
        {
            var f = new GhostMotionClip.Frame
            {
                phase = phase, time = time, bodyPosition = new Vector3(.1f, 1.2f, -.3f),
                bodyRotation = Quaternion.Euler(10, 20, 30), rootPosition = new Vector3(0, 0, 2),
                rootRotation = Quaternion.Euler(0, 180, 0), muscles = new float[GhostMotionClip.MuscleCount]
            };
            for (int i = 0; i < f.muscles.Length; i++) f.muscles[i] = Mathf.Clamp(muscle + i * .001f, -2, 2);
            return f;
        }

        private static void Codec()
        {
            var frame = Pose(GhostMotionClip.Live, 12.345f, -.75f);
            frame.muscles[3] = 5f;   // out of range: clamped, not wrapped
            var bytes = LivePoseCodec.Encode(frame);
            Require(bytes.Length == LivePoseCodec.FrameBytes && bytes.Length < 240, $"Pose frame is {bytes.Length} bytes.");
            Require(LivePoseCodec.TryDecode(bytes, out var back), "Pose frame did not decode.");
            Require(back.phase == frame.phase && Mathf.Approximately(back.time, frame.time) && back.bodyPosition == frame.bodyPosition,
                "Pose header changed on the wire.");
            Require(Quaternion.Angle(back.bodyRotation, frame.bodyRotation) < .1f && Quaternion.Angle(back.rootRotation, frame.rootRotation) < .1f,
                "Pose rotations lost precision.");
            for (int i = 0; i < frame.muscles.Length; i++)
                Require(Mathf.Abs(back.muscles[i] - Mathf.Clamp(frame.muscles[i], -2, 2)) < .0002f, $"Muscle {i} lost precision.");
            var broken = (byte[])bytes.Clone(); broken[0] = 99;
            Require(!LivePoseCodec.TryDecode(broken, out _) && !LivePoseCodec.TryDecode(new byte[10], out _), "Malformed pose accepted.");
            var nan = (byte[])bytes.Clone(); BitConverter.GetBytes(float.NaN).CopyTo(nan, 2);
            Require(!LivePoseCodec.TryDecode(nan, out _), "A NaN clock was accepted.");
        }

        private static void Protocol()
        {
            var seat = new LiveDuelSeat { Uid = "u", Name = "<b>A_VERY_LONG_PLAYER_NAME_THAT_GOES_ON</b>", Hero = 12, Gender = 1, Ready = true, Armed = true, Finished = true, FinalReps = 31 };
            Require(LiveDuelProtocol.TryDecodeState(LiveDuelProtocol.EncodeState(seat), out var s, out _), "State did not decode.");
            Require(s.Name.Length <= 24 && !s.Name.Contains("<") && s.Hero == 12 && s.Gender == 1 && s.Ready && s.Armed && s.Finished && s.FinalReps == 31,
                "State fields changed on the wire.");
            var old = LiveDuelProtocol.EncodeState(seat); old[0] = LiveDuelProtocol.Version + 1;
            Require(!LiveDuelProtocol.TryDecodeState(old, out _, out bool mismatch) && mismatch, "Another protocol version was not recognised.");
            var score = LiveDuelProtocol.EncodeScore(new LiveDuelProtocol.ScoreUpdate { Reps = 66, Claps = 1, Form = 80, Elapsed = 10 });
            Require(!LiveDuelProtocol.TryDecodeScore(score, out _), "A score past the server's 65-rep cap was accepted.");
        }

        private static void Lobby()
        {
            var p = NewPair();
            Require(p.HostNet.OpenedRoom == "pd_m1" && p.GuestNet.OpenedRoom == "pd_m1", "Both seats must open the match's room.");
            p.Step(.2f);
            Require(p.Host.Peer?.Name == "BOB" && p.Guest.Peer?.Name == "ALICE" && p.Guest.Peer.Hero == 7 && p.Guest.Peer.Gender == 1,
                "Seats did not introduce themselves.");
            FightRequest.Live(p.Guest);
            Require(FightRequest.Mode == FightMode.Ghost && FightRequest.IsLiveDuel && !FightRequest.IsBotTest, "Live duel request wrong.");
            Require(FightRequest.TryGetOpponentLook(out int hero, out int gender) && hero == 7 && gender == 1, "Opponent stage would not build the friend's hero.");
            FightRequest.Ghost();
            Require(!FightRequest.IsLiveDuel && !FightRequest.TryGetOpponentLook(out _, out _), "A later ghost duel kept the live opponent.");
            FightRequest.Clear();

            p.Guest.SetReady(true); p.Step();
            Require(p.Host.Peer.Ready && !p.Guest.Peer.Ready, "Ready did not reach the host.");

            // The shared start needs both planks; only the host names it.
            p.Host.SetArmed(true); p.Step(.5f);
            Require(!p.Host.HasStart && !p.Guest.HasStart, "Started with one plank.");
            p.Guest.SetArmed(true); p.Step();
            Require(p.Host.HasStart, "Host did not schedule the start.");
            p.Step();
            Require(p.Guest.HasStart && p.Guest.StartAtMs == p.Host.StartAtMs, "Guest did not receive the same start.");
            float until = p.Guest.SecondsUntilStart;
            Require(until > 3.5f && until <= LiveDuelLink.StartLeadMs / 1000f, $"Start lead is {until:0.00}s.");
            p.Host.SetArmed(false); p.Step(1f);
            Require(p.Guest.HasStart && Mathf.Abs(p.Guest.SecondsUntilStart - p.Host.SecondsUntilStart) < .001f,
                "Losing a plank must not move a start both phones count to.");
        }

        private static void Streams()
        {
            var p = NewPair();
            p.Step(.2f);
            p.Host.SendScore(3, 1, 87.4f, 4.2f); p.Step();
            Require(p.Guest.PeerReps == 3 && p.Guest.PeerClaps == 1 && Mathf.Approximately(p.Guest.PeerForm, 87), "Score did not stream.");
            p.Host.SendScore(2, 0, 80, 5); p.Step();
            Require(p.Guest.PeerReps == 3, "A stale score took reps back.");

            // 12 Hz pose stream: the friend's body plays a fixed delay behind, ordered, never backwards.
            // 10 Hz here so the test clock (30 fps) divides it evenly; the app streams at 12 Hz.
            const float interval = .1f;
            float t = 0; float last = -1;
            for (int i = 0; i < 24; i++)
            {
                p.Host.SendPose(Pose(GhostMotionClip.Live, t, t / 10f));
                t += interval;
                p.Step(interval);
                Require(p.Guest.PeerPose.PlaybackTime >= last, "Pose playback went backwards.");
                last = p.Guest.PeerPose.PlaybackTime;
            }
            var buffer = p.Guest.PeerPose;
            Require(buffer.HasPose && buffer.Phase == GhostMotionClip.Live && buffer.Received == 24, $"Pose stream lost frames ({buffer.Received}).");
            float newest = t - interval;
            Require(buffer.PlaybackTime <= newest && buffer.PlaybackTime >= newest - LivePoseBuffer.Delay - .15f,
                $"Playback {buffer.PlaybackTime:0.00}s is not ~{LivePoseBuffer.Delay}s behind {newest:0.00}s.");
            var pose = new HumanPose();
            Require(buffer.Clip.Sample(GhostMotionClip.Live, buffer.PlaybackTime, ref pose, out _, out _), "Streamed clip cannot be sampled.");
            Require(buffer.Clip.Frames.Count <= 30, $"Pose buffer keeps {buffer.Clip.Frames.Count} frames.");

            buffer.Add(Pose(GhostMotionClip.Live, .5f, 0), p.Now);
            Require(buffer.Dropped == 1, "A late pose was not dropped.");
            p.HostNet.DropUnreliable = true;
            p.Host.SendPose(Pose(GhostMotionClip.Live, t, 0)); p.Step();
            Require(buffer.Received == 24, "A lost unreliable pose was delivered.");
        }

        private static void Disconnects()
        {
            // Inside the window: the friend comes back and nothing is lost.
            var p = NewPair();
            p.Step(.5f);
            p.GuestNet.SetConnected(false);
            p.Step(4f);
            Require(!p.Host.PeerPresent && p.Host.PeerAbsentSec > 3f && !p.Host.PeerGone, "Away friend handled wrong.");
            Require(!p.Guest.Online && p.Guest.PeerAbsentSec == 0f, "An offline phone judged its friend.");
            p.GuestNet.SetConnected(true);
            p.Step(.2f);
            Require(p.Host.PeerPresent && p.Host.PeerAbsentSec == 0f && !p.Host.PeerGone, "Reconnected friend not restored.");

            // Past the window: gone, the duel can end as a walkover.
            p.GuestNet.SetConnected(false);
            p.Step(LiveDuelLink.PeerGraceSec + .5f);
            Require(p.Host.PeerGone, "Friend away past the reconnect window is not gone.");

            // A frozen phone (in the room, silent) is away too.
            var frozen = NewPair();
            frozen.Step(.5f);
            for (int i = 0; i < 120; i++) { frozen.Now += Frame; frozen.Host.Tick(frozen.Now); }   // guest never ticks
            Require(!frozen.Host.PeerPresent && frozen.Host.PeerAbsentSec > 0f, "A silent friend still counts as present.");

            // Leaving on purpose is heard at once.
            var quit = NewPair();
            quit.Step(.2f);
            quit.Guest.Leave(); quit.Step();
            Require(quit.Host.PeerGone && quit.Host.PeerQuit && quit.Guest.Closed && LiveDuelLink.Active != quit.Guest, "Exit not heard at once.");

            // Leaving after the bell: FINISHED arrives first, so it never reads as a forfeit.
            var done = NewPair();
            done.Step(.2f);
            done.Guest.Finish(27); done.Guest.Leave(); done.Step();
            Require(done.Host.PeerGone && done.Host.Peer.Finished && done.Host.PeerReps == 27, "Finished friend's exit looks like a forfeit.");

            // Two app versions must not misread each other.
            var mixed = NewPair();
            mixed.Step(.2f);
            var future = LiveDuelProtocol.EncodeState(mixed.Guest.Local); future[0]++;
            mixed.GuestNet.Send(LiveDuelProtocol.State, future, true); mixed.Step();
            Require(mixed.Host.VersionMismatch, "Another protocol version went unnoticed.");
        }

        private static void Opponent()
        {
            var p = NewPair();
            p.Step(.2f);
            var go = new GameObject("LiveOpponentRegression") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var feed = go.AddComponent<LiveOpponent>();
                feed.Configure(p.Guest);
                IOpponentFeed duel = feed; IOpponentPose body = feed;
                int reps = 0, claps = 0;
                duel.OnRep += n => reps = n; feed.OnClap += n => claps = n;
                Require(duel.DisplayName == "ALICE" && duel.ExpectedReps == 0 && !body.HasPose && !body.IsWorking, "Fresh live opponent wrong.");
                duel.Begin();
                p.Host.SendScore(4, 1, 90, 6); p.Step();
                duel.Tick(6.2f);
                Require(reps == 4 && claps == 1 && duel.Reps == 4 && Mathf.Approximately(duel.FormPercent, 90), "Live reps did not reach the feed.");
                Require(duel.SecondsPerRep > 1.4f && duel.SecondsPerRep < 1.7f, $"Tempo {duel.SecondsPerRep:0.00}s per rep.");
                p.Host.SendPose(Pose(GhostMotionClip.Setup, 3f, .2f)); p.Step();
                Require(body.HasPose && body.MotionPhase == GhostMotionClip.Setup && body.Motion == p.Guest.PeerPose.Clip, "Live pose not exposed to the avatar.");
                feed.StopPlayback();
                Require(!body.HasPose && !body.PlaybackActive, "Stopped feed still poses the body.");
            }
            finally { Object.DestroyImmediate(go); }

            var session = new FriendDuelSession();
            session.Create("123456", 0); session.Close("Connection lost. Room closed.");
            Require(session.State == FriendDuelSession.Phase.Expired && session.Notice.StartsWith("Connection lost"), "Room sheet cannot show a closed room.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception("[LiveDuelRegression] " + message);
        }
    }
}

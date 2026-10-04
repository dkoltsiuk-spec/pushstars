using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>
    /// Builds the free gesture emotes (HELLO, GG, BOO, YOU!, LOSER) by motion capture from the
    /// owner's reference videos of our own hero (Higgsfield, green screen).
    ///
    /// <para><c>Tools/EmoteMocap/extract_pose.py</c> runs MediaPipe over each video and writes a
    /// track: 33 body points and 21 points per hand, per frame, smoothed. Here every frame is
    /// retargeted onto the man's humanoid rig by solving muscles (coordinate descent, warm-started
    /// from the previous frame), group by group: the torso and the head follow their rotation
    /// relative to the video's standing frame; each arm follows the directions of the upper arm,
    /// forearm and hand; each finger follows its segment directions. Working in muscle space makes
    /// the clip humanoid, so it plays on every hero. Legs, hips and breathing come from the menu
    /// idle, so feet stay planted. The standing lead-in and tail of each video are trimmed.</para>
    /// </summary>
    public static class EmoteClipAuthoring
    {
        private const string RigPrefab = "Assets/Character/Main_man/MainMan.prefab";
        public const string TrackDir = "Tools/EmoteMocap/tracks";

        /// <summary>clip file name ← track name, with optional frame limits where the automatic
        /// trim (hands leaving their standing place) is fooled: BOO shuffles before it starts, GG
        /// holds its thumb until the video ends.</summary>
        private static readonly (string clip, string track, int first, int last)[] Captures =
        {
            ("Emote_Hello.anim", "hello", -1, -1), ("Emote_GG.anim", "gg", -1, 100), ("Emote_Boo.anim", "boo", 30, -1),
            ("Emote_You.anim", "you", -1, -1), ("Emote_Loser.anim", "loser", -1, -1),
        };

        public static void AuthorAll(string dir) => AuthorAll(dir, null);

        /// <summary>One track into one clip, outside the <see cref="Captures"/> table — for trying
        /// a new capture (another tracker, a new recording) next to the shipped clip.</summary>
        public static void AuthorTrack(string trackPath, string clipPath, int first = -1, int last = -1)
        {
            var idle = AssetDatabase.LoadAllAssetsAtPath(MainCharacterSetup.IdleFbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            var rig = new Rig(prefab, idle);
            try
            {
                var log = new StringBuilder();
                var data = JsonUtility.FromJson<Track>(File.ReadAllText(trackPath));
                var keys = Retarget(rig, data, log, Path.GetFileNameWithoutExtension(trackPath), first, last);
                PlantHips(rig, idle, keys);
                Save(clipPath, keys, rig, idle);
                AssetDatabase.SaveAssets();
                Debug.Log("[Emotes] Track to clip.\n" + log);
            }
            finally { rig.Dispose(); }
        }

        public static void AuthorAll(string dir, string only)
        {
            var idle = AssetDatabase.LoadAllAssetsAtPath(MainCharacterSetup.IdleFbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            if (idle == null || prefab == null) { Debug.LogError("[Emotes] Idle clip or MainMan prefab missing."); return; }
            var rig = new Rig(prefab, idle);
            try
            {
                var log = new StringBuilder();
                foreach (var (clip, track, first, last) in Captures)
                {
                    if (only != null && !only.Split(',').Contains(track)) continue;
                    string path = Path.Combine(TrackDir, track + ".json");
                    if (!File.Exists(path)) { log.AppendLine(track + ": no track at " + path); continue; }
                    var data = JsonUtility.FromJson<Track>(File.ReadAllText(path));
                    var keys = Retarget(rig, data, log, track, first, last);
                    PlantHips(rig, idle, keys);
                    Save(dir + "/" + clip, keys, rig, idle);
                }
                Debug.Log("[Emotes] Mocap gestures.\n" + log);
            }
            finally { rig.Dispose(); }
        }

        // ── Track ───────────────────────────────────────────────────────────────────────────

        [Serializable]
        private sealed class Track
        {
            public float fps;
            public int frames;
            public float[] body, left, right, bodyImage;
            public float aspect = 1f;
            public float leftCoverage, rightCoverage;
        }

        // MediaPipe: x image-right, y down, z away from the camera. The subject faces the camera,
        // as our rig faces +Z towards its lens, so image-right is the subject's left (-X).
        private static Vector3 Point(float[] a, int frame, int count, int i)
        {
            int k = (frame * count + i) * 3;
            return new Vector3(-a[k], -a[k + 1], -a[k + 2]);
        }

        private static Vector3 Body(Track t, int f, int i) => Point(t.body, f, 33, i);

        /// <summary>Image position in frame-height units (x right, y down).</summary>
        private static Vector2 Image(Track t, int f, int i)
        {
            int k = (f * 33 + i) * 2;
            return new Vector2(t.bodyImage[k] * t.aspect, t.bodyImage[k + 1]);
        }

        private static bool HasImage(Track t) => t.bodyImage != null && t.bodyImage.Length >= t.frames * 66;

        /// <summary>Metres per frame-height unit, from the torso (shoulders to hips).</summary>
        private static float ImageScale(Track t, int f)
        {
            float image = ((Image(t, f, 11) + Image(t, f, 12)) * .5f - (Image(t, f, 23) + Image(t, f, 24)) * .5f).magnitude;
            return (Mid(t, f, 11, 12) - Mid(t, f, 23, 24)).magnitude / Mathf.Max(1e-4f, image);
        }

        /// <summary>A limb segment's length: the longest it ever appears in the picture (when it
        /// lies parallel to it), in metres.</summary>
        private static float SegmentLength(Track t, int a, int b)
        {
            if (!HasImage(t)) return 0f;
            var lengths = new List<float>();
            for (int f = 0; f < t.frames; f++) lengths.Add((Image(t, f, b) - Image(t, f, a)).magnitude * ImageScale(t, f));
            lengths.Sort();
            return lengths[Mathf.Clamp(Mathf.RoundToInt(lengths.Count * .95f), 0, lengths.Count - 1)];
        }

        /// <summary>A segment's direction lifted from the picture: its 2D offset is exact, so only
        /// the depth is taken from the model — its sign, and a magnitude that keeps the segment at
        /// its true length. MediaPipe's own depth flattens an arm reaching toward the lens (a
        /// thumbs-up at the chin comes out at the belly).</summary>
        private static Vector3 Lifted(Track t, int f, int a, int b, float length)
        {
            var world = Body(t, f, b) - Body(t, f, a);
            if (!HasImage(t) || length <= 0f) return world.normalized;
            var planar = (Image(t, f, b) - Image(t, f, a)) * ImageScale(t, f);
            float depth = Mathf.Sqrt(Mathf.Max(0f, length * length - planar.sqrMagnitude));
            depth *= Mathf.Clamp(world.z / Mathf.Max(1e-3f, .25f * length), -1f, 1f);
            return new Vector3(-planar.x, -planar.y, depth).normalized;
        }

        private static bool HasHand(float[] hand, Track t) => hand != null && hand.Length >= t.frames * 63;

        private static Quaternion Basis(Vector3 right, Vector3 up)
        {
            var forward = Vector3.Cross(right, up);
            return forward.sqrMagnitude < 1e-8f ? Quaternion.identity : Quaternion.LookRotation(forward, up);
        }

        private static Vector3 Mid(Track t, int f, int a, int b) => (Body(t, f, a) + Body(t, f, b)) * .5f;

        private static Quaternion TorsoBasis(Track t, int f)
            => Basis(Body(t, f, 12) - Body(t, f, 11), Mid(t, f, 11, 12) - Mid(t, f, 23, 24));

        private static Quaternion HeadBasis(Track t, int f)
            => Basis(Body(t, f, 8) - Body(t, f, 7), Mid(t, f, 2, 5) - Mid(t, f, 9, 10));

        // ── Retarget ────────────────────────────────────────────────────────────────────────

        private sealed class Key
        {
            public float Time;
            public readonly Dictionary<int, float> Muscles = new Dictionary<int, float>();
            /// <summary>Body position to write instead of the idle's (see <see cref="PlantHips"/>).</summary>
            public Vector3? RootT;
            public Quaternion RootQ;
        }

        private static List<Key> Retarget(Rig rig, Track t, StringBuilder log, string name, int firstLimit, int lastLimit)
        {
            var (first, last) = ActiveSpan(t);
            if (firstLimit >= 0) first = firstLimit;
            if (lastLimit >= 0) last = Mathf.Min(last, lastLimit);
            int rest = Mathf.Max(0, Mathf.Min(3, first));
            var torsoRest = TorsoBasis(t, rest);
            var headRest = HeadBasis(t, rest);
            rig.Apply(rig.Base);
            var chestRest = rig.R(HumanBodyBones.UpperChest, HumanBodyBones.Chest);
            var headBoneRest = rig.R(HumanBodyBones.Head);
            // Where the video's ears sit, as a point riding on the rig's head bone: hands that
            // touch the head (the "L", a salute) are placed relative to it, not only by direction.
            float scale = RigShoulders(rig) / Mathf.Max(.01f, (Body(t, rest, 12) - Body(t, rest, 11)).magnitude);
            var earWorld = RigShoulderMid(rig) + (Mid(t, rest, 7, 8) - Mid(t, rest, 11, 12)) * scale;
            var headPos = rig.P(HumanBodyBones.Head);
            var earLocal = Quaternion.Inverse(headBoneRest) * (earWorld - headPos);
            var headUpLocal = Quaternion.Inverse(headBoneRest) * Vector3.up;

            var torso = Muscles("Spine Front-Back", "Spine Left-Right", "Spine Twist Left-Right",
                "Chest Front-Back", "Chest Left-Right", "Chest Twist Left-Right",
                "UpperChest Front-Back", "UpperChest Left-Right", "UpperChest Twist Left-Right");
            var head = Muscles("Neck Nod Down-Up", "Neck Tilt Left-Right", "Neck Turn Left-Right",
                "Head Nod Down-Up", "Head Tilt Left-Right", "Head Turn Left-Right");

            var m = (float[])rig.Base.Clone();
            var frames = new List<float[]>();
            float worstArm = 0f, sumArm = 0f;
            for (int f = first, n = 0; f <= last; f++, n++)
            {
                float step = n == 0 ? .25f : .06f;
                // MediaPipe's shoulder and hip lines exaggerate a lean a little, and with an arm
                // raised its shoulder depth can swing the torso round by tens of degrees that are
                // not in the picture. Keep 75% of the rotation, within what a gesture uses.
                var chestTarget = Clamp(Quaternion.Slerp(Quaternion.identity, TorsoBasis(t, f) * Quaternion.Inverse(torsoRest), .75f),
                    15f, 15f, 12f) * chestRest;
                Descend(rig, m, torso, step, () =>
                    Rad(rig.R(HumanBodyBones.UpperChest, HumanBodyBones.Chest), chestTarget) + Reg(m, torso, .02f));
                var headTarget = Clamp(HeadBasis(t, f) * Quaternion.Inverse(headRest), 25f, 35f, 20f) * headBoneRest;
                Descend(rig, m, head, step, () =>
                    Rad(rig.R(HumanBodyBones.Head), headTarget) + Reg(m, head, .02f));
                foreach (var side in new[] { Side.Left, Side.Right })
                {
                    float err = SolveArm(rig, m, t, f, side, step, earLocal, scale, headUpLocal);
                    worstArm = Mathf.Max(worstArm, err);
                    sumArm += err;
                    SolveFingers(rig, m, t, f, side, step);
                }
                frames.Add((float[])m.Clone());
            }

            var owned = torso.Concat(head).Concat(ArmMuscles(Side.Left)).Concat(ArmMuscles(Side.Right))
                .Concat(FingerMuscles(Side.Left)).Concat(FingerMuscles(Side.Right)).ToArray();
            Smooth(frames, owned);
            var keys = new List<Key>();
            for (int i = 0; i < frames.Count; i++)
            {
                var key = new Key { Time = i / t.fps };
                foreach (int mu in owned) key.Muscles[mu] = frames[i][mu];
                keys.Add(key);
            }
            log.AppendLine($"{name}: frames {first}–{last} of {t.frames} ({frames.Count / t.fps:0.00} s), " +
                           $"arm direction error mean {sumArm / Mathf.Max(1, frames.Count * 2) * Mathf.Rad2Deg:0.0}°, " +
                           $"worst {worstArm * Mathf.Rad2Deg:0.0}°; hands L {t.leftCoverage:P0} R {t.rightCoverage:P0}");
            return keys;
        }

        /// <summary>
        /// A humanoid clip places the body by its centre of mass. Raise an arm with the idle's
        /// root curve and Unity keeps the centre of mass where it was — so the pelvis swings the
        /// other way and the feet skate. Per key, the body position is solved so the hips sit
        /// exactly where the idle has them at that moment.
        /// </summary>
        private static void PlantHips(Rig rig, AnimationClip idle, List<Key> keys)
        {
            foreach (var key in keys)
            {
                var idlePose = rig.SampleIdle(idle, Mathf.Repeat(key.Time, idle.length));
                var target = rig.P(HumanBodyBones.Hips);
                var targetRotation = rig.R(HumanBodyBones.Hips);
                var pose = idlePose;
                pose.muscles = (float[])idlePose.muscles.Clone();
                foreach (var m in key.Muscles) pose.muscles[m.Key] = m.Value;
                // The body frame averages hips and chest, so a leaning chest also turns the hips
                // (and swings the legs) unless the body rotation is corrected too.
                for (int i = 0; i < 6; i++)
                {
                    rig.SetPose(pose);
                    pose.bodyRotation = targetRotation * Quaternion.Inverse(rig.R(HumanBodyBones.Hips)) * pose.bodyRotation;
                    rig.SetPose(pose);
                    var miss = target - rig.P(HumanBodyBones.Hips);
                    pose.bodyPosition += miss / Mathf.Max(.01f, rig.HumanScale);
                }
                key.RootT = pose.bodyPosition;
                key.RootQ = pose.bodyRotation;
            }
        }

        /// <summary>The span where the hands move away from where they stood at the start, padded
        /// so the clip still opens and closes on the standing pose.</summary>
        private static (int first, int last) ActiveSpan(Track t)
        {
            Vector3 Local(int f, int i) => Body(t, f, i) - Mid(t, f, 23, 24);
            var restL = Local(0, 15); var restR = Local(0, 16);
            int first = -1, last = -1;
            for (int f = 0; f < t.frames; f++)
            {
                float d = Mathf.Max((Local(f, 15) - restL).magnitude, (Local(f, 16) - restR).magnitude);
                if (d < .2f) continue;
                if (first < 0) first = f;
                last = f;
            }
            if (first < 0) return (0, t.frames - 1);
            int lead = Mathf.RoundToInt(.3f * t.fps), tail = Mathf.RoundToInt(.4f * t.fps);
            return (Mathf.Max(0, first - lead), Mathf.Min(t.frames - 1, last + tail));
        }

        private enum Side { Left, Right }

        private static int[] ArmMuscles(Side s)
        {
            string p = s == Side.Right ? "Right " : "Left ";
            return Muscles(p + "Shoulder Down-Up", p + "Shoulder Front-Back", p + "Arm Down-Up", p + "Arm Front-Back",
                p + "Arm Twist In-Out", p + "Forearm Stretch", p + "Forearm Twist In-Out", p + "Hand Down-Up", p + "Hand In-Out");
        }

        private static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };

        private static int[] FingerMuscles(Side s)
        {
            string p = s == Side.Right ? "Right " : "Left ";
            var list = new List<string>();
            foreach (var f in Fingers)
            {
                for (int j = 1; j <= 3; j++) list.Add(p + f + " " + j + " Stretched");
                list.Add(p + f + " Spread");
            }
            return Muscles(list.ToArray());
        }

        /// <summary>Upper arm, forearm and (when the hand was tracked) hand direction and knuckle
        /// line. Returns the remaining mean direction error of the two arm segments, radians.</summary>
        /// <summary>Metres the wrist keeps from the rig's head centre, as seen by the lens, when the
        /// video's hand is not touching the head.</summary>
        private const float HeadClearance = .25f;

        /// <summary>Metres from the head bone (base of the skull) up to the skull's centre.</summary>
        private const float SkullAboveHeadBone = .11f;

        private static float RigShoulders(Rig rig)
            => (rig.P(HumanBodyBones.RightUpperArm) - rig.P(HumanBodyBones.LeftUpperArm)).magnitude;

        private static Vector3 RigShoulderMid(Rig rig)
            => (rig.P(HumanBodyBones.RightUpperArm) + rig.P(HumanBodyBones.LeftUpperArm)) * .5f;

        private static float SolveArm(Rig rig, float[] m, Track t, int f, Side side, float step, Vector3 earLocal, float scale,
            Vector3 headUpLocal)
        {
            bool right = side == Side.Right;
            int sh = right ? 12 : 11, el = right ? 14 : 13, wr = right ? 16 : 15;
            float upperLength = SegmentLength(t, sh, el), foreLength = SegmentLength(t, el, wr);
            var upper = Lifted(t, f, sh, el, upperLength);
            var fore = Lifted(t, f, el, wr, foreLength);
            var hand = right ? t.right : t.left;
            bool tracked = HasHand(hand, t);
            Vector3 handFwd = default, knuckles = default, thumb = default;
            if (tracked)
            {
                handFwd = (Point(hand, f, 21, 9) - Point(hand, f, 21, 0)).normalized;
                knuckles = (Point(hand, f, 21, 5) - Point(hand, f, 21, 17)).normalized;
                thumb = (Point(hand, f, 21, 3) - Point(hand, f, 21, 2)).normalized;
            }
            var bUpper = right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm;
            var bLower = right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm;
            var bHand = right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand;
            var bMiddle = right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal;
            var bIndex = right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal;
            var bLittle = right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal;
            var bThumbMid = right ? HumanBodyBones.RightThumbIntermediate : HumanBodyBones.LeftThumbIntermediate;
            var bThumbEnd = right ? HumanBodyBones.RightThumbDistal : HumanBodyBones.LeftThumbDistal;
            var arm = ArmMuscles(side);
            // Contact with the head (the "L" on the forehead: the wrist within ~17 cm of the ears in
            // the picture) also holds the wrist at the same offset from the rig's ears. A fist merely
            // beside the head (BOO, 22 cm and up) is left to the directions — our hero's head is
            // bigger than the video's, and pulling it in would bury the hand in the hair.
            var wristLifted = upperLength > 0f
                ? Body(t, f, sh) + upper * upperLength + fore * foreLength
                : Body(t, f, wr);
            var fromEars = (wristLifted - Mid(t, f, 7, 8)) * scale;
            float contact = HasImage(t)
                ? Mathf.Clamp01((.2f - (Image(t, f, wr) - (Image(t, f, 7) + Image(t, f, 8)) * .5f).magnitude * ImageScale(t, f)) / .03f)
                : Mathf.Clamp01((.2f - fromEars.magnitude / scale) / .03f);

            float Segments() => Miss(rig.P(bLower) - rig.P(bUpper), upper) + Miss(rig.P(bHand) - rig.P(bLower), fore);
            Descend(rig, m, arm, step, () =>
            {
                float c = Segments() * 1f;
                var ears = rig.P(HumanBodyBones.Head) + rig.R(HumanBodyBones.Head) * earLocal;
                if (contact > 0f)
                    c += (rig.P(bHand) - (ears + fromEars)).sqrMagnitude * 12f * contact;
                // Otherwise keep the fist out of the (big, stylised) head: a hand beside the head in
                // the video must not end up in our hero's hair.
                // Measured in the picture plane (the lens looks along Z): what matters is that the
                // hand does not cover or sink into the head as the viewer sees it.
                // The fist, not the wrist (the knuckles sit a hand's length past it), against the
                // rig's own skull: its head is bigger than the video's, so the video's ears are no
                // guide to where it is.
                var skull = rig.P(HumanBodyBones.Head) + rig.R(HumanBodyBones.Head) * headUpLocal * SkullAboveHeadBone;
                var offset = rig.P(bMiddle) - skull;
                offset.z = 0f;
                float clearance = HeadClearance - offset.magnitude;
                if (clearance > 0f) c += clearance * clearance * 150f * (1f - contact);
                if (tracked)
                    // In a fist the knuckle line is noisy; the thumb is what fixes the hand's roll.
                    c += Miss(rig.P(bMiddle) - rig.P(bHand), handFwd) * .5f
                         + Miss(rig.P(bIndex) - rig.P(bLittle), knuckles) * .2f
                         + Miss(rig.P(bThumbEnd) - rig.P(bThumbMid), thumb) * .4f;
                return c + (m[arm[0]] * m[arm[0]] + m[arm[1]] * m[arm[1]]) * .01f
                         + (m[arm[7]] * m[arm[7]] + m[arm[8]] * m[arm[8]]) * .005f;
            });
            rig.Apply(m);
            return (Vector3.Angle(rig.P(bLower) - rig.P(bUpper), upper) + Vector3.Angle(rig.P(bHand) - rig.P(bLower), fore))
                   * .5f * Mathf.Deg2Rad;
        }

        /// <summary>Fingers, from two measures. The segment directions pose a finger well when it is
        /// seen side-on (a pointing index, an open wave) but a fist pointed at the lens foreshortens
        /// them into noise and the solve leaves it half open. So a clearly closed finger — its tip
        /// hardly further from the wrist than its knuckle — is closed outright, and only the
        /// in-between cases keep the direction solve. The thumb, which carries the gesture (up,
        /// down, the foot of the "L"), is always solved on its directions.</summary>
        private static void SolveFingers(Rig rig, float[] m, Track t, int f, Side side, float step)
        {
            var hand = side == Side.Right ? t.right : t.left;
            if (!HasHand(hand, t)) return;
            string p = side == Side.Right ? "Right " : "Left ";
            string b = side == Side.Right ? "Right" : "Left";
            var wrist = Point(hand, f, 21, 0);
            int[] bases = { 5, 9, 13, 17 };
            string[] names = { "Index", "Middle", "Ring", "Little" };
            for (int i = 0; i < 4; i++)
            {
                int k = bases[i];
                var bProx = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), b + names[i] + "Proximal");
                var bInter = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), b + names[i] + "Intermediate");
                var bDist = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), b + names[i] + "Distal");
                var seg1 = (Point(hand, f, 21, k + 1) - Point(hand, f, 21, k)).normalized;
                var seg2 = (Point(hand, f, 21, k + 2) - Point(hand, f, 21, k + 1)).normalized;
                var free = Muscles(p + names[i] + " 1 Stretched", p + names[i] + " 2 Stretched", p + names[i] + " Spread");
                int third = Rig.Muscle(p + names[i] + " 3 Stretched");
                Descend(rig, m, free, step, () =>
                {
                    m[third] = m[free[1]];
                    return Miss(rig.P(bInter) - rig.P(bProx), seg1) + Miss(rig.P(bDist) - rig.P(bInter), seg2)
                           + m[free[2]] * m[free[2]] * .02f;
                });
                m[third] = m[free[1]];

                float reach = (Point(hand, f, 21, k + 3) - wrist).magnitude
                              / Mathf.Max(1e-4f, (Point(hand, f, 21, k) - wrist).magnitude);
                // The index may point at the lens (its reach then collapses to ~1.0 while it is
                // straight), so only a clearly folded index is closed. The other three fingers
                // are folded or open in every gesture here; closing them is the safe call.
                float closed = i == 0
                    ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1.0f - reach) / .2f))
                    : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1.5f - reach) / .3f));
                if (closed <= 0f) continue;
                for (int j = 1; j <= 3; j++)
                {
                    int mu = Rig.Muscle(p + names[i] + " " + j + " Stretched");
                    m[mu] = Mathf.Lerp(m[mu], -rig.OpenFinger, closed);
                }
                m[free[2]] = Mathf.Lerp(m[free[2]], 0f, closed);
            }

            var tProx = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), b + "ThumbProximal");
            var tInter = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), b + "ThumbIntermediate");
            var tDist = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), b + "ThumbDistal");
            var tSeg1 = (Point(hand, f, 21, 2) - Point(hand, f, 21, 1)).normalized;
            var tSeg2 = (Point(hand, f, 21, 3) - Point(hand, f, 21, 2)).normalized;
            var tFree = Muscles(p + "Thumb 1 Stretched", p + "Thumb 2 Stretched", p + "Thumb Spread");
            int tThird = Rig.Muscle(p + "Thumb 3 Stretched");
            Descend(rig, m, tFree, step, () =>
            {
                m[tThird] = m[tFree[1]];
                return Miss(rig.P(tInter) - rig.P(tProx), tSeg1) + Miss(rig.P(tDist) - rig.P(tInter), tSeg2)
                       + m[tFree[2]] * m[tFree[2]] * .02f;
            });
            m[tThird] = m[tFree[1]];
        }

        /// <summary>A rotation limited per axis (pitch about X, yaw about Y, roll about Z), degrees.</summary>
        private static Quaternion Clamp(Quaternion q, float pitch, float yaw, float roll)
        {
            var e = q.eulerAngles;
            float Signed(float a) => a > 180f ? a - 360f : a;
            return Quaternion.Euler(Mathf.Clamp(Signed(e.x), -pitch, pitch), Mathf.Clamp(Signed(e.y), -yaw, yaw),
                Mathf.Clamp(Signed(e.z), -roll, roll));
        }

        private static float Miss(Vector3 a, Vector3 target) => 1f - Vector3.Dot(a.normalized, target);

        private static float Rad(Quaternion a, Quaternion b) => Quaternion.Angle(a, b) * Mathf.Deg2Rad;

        private static float Reg(float[] m, int[] set, float w)
        {
            float s = 0f;
            foreach (int i in set) s += m[i] * m[i];
            return s * w;
        }

        /// <summary>Coordinate descent on <paramref name="free"/>, applying the pose before each
        /// cost read. The cost closure reads the rig.</summary>
        private static void Descend(Rig rig, float[] m, int[] free, float start, Func<float> cost)
        {
            rig.Apply(m);
            float best = cost();
            for (float step = start; step > .003f; step *= .5f)
            {
                bool improved = true;
                for (int pass = 0; improved && pass < 12; pass++)
                {
                    improved = false;
                    foreach (int i in free)
                        foreach (float sign in new[] { 1f, -1f })
                        {
                            float old = m[i];
                            m[i] = Mathf.Clamp(old + sign * step, -1f, 1f);
                            rig.Apply(m);
                            float c = cost();
                            if (c < best - 1e-7f) { best = c; improved = true; }
                            else m[i] = old;
                        }
                }
            }
            rig.Apply(m);
        }

        /// <summary>A light 5-tap binomial smooth over time: the solve is warm-started and the
        /// tracks are filtered, so this only takes the last solver quantisation out.</summary>
        private static void Smooth(List<float[]> frames, int[] muscles)
        {
            if (frames.Count < 5) return;
            float[] w = { 1f, 4f, 6f, 4f, 1f };
            foreach (int mu in muscles)
            {
                var src = frames.Select(fr => fr[mu]).ToArray();
                for (int i = 0; i < frames.Count; i++)
                {
                    float s = 0f, ws = 0f;
                    for (int k = -2; k <= 2; k++)
                    {
                        int j = Mathf.Clamp(i + k, 0, frames.Count - 1);
                        s += src[j] * w[k + 2]; ws += w[k + 2];
                    }
                    frames[i][mu] = s / ws;
                }
            }
        }

        private static int[] Muscles(params string[] names) => names.Select(Rig.Muscle).ToArray();

        // ── Clip writing ────────────────────────────────────────────────────────────────────

        private static void Save(string path, List<Key> keys, Rig rig, AnimationClip idle)
        {
            float duration = keys[keys.Count - 1].Time;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool created = clip == null;
            if (created) clip = new AnimationClip();
            clip.ClearCurves();
            clip.frameRate = 30f;

            var owned = new HashSet<int>(keys.SelectMany(k => k.Muscles.Keys));
            var idleBindings = AnimationUtility.GetCurveBindings(idle);
            var ownedNames = new HashSet<string>(owned.Select(ClipName));
            foreach (var binding in idleBindings)
            {
                if (ownedNames.Contains(binding.propertyName)) continue;
                bool rootT = binding.propertyName.StartsWith("RootT."), rootQ = binding.propertyName.StartsWith("RootQ.");
                if ((rootT || rootQ) && keys.All(k => k.RootT.HasValue))
                {
                    int axis = binding.propertyName[6] == 'w' ? 3 : binding.propertyName[6] - 'x';
                    // Keep quaternion signs continuous so the curves never flip through zero.
                    var q = keys.Select(k => k.RootQ).ToArray();
                    for (int i = 1; i < q.Length; i++)
                        if (Quaternion.Dot(q[i], q[i - 1]) < 0f) q[i] = new Quaternion(-q[i].x, -q[i].y, -q[i].z, -q[i].w);
                    var root = new AnimationCurve(keys.Select((k, i) => new Keyframe(k.Time,
                        rootT ? k.RootT.Value[axis] : q[i][axis])).ToArray());
                    for (int i = 0; i < root.length; i++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(root, i, AnimationUtility.TangentMode.ClampedAuto);
                        AnimationUtility.SetKeyRightTangentMode(root, i, AnimationUtility.TangentMode.ClampedAuto);
                    }
                    AnimationUtility.SetEditorCurve(clip, binding, root);
                    continue;
                }
                // Hand IK goals describe the idle's hands; they would contradict a raised arm.
                if (binding.propertyName.StartsWith("LeftHand") || binding.propertyName.StartsWith("RightHand"))
                    if (binding.propertyName.Contains("T.") || binding.propertyName.Contains("Q.")) continue;
                var source = AnimationUtility.GetEditorCurve(idle, binding);
                var curve = new AnimationCurve();
                for (float t = 0f; t <= duration + .001f; t += 1f / 15f)
                    curve.AddKey(t, source.Evaluate(Mathf.Repeat(t, idle.length)));
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            foreach (int m in owned)
            {
                var source = idleBindings.FirstOrDefault(b => b.propertyName == ClipName(m));
                var idleCurve = source.propertyName != null ? AnimationUtility.GetEditorCurve(idle, source) : null;
                float IdleAt(float t) => idleCurve != null ? idleCurve.Evaluate(Mathf.Repeat(t, idle.length)) : rig.Base[m];
                var curve = new AnimationCurve();
                foreach (var key in keys)
                    curve.AddKey(key.Time, key.Muscles.TryGetValue(m, out float v) ? v : IdleAt(key.Time));
                for (int i = 0; i < curve.length; i++)
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                for (int i = 0; i < curve.length; i++)
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), ClipName(m)), curve);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            settings.loopBlendOrientation = settings.loopBlendPositionY = settings.loopBlendPositionXZ = true;
            settings.keepOriginalOrientation = settings.keepOriginalPositionY = settings.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (created) AssetDatabase.CreateAsset(clip, path);
            else EditorUtility.SetDirty(clip);
        }

        /// <summary>HumanTrait names fingers "Right Index 1 Stretched"; clips bind them as
        /// "RightHand.Index.1 Stretched".</summary>
        private static string ClipName(int muscle)
        {
            string name = HumanTrait.MuscleName[muscle];
            var m = Regex.Match(name, @"^(Left|Right) (Thumb|Index|Middle|Ring|Little) (.+)$");
            return m.Success ? $"{m.Groups[1].Value}Hand.{m.Groups[2].Value}.{m.Groups[3].Value}" : name;
        }

        /// <summary>The man's rig in the idle's first frame, posed through HumanPoseHandler.</summary>
        private sealed class Rig : IDisposable
        {
            private readonly GameObject _go;
            private readonly Animator _animator;
            private readonly HumanPoseHandler _handler;
            private HumanPose _pose;
            public readonly float[] Base;
            /// <summary>The "Stretched" sign that opens a finger on this rig — measured, not assumed.</summary>
            public readonly float OpenFinger;

            public Rig(GameObject prefab, AnimationClip idle)
            {
                _go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                _go.hideFlags = HideFlags.HideAndDontSave;
                _go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                _animator = _go.GetComponentInChildren<Animator>();
                idle.SampleAnimation(_animator.gameObject, 0f);
                _handler = new HumanPoseHandler(_animator.avatar, _animator.transform);
                _handler.GetHumanPose(ref _pose);
                Base = (float[])_pose.muscles.Clone();
                OpenFinger = FingerReach(1f) > FingerReach(-1f) ? 1f : -1f;
                Apply(Base);
            }

            private float FingerReach(float value)
            {
                var m = (float[])Base.Clone();
                for (int j = 1; j <= 3; j++) m[Muscle("Right Middle " + j + " Stretched")] = value;
                Apply(m);
                return (P(HumanBodyBones.RightMiddleDistal) - P(HumanBodyBones.RightHand)).magnitude;
            }

            public static int Muscle(string name)
            {
                int i = Array.IndexOf(HumanTrait.MuscleName, name);
                if (i < 0) throw new ArgumentException("No humanoid muscle " + name);
                return i;
            }

            public float HumanScale => _animator.humanScale;

            /// <summary>The idle's full pose at a moment (muscles and body placement); the rig is
            /// left standing in it.</summary>
            public HumanPose SampleIdle(AnimationClip idle, float time)
            {
                idle.SampleAnimation(_animator.gameObject, time);
                var pose = new HumanPose();
                _handler.GetHumanPose(ref pose);
                return pose;
            }

            public void SetPose(HumanPose pose) => _handler.SetHumanPose(ref pose);

            public void Apply(float[] muscles)
            {
                Array.Copy(muscles, _pose.muscles, muscles.Length);
                _handler.SetHumanPose(ref _pose);
            }

            public Vector3 P(HumanBodyBones bone)
            {
                var t = _animator.GetBoneTransform(bone);
                return t != null ? t.position : Vector3.zero;
            }

            /// <summary>World rotation of a bone, or of the fallback when the rig lacks it.</summary>
            public Quaternion R(HumanBodyBones bone, HumanBodyBones fallback = HumanBodyBones.Hips)
            {
                var t = _animator.GetBoneTransform(bone) ?? _animator.GetBoneTransform(fallback);
                return t != null ? t.rotation : Quaternion.identity;
            }

            public void Dispose()
            {
                _handler.Dispose();
                Object.DestroyImmediate(_go);
            }
        }
    }
}

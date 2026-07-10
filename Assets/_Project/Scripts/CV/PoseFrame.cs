using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// The 33 BlazePose / MediaPipe Pose landmarks, in their canonical index order. Any pose
    /// backend (MediaPipe Homuler, Sentis BlazePose, a mock) maps its output into this order so the
    /// rest of the CV pipeline (<see cref="PushupRepCounter"/>, <see cref="FormScoreCalculator"/>)
    /// is backend-agnostic.
    /// </summary>
    public enum PoseLandmark
    {
        Nose = 0,
        LeftEyeInner = 1, LeftEye = 2, LeftEyeOuter = 3,
        RightEyeInner = 4, RightEye = 5, RightEyeOuter = 6,
        LeftEar = 7, RightEar = 8,
        MouthLeft = 9, MouthRight = 10,
        LeftShoulder = 11, RightShoulder = 12,
        LeftElbow = 13, RightElbow = 14,
        LeftWrist = 15, RightWrist = 16,
        LeftPinky = 17, RightPinky = 18,
        LeftIndex = 19, RightIndex = 20,
        LeftThumb = 21, RightThumb = 22,
        LeftHip = 23, RightHip = 24,
        LeftKnee = 25, RightKnee = 26,
        LeftAnkle = 27, RightAnkle = 28,
        LeftHeel = 29, RightHeel = 30,
        LeftFootIndex = 31, RightFootIndex = 32,
    }

    /// <summary>One landmark: normalized image coords (x,y ∈ [0,1]), relative depth z, and the
    /// detector's visibility/confidence ∈ [0,1].</summary>
    public readonly struct Landmark
    {
        public readonly float X, Y, Z, Visibility;

        public Landmark(float x, float y, float z, float visibility)
        {
            X = x; Y = y; Z = z; Visibility = visibility;
        }

        public Vector2 Pos2D => new Vector2(X, Y);
    }

    /// <summary>
    /// A single tracked frame: the 33 landmarks plus a capture timestamp. Immutable snapshot passed
    /// from an <see cref="IPoseSource"/> to the consumers. The landmark array is always length 33
    /// (<see cref="PoseLandmarks.Count"/>); missing joints carry Visibility ≈ 0.
    ///
    /// <para><b>Coordinate spaces.</b> <see cref="Landmarks"/> are normalized image coordinates
    /// (x,y ∈ [0,1], origin TOP-LEFT, Y points down). <see cref="WorldLandmarks"/> (optional) are
    /// the MediaPipe "world" landmarks in <b>meters</b>, <b>hip-centered</b> (origin = midpoint
    /// between LeftHip/RightHip). <b>Axes match the image axes</b> — +X = image right, +Y = image
    /// down, +Z = away from the camera — so world.Y is still camera-aligned, NOT gravity-aligned.
    /// What world coords buy us vs. image coords: (1) metric scale so anti-cheat thresholds can be
    /// expressed in meters instead of "fraction of body length"; (2) hip-centering so camera zoom
    /// no longer biases distances; (3) consistent units across frames. "Down toward the floor" must
    /// still be derived from the pose itself (e.g. the torso normal n_torso = cross(shoulderMid -
    /// hipMid, leftShoulder - rightShoulder), which points out of the chest and — for a person in
    /// plank — toward the floor). Visibility on world landmarks is populated from the image
    /// landmarks (per-keypoint visibility is shared by BlazePose).</para>
    /// </summary>
    public readonly struct PoseFrame
    {
        public readonly Landmark[] Landmarks;

        /// <summary>Optional world-space landmarks (meters, hip-centered, body frame). Null when the
        /// backend does not provide them — consumers must check <see cref="HasWorldLandmarks"/>
        /// before calling <see cref="GetWorld"/>.</summary>
        public readonly Landmark[] WorldLandmarks;

        public readonly float TimestampSec;

        /// <summary>Source image aspect ratio (width / height). Normalized landmark x,y are
        /// ANISOTROPIC — x is a fraction of image width, y of image height — so raw euclidean
        /// distances mix units. All new (phase 08.1 frontal) metrics convert to the aspect-corrected
        /// "square" space via <see cref="PoseMath.ToSquare"/> using this value. 1.0 when the source
        /// doesn't report it (mocks — their synthetic geometry is authored square).</summary>
        public readonly float Aspect;

        public PoseFrame(Landmark[] landmarks, float timestampSec)
            : this(landmarks, null, timestampSec, 1f) { }

        public PoseFrame(Landmark[] landmarks, Landmark[] worldLandmarks, float timestampSec)
            : this(landmarks, worldLandmarks, timestampSec, 1f) { }

        public PoseFrame(Landmark[] landmarks, Landmark[] worldLandmarks, float timestampSec, float aspect)
        {
            Landmarks = landmarks;
            WorldLandmarks = worldLandmarks;
            TimestampSec = timestampSec;
            Aspect = aspect > 1e-3f ? aspect : 1f;
        }

        public bool IsValid => Landmarks != null && Landmarks.Length == PoseLandmarks.Count;

        /// <summary>True iff the backend filled in <see cref="WorldLandmarks"/> for this frame.
        /// Anti-cheat signals S9/S10 require this; S1/S5/S6 use world coords when available and fall
        /// back to image-space otherwise.</summary>
        public bool HasWorldLandmarks
            => WorldLandmarks != null && WorldLandmarks.Length == PoseLandmarks.Count;

        public Landmark Get(PoseLandmark id) => Landmarks[(int)id];

        /// <summary>World-space landmark (meters, hip-centered, body frame). Caller must guard with
        /// <see cref="HasWorldLandmarks"/>; this method indexes blindly.</summary>
        public Landmark GetWorld(PoseLandmark id) => WorldLandmarks[(int)id];

        public float Visibility(PoseLandmark id) => Landmarks[(int)id].Visibility;
    }

    public static class PoseLandmarks
    {
        public const int Count = 33;
    }
}

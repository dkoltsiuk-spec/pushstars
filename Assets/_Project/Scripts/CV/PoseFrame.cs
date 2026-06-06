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
    /// </summary>
    public readonly struct PoseFrame
    {
        public readonly Landmark[] Landmarks;
        public readonly float TimestampSec;

        public PoseFrame(Landmark[] landmarks, float timestampSec)
        {
            Landmarks = landmarks;
            TimestampSec = timestampSec;
        }

        public bool IsValid => Landmarks != null && Landmarks.Length == PoseLandmarks.Count;

        public Landmark Get(PoseLandmark id) => Landmarks[(int)id];

        public float Visibility(PoseLandmark id) => Landmarks[(int)id].Visibility;
    }

    public static class PoseLandmarks
    {
        public const int Count = 33;
    }
}

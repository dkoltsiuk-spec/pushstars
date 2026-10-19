using PushStars.Core;

namespace PushStars.Fight
{
    /// <summary>
    /// Where the opponent's body comes from: a Humanoid timeline sampled at <see cref="Elapsed"/>
    /// on <see cref="MotionPhase"/>, or — for recordings without poses — a push-up depth to scrub
    /// the canned clip by. A stored ghost (<see cref="GhostOpponent"/>) and a friend streaming
    /// live (<see cref="LiveOpponent"/>) both answer it, so one avatar driver poses either.
    /// </summary>
    public interface IOpponentPose
    {
        GhostMotionClip Motion { get; }
        byte MotionPhase { get; }
        float Elapsed { get; }
        bool PlaybackActive { get; }
        bool HasPose { get; }
        bool IsWorking { get; }
        float Depth01 { get; }
    }
}

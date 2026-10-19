using UnityEngine;

namespace PushStars.Core
{
    /// <summary>
    /// The opponent's streamed poses as a short rolling <see cref="GhostMotionClip"/>, so the
    /// ghost playback that already poses a recorded body poses a live one unchanged: it samples
    /// <see cref="Clip"/> at <see cref="PlaybackTime"/> on <see cref="Phase"/>.
    ///
    /// <para>Playback runs a fixed <see cref="Delay"/> behind the sender's clock. The offset
    /// between the two clocks is the fastest arrival seen (the frame that waited least in the
    /// network), decaying slowly so a congested patch cannot freeze the body for good. Late and
    /// duplicated packets are dropped: the stream is unreliable on purpose.</para>
    /// </summary>
    public sealed class LivePoseBuffer
    {
        public const float Delay = .2f;
        private const float KeepSec = 2f, OffsetDecayPerSec = .05f;
        public readonly GhostMotionClip Clip = new GhostMotionClip();
        public byte Phase { get; private set; } = GhostMotionClip.Setup;
        public float PlaybackTime { get; private set; }
        public bool HasPose => Clip.Frames.Count > 0;
        public int Received { get; private set; }
        public int Dropped { get; private set; }
        private float _offset, _offsetAt;
        private bool _hasOffset;

        public void Reset()
        {
            Clip.Frames.Clear(); Phase = GhostMotionClip.Setup; PlaybackTime = 0;
            _hasOffset = false; Received = Dropped = 0;
        }

        public void Add(GhostMotionClip.Frame frame, float now)
        {
            var frames = Clip.Frames;
            if (frames.Count > 0)
            {
                var last = frames[frames.Count - 1];
                if (frame.phase < last.phase || (frame.phase == last.phase && frame.time <= last.time)) { Dropped++; return; }
                if (frame.phase > last.phase) { frames.Clear(); _hasOffset = false; }
            }
            Received++;
            frames.Add(frame);
            if (frame.phase != Phase) { Phase = frame.phase; PlaybackTime = frame.time; }
            float sample = frame.time - now;
            float decayed = _offset - (now - _offsetAt) * OffsetDecayPerSec;
            if (!_hasOffset || sample > decayed) { _offset = sample; _offsetAt = now; _hasOffset = true; }
            else if (sample < decayed - 1f) { _offset = sample; _offsetAt = now; }   // the sender's clock jumped back
            Prune();
        }

        /// <summary>Moves the playhead to where the sender was <see cref="Delay"/> ago. Never
        /// backwards, never past the newest frame.</summary>
        public void Advance(float now)
        {
            if (!_hasOffset || Clip.Frames.Count == 0) return;
            float decayed = _offset - (now - _offsetAt) * OffsetDecayPerSec;
            float target = now + decayed - Delay;
            var frames = Clip.Frames;
            float first = frames[0].time, newest = frames[frames.Count - 1].time;
            PlaybackTime = Mathf.Clamp(Mathf.Max(PlaybackTime, target), first, newest);
        }

        private void Prune()
        {
            var frames = Clip.Frames;
            int drop = 0;
            while (drop < frames.Count - 2 && frames[drop + 1].time < PlaybackTime - KeepSec) drop++;
            if (drop > 0) frames.RemoveRange(0, drop);
        }
    }
}

using UnityEngine;
using PushStars.CV.Util;

namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// Per-frame sliding-window detector for wrist drift. Catches "airborne" push-ups (arms waving
    /// without support — lying on back, sitting, standing) by reporting whether the user's wrists
    /// stay anchored at a fixed position frame-to-frame.
    ///
    /// <para><b>Stage 1: image-space implementation.</b> Uses normalized image landmarks; drift is
    /// the per-component RMS deviation from the mean wrist position over the last
    /// <see cref="CVConstants.WristAnchorWindowFrames"/> frames, normalized by torso scale
    /// (|midShoulder − midHip| in image coords) so the threshold is body-relative and zoom-invariant.
    /// World-coords path can be added later — both axes share the same threshold semantics since
    /// drift is a fraction, not an absolute distance.</para>
    ///
    /// <para><b>Grace periods.</b> The window is wiped (and the verdict held at
    /// <see cref="AnchorVerdict.Unknown"/>) for some frames after a credited rep and after the FSM
    /// re-arms, so a user adjusting hand position between reps does not get flagged.</para>
    /// </summary>
    public sealed class WristAnchorMonitor
    {
        private readonly RingBuffer<Vector2> _leftBuf;
        private readonly RingBuffer<Vector2> _rightBuf;
        private int _graceFramesRemaining;

        /// <summary>Verdict for the latest <see cref="Tick"/>. <see cref="AnchorVerdict.Unknown"/>
        /// during grace or when the window hasn't filled — consumers must NOT treat this as airborne.</summary>
        public AnchorVerdict LastVerdict { get; private set; } = AnchorVerdict.Unknown;

        public float LastLeftDriftFrac  { get; private set; }
        public float LastRightDriftFrac { get; private set; }

        public WristAnchorMonitor()
        {
            _leftBuf  = new RingBuffer<Vector2>(CVConstants.WristAnchorWindowFrames);
            _rightBuf = new RingBuffer<Vector2>(CVConstants.WristAnchorWindowFrames);
        }

        /// <summary>Wipe the sliding window and force <see cref="AnchorVerdict.Unknown"/> for
        /// <paramref name="graceFrames"/> ticks. Called after a credited rep, after the armer
        /// transitions to Armed, and on tracking loss.</summary>
        public void Reset(int graceFrames = 0)
        {
            _leftBuf.Clear();
            _rightBuf.Clear();
            _graceFramesRemaining = Mathf.Max(0, graceFrames);
            LastVerdict = AnchorVerdict.Unknown;
            LastLeftDriftFrac = LastRightDriftFrac = 0f;
        }

        public void Tick(in PoseFrame frame)
        {
            if (!frame.IsValid)
            {
                LastVerdict = AnchorVerdict.Unknown;
                return;
            }
            if (_graceFramesRemaining > 0)
            {
                _graceFramesRemaining--;
                LastVerdict = AnchorVerdict.Unknown;
                return;
            }

            if (!TryTorsoScale(frame, out float bodyScale) || bodyScale < 1e-3f)
            {
                LastVerdict = AnchorVerdict.Unknown;
                return;
            }

            bool leftOk  = frame.Visibility(PoseLandmark.LeftWrist)  >= CVConstants.MinJointVisibility;
            bool rightOk = frame.Visibility(PoseLandmark.RightWrist) >= CVConstants.MinJointVisibility;

            if (leftOk)  _leftBuf.Push(frame.Get(PoseLandmark.LeftWrist).Pos2D);
            if (rightOk) _rightBuf.Push(frame.Get(PoseLandmark.RightWrist).Pos2D);

            float leftDrift  = leftOk  && _leftBuf.IsFull  ? DriftFrac(_leftBuf,  bodyScale) : float.NaN;
            float rightDrift = rightOk && _rightBuf.IsFull ? DriftFrac(_rightBuf, bodyScale) : float.NaN;

            LastLeftDriftFrac  = float.IsNaN(leftDrift)  ? 0f : leftDrift;
            LastRightDriftFrac = float.IsNaN(rightDrift) ? 0f : rightDrift;

            // Need at least one wrist with a fully-warmed window to render a verdict.
            bool leftReady  = !float.IsNaN(leftDrift);
            bool rightReady = !float.IsNaN(rightDrift);
            if (!leftReady && !rightReady)
            {
                LastVerdict = AnchorVerdict.Unknown;
                return;
            }

            // Attack the worse wrist (max drift). The defining cheat is "one arm anchored, the
            // other waving" — averaging would hide it.
            float worst = Mathf.Max(leftReady ? leftDrift : 0f, rightReady ? rightDrift : 0f);
            LastVerdict = ClassifyDrift(worst);
        }

        static bool TryTorsoScale(in PoseFrame f, out float scale)
        {
            scale = 0f;
            bool ls = f.Visibility(PoseLandmark.LeftShoulder)  >= CVConstants.MinJointVisibility;
            bool rs = f.Visibility(PoseLandmark.RightShoulder) >= CVConstants.MinJointVisibility;
            bool lh = f.Visibility(PoseLandmark.LeftHip)       >= CVConstants.MinJointVisibility;
            bool rh = f.Visibility(PoseLandmark.RightHip)      >= CVConstants.MinJointVisibility;
            if (!(ls || rs) || !(lh || rh)) return false;

            Vector2 shoulder = (ls && rs)
                ? (f.Get(PoseLandmark.LeftShoulder).Pos2D + f.Get(PoseLandmark.RightShoulder).Pos2D) * 0.5f
                : (ls ? f.Get(PoseLandmark.LeftShoulder).Pos2D : f.Get(PoseLandmark.RightShoulder).Pos2D);
            Vector2 hip = (lh && rh)
                ? (f.Get(PoseLandmark.LeftHip).Pos2D + f.Get(PoseLandmark.RightHip).Pos2D) * 0.5f
                : (lh ? f.Get(PoseLandmark.LeftHip).Pos2D : f.Get(PoseLandmark.RightHip).Pos2D);
            scale = Vector2.Distance(shoulder, hip);
            return scale > 0f;
        }

        // RMS displacement from the per-component mean, normalized by torso scale. Mean (vs median)
        // is fine here — for a planted wrist the samples cluster tightly; for an airborne arm they
        // sweep through a range and either statistic blows up.
        static float DriftFrac(RingBuffer<Vector2> buf, float bodyScale)
        {
            int n = buf.Count;
            if (n < 2) return 0f;

            Vector2 sum = Vector2.zero;
            for (int i = 0; i < n; i++) sum += buf[i];
            Vector2 mean = sum / n;

            float sumSq = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 d = buf[i] - mean;
                sumSq += d.sqrMagnitude;
            }
            float rms = Mathf.Sqrt(sumSq / n);
            return rms / bodyScale;
        }

        static AnchorVerdict ClassifyDrift(float frac)
        {
            if (frac < CVConstants.WristAnchorSoftFrac) return AnchorVerdict.Anchored;
            if (frac < CVConstants.WristAnchorHardFrac) return AnchorVerdict.Drifting;
            return AnchorVerdict.Airborne;
        }
    }
}

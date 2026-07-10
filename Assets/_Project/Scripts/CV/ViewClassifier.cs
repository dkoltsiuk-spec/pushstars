using UnityEngine;
using PushStars.CV.Util;

namespace PushStars.CV
{
    /// <summary>Which way the user's body is oriented relative to the camera. Drives view-adaptive
    /// branches in PlankArmer / FullRomGate / HipDecouplingGate (see the consumer table in
    /// docs/plan/phase-08.1-frontal-addendum.md).</summary>
    public enum ViewKind
    {
        /// <summary>No stable classification yet (session start, tracking lost > 1s).</summary>
        Unknown = 0,
        /// <summary>Camera in front of the user (the product's primary setup).</summary>
        Frontal = 1,
        /// <summary>Camera to the side (legacy phase-08 geometry; side mocks).</summary>
        Side = 2,
        /// <summary>Diagonal / in the hysteresis gap — consumers use their safe default branch.</summary>
        Ambiguous = 3,
    }

    /// <summary>
    /// Classifies camera view from pose geometry. Metric: R = torsoLen / shoulderWidth in the
    /// aspect-corrected square space. Side view: torso projects long, shoulders overlap → R ≈ 2–5.
    /// Frontal: shoulders wide, torso foreshortened to almost nothing → R ≈ 0.05–0.7.
    ///
    /// <para>Robustness rules (from the adversarial review):</para>
    /// <list type="bullet">
    /// <item>A frame "votes" only when both shoulders are visible (≥0.5) and at least ONE hip
    /// clears the LOWERED threshold 0.35 — frontal hip visibility routinely sits at 0.4–0.75, and
    /// requiring 0.5 on both hips starved the classifier into permanent Unknown.</item>
    /// <item>Non-voting frames do NOT reset statistics — they just don't advance them.</item>
    /// <item>R is median-filtered over 9 voting frames; state switches only when 20 of the last 30
    /// voting frames agree AND the switch is allowed by the caller (not mid-rep) — the view must
    /// never flip in the middle of a rep and swap signal semantics under the auditor.</item>
    /// </list>
    /// </summary>
    public sealed class ViewClassifier
    {
        private readonly RingBuffer<float> _rWindow = new RingBuffer<float>(CVConstants.ViewMedianWindowFrames);
        private readonly RingBuffer<ViewKind> _votes = new RingBuffer<ViewKind>(CVConstants.ViewSwitchWindow);
        private readonly float[] _medianScratch = new float[CVConstants.ViewMedianWindowFrames];

        private float _lastVoteTimeSec = -1f;
        private float _lastAnyTickTimeSec = -1f;

        public ViewKind View { get; private set; } = ViewKind.Unknown;

        /// <summary>Median R over the voting window — for the HUD readout.</summary>
        public float RMedian { get; private set; }

        /// <summary>How many of the last <see cref="CVConstants.ViewSwitchWindow"/> voting frames
        /// agree with the current pending raw class (HUD/debug).</summary>
        public int PendingVotes { get; private set; }

        public void Reset()
        {
            _rWindow.Clear();
            _votes.Clear();
            View = ViewKind.Unknown;
            RMedian = 0f;
            PendingVotes = 0;
            _lastVoteTimeSec = -1f;
            _lastAnyTickTimeSec = -1f;
        }

        /// <summary>Advance the classifier. <paramref name="canSwitch"/> must be false mid-rep
        /// (counter descending/bottom) so the view never changes under an in-flight rep window.</summary>
        public void Tick(in PoseFrame frame, bool trackingOk, bool canSwitch, float nowSec)
        {
            _lastAnyTickTimeSec = nowSec;

            // Tracking lost for over a second → back to Unknown (the user may have walked around
            // the camera; don't trust the stale classification).
            if (!trackingOk || !frame.IsValid)
            {
                if (_lastVoteTimeSec >= 0f && nowSec - _lastVoteTimeSec > 1f)
                {
                    View = ViewKind.Unknown;
                    _votes.Clear();
                }
                return;
            }

            bool ls = frame.Visibility(PoseLandmark.LeftShoulder)  >= CVConstants.MinJointVisibility;
            bool rs = frame.Visibility(PoseLandmark.RightShoulder) >= CVConstants.MinJointVisibility;
            bool lh = frame.Visibility(PoseLandmark.LeftHip)  >= CVConstants.ViewHipVoteVisibility;
            bool rh = frame.Visibility(PoseLandmark.RightHip) >= CVConstants.ViewHipVoteVisibility;
            if (!ls || !rs || (!lh && !rh)) return; // non-voting frame — hold, don't reset

            float aspect = frame.Aspect;
            Vector2 lsp = PoseMath.ToSquare(frame.Get(PoseLandmark.LeftShoulder).Pos2D, aspect);
            Vector2 rsp = PoseMath.ToSquare(frame.Get(PoseLandmark.RightShoulder).Pos2D, aspect);
            Vector2 shoulderMid = (lsp + rsp) * 0.5f;

            Vector2 hipMid;
            if (lh && rh)
                hipMid = (PoseMath.ToSquare(frame.Get(PoseLandmark.LeftHip).Pos2D, aspect)
                        + PoseMath.ToSquare(frame.Get(PoseLandmark.RightHip).Pos2D, aspect)) * 0.5f;
            else
                hipMid = PoseMath.ToSquare(frame.Get(lh ? PoseLandmark.LeftHip : PoseLandmark.RightHip).Pos2D, aspect);

            float shoulderWidth = Vector2.Distance(lsp, rsp);
            float torsoLen = Vector2.Distance(shoulderMid, hipMid);
            float r = torsoLen / Mathf.Max(shoulderWidth, 1e-3f);

            _rWindow.Push(r);
            RMedian = Median(_rWindow);
            _lastVoteTimeSec = nowSec;

            ViewKind raw =
                RMedian < CVConstants.ViewFrontalMaxRatio ? ViewKind.Frontal :
                RMedian > CVConstants.ViewSideMinRatio    ? ViewKind.Side :
                ViewKind.Ambiguous;
            _votes.Push(raw);

            if (View == ViewKind.Unknown)
            {
                // Bootstrap: first classification doesn't need the full 20-of-30 — half the median
                // window agreeing is enough to leave Unknown quickly at session start (spec: mock
                // must classify ≤ 1.5s).
                if (_rWindow.IsFull) View = raw;
                PendingVotes = CountVotes(raw);
                return;
            }

            if (raw == View)
            {
                PendingVotes = 0;
                return;
            }

            PendingVotes = CountVotes(raw);
            if (PendingVotes >= CVConstants.ViewSwitchVotes && canSwitch)
                View = raw;
        }

        private int CountVotes(ViewKind kind)
        {
            int n = 0;
            for (int i = 0; i < _votes.Count; i++)
                if (_votes[i] == kind) n++;
            return n;
        }

        private float Median(RingBuffer<float> buf)
        {
            int n = buf.Count;
            for (int i = 0; i < n; i++) _medianScratch[i] = buf[i];
            // Insertion sort — n ≤ 9, zero alloc.
            for (int i = 1; i < n; i++)
            {
                float key = _medianScratch[i];
                int j = i - 1;
                while (j >= 0 && _medianScratch[j] > key) { _medianScratch[j + 1] = _medianScratch[j]; j--; }
                _medianScratch[j + 1] = key;
            }
            return n == 0 ? 0f : _medianScratch[n / 2];
        }
    }
}

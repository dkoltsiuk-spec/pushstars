using System;
using UnityEngine;

namespace PushStars.CV
{
    /// <summary>State of the depth-arc automaton — one full arc (Top → Bottom → Top) is one rep.</summary>
    public enum DepthArcState
    {
        /// <summary>Not started (disarmed / signal lost / session start). Latches never fire here.</summary>
        Idle = 0,
        /// <summary>User confirmed at the top; waiting for the bottom latch.</summary>
        AwaitBottom = 1,
        /// <summary>Bottom latched; waiting for the return-to-top latch (completes the arc).</summary>
        AwaitTop = 2,
    }

    /// <summary>
    /// The precision core of rep detection (phase 08.1 frontal addendum, priority #1): turns the
    /// raw elbow angle into robust top/bottom LATCHES and a normalized depth signal for the HUD
    /// amplitude gauge.
    ///
    /// <para><b>Signal chain:</b> raw θ (gated by PoseMath.TryElbowAngle — the 180° sentinel never
    /// enters) → Hampel clamp → θm → zone latches; θm → One-Euro → θs → HUD / Phase / stats.
    /// The former median-of-3 stage was REMOVED in the fast-rep round: it killed single-sample
    /// SPIKES but equally killed single-sample TROUGHS — and at fast tempo the true bottom IS a
    /// one-sample event ([125, 98, 128] → median 125: the rep's bottom never existed in θm).
    /// Spike immunity is carried by the Hampel clamp alone; a surviving false trough still has to
    /// pass the cycle rate-limit, the ascent floor and the per-rep auditor to become a rep.</para>
    ///
    /// <para><b>Turnaround (undersampled-extreme) channels:</b> when the detector undersamples a
    /// fast rep, no sample may land inside a zone at all. If the signal V-turns close to a zone
    /// edge at high speed (|v̂| ≥ FastTurnaroundMinSpeed), the extreme is latched retroactively —
    /// a slow hoverer near the edge has low speed and never triggers these.</para>
    ///
    /// <para><b>Zones are absolute this release</b> (TopEnter=160 / BottomEnter=95 — the anti-cheat
    /// envelope). The adaptive tightening (median window of accepted reps) is computed but affects
    /// ONLY the HUD bands (<see cref="CVConstants.AdaptiveZonesAffectLatch"/> = false) — the
    /// adversarial review found a ratchet deadlock if adaptive zones can gate latching.</para>
    ///
    /// <para>Pure C#, zero per-frame allocation, unit-testable on synthetic traces.</para>
    /// </summary>
    public sealed class AmplitudeTracker
    {
        // ── per-frame outputs ──
        public float SmoothedElbowDeg { get; private set; } = 180f;  // θs — HUD/Phase/stats
        public float MedianElbowDeg { get; private set; } = 180f;    // θm — latches
        public float CurrentDepth01 { get; private set; }
        public DepthArcState ArcState { get; private set; } = DepthArcState.Idle;
        public bool InTopZone { get; private set; }
        public bool InBottomZone { get; private set; }
        /// <summary>Level: bottom has been latched in the current arc (until the arc completes).</summary>
        public bool BottomLatched { get; private set; }
        /// <summary>One-tick pulses for the counter.</summary>
        public bool BottomLatchedThisTick { get; private set; }
        public bool TopLatchedThisTick { get; private set; }
        /// <summary>Timestamp of the bottom latch in the current arc; −1 when none.</summary>
        public float BottomLatchTimeSec { get; private set; } = -1f;
        /// <summary>False while the input signal is frozen (invalid frames) — HUD paints the marker red.</summary>
        public bool SignalValid { get; private set; }
        /// <summary>HUD hint: deep shoulder travel with θm stuck in 100–125 — the tucked-elbows /
        /// wide-grip pattern channel B would latch; until it's enabled we coach "flare your elbows".</summary>
        public bool BottomAltHintActive { get; private set; }

        // ── zones (latching = absolute; Hud* = adaptive display bands) ──
        public float TopEnterDeg => CVConstants.TopElbowAngle;       // 160 — latch
        public float BottomEnterDeg => CVConstants.BottomElbowAngle; // 95 — latch
        public float HudTopEnterDeg { get; private set; } = CVConstants.TopElbowAngle;
        public float HudBottomEnterDeg { get; private set; } = CVConstants.BottomElbowAngle;

        // ── watermarks (depth01 space) ──
        public float RepMinDepth01 { get; private set; }
        public float RepMaxDepth01 { get; private set; }
        public float LastRepMinDepth01 { get; private set; }
        public float LastRepMaxDepth01 { get; private set; }

        // ── per-arc raw material for audits / channel B ──
        public float ArcShoulderWidthMinImg { get; private set; } = float.PositiveInfinity;
        public float ArcShoulderWidthMaxImg { get; private set; }
        public float ArcShoulderMidYMin { get; private set; } = float.PositiveInfinity;
        public float ArcShoulderMidYMax { get; private set; }

        public event Action OnBottomLatched;   // → 1320Hz bottom tick
        public event Action OnTopLatched;

        // ── internals ──
        private OneEuroFilter _filter = new OneEuroFilter(
            CVConstants.ElbowFilterMinCutoffHz, CVConstants.ElbowFilterBeta, CVConstants.ElbowFilterDerivCutoffHz);

        private float _lastValidTimeSec = -1f;
        private float _prevTickTimeSec = -1f;
        private bool _pendingReseed = true;

        // zone dwell for the timestamp debounce
        private float _bottomZoneEnterTime = -1f;
        private float _topZoneEnterTime = -1f;
        private int _idleTopFrames;

        // grace-latch bookkeeping
        private int _descendStreak;
        private float _prevMedian = 180f;
        private float _lastValidTheta = 180f;
        private bool _anchorOkAtLastValid;
        private bool _inGap;

        // per-arc θm extremes (adaptive window feed) + top-of-arc shoulder reference (channel B)
        private float _arcThetaMin = 180f;
        private float _arcThetaMax;
        private float _lastArcThetaMin = 180f;
        private float _lastArcThetaMax;
        private float _arcTopShoulderY = -1f;

        // turnaround channels (fast undersampled reps)
        private float _arcThetaMinTime = -1f;   // when the descent trough was observed
        private float _crestMax;                // max θm since the bottom latch (AwaitTop)
        private float _maxSpeedThisPhase;       // rolling max |v̂| within the current arc phase

        // adaptive HUD windows (accepted reps only)
        private readonly float[] _botWindow = new float[CVConstants.AdaptiveWindowReps];
        private readonly float[] _topWindow = new float[CVConstants.AdaptiveWindowReps];
        private readonly float[] _adaptScratch = new float[CVConstants.AdaptiveWindowReps];
        private int _adaptCount;
        private int _adaptHead;
        private int _missedAttempts;

        public void Reset()
        {
            _filter.Reset();
            SmoothedElbowDeg = 180f;
            MedianElbowDeg = 180f;
            _prevMedian = 180f;
            CurrentDepth01 = 0f;
            SignalValid = false;
            _lastValidTimeSec = -1f;
            _prevTickTimeSec = -1f;
            _pendingReseed = true;
            _inGap = false;
            _descendStreak = 0;
            ResetArc(toIdle: true);
            LastRepMinDepth01 = LastRepMaxDepth01 = 0f;
            _adaptCount = 0;
            _adaptHead = 0;
            _missedAttempts = 0;
            HudTopEnterDeg = CVConstants.TopElbowAngle;
            HudBottomEnterDeg = CVConstants.BottomElbowAngle;
            BottomAltHintActive = false;
        }

        /// <summary>Advance one frame. <paramref name="hasRawElbow"/>/<paramref name="rawElbowDeg"/>
        /// come from PoseMath.TryElbowAngle computed ONCE per frame by PushupSession (single-source
        /// rule). <paramref name="anchorOk"/> = WristAnchor verdict is Anchored — consumed by the
        /// grace-latch only.</summary>
        public void Tick(in PoseFrame frame, bool trackingOk, bool isArmed, float timeSec,
                         bool hasRawElbow, float rawElbowDeg, bool anchorOk)
        {
            BottomLatchedThisTick = false;
            TopLatchedThisTick = false;

            bool frameValid = trackingOk && frame.IsValid && hasRawElbow;

            if (!isArmed)
            {
                // Signal keeps flowing for HUD/calibration; the arc machine sleeps.
                ResetArc(toIdle: true);
            }

            if (!frameValid)
            {
                SignalValid = false;
                if (!_inGap && _lastValidTimeSec >= 0f)
                {
                    _inGap = true;
                    _lastValidTheta = MedianElbowDeg;
                }
                if (_lastValidTimeSec >= 0f && timeSec - _lastValidTimeSec > CVConstants.TrackerRebaseAfterLostSec)
                {
                    _pendingReseed = true;
                    if (ArcState != DepthArcState.Idle) ResetArc(toIdle: true);
                }
                _prevTickTimeSec = timeSec;
                return;
            }

            float dt = _prevTickTimeSec >= 0f ? timeSec - _prevTickTimeSec : CVConstants.FilterDtClampMinSec;
            _prevTickTimeSec = timeSec;

            if (_pendingReseed)
            {
                _filter.Reset();
                _pendingReseed = false;
            }
            else if (Mathf.Abs(rawElbowDeg - SmoothedElbowDeg) > CVConstants.ElbowSpikeClampDegPerFrame
                     && _filter.IsInitialized)
            {
                // Hampel clamp: single-frame outlier — hold θm/θs, count the frame as valid time
                // (the gap timer must not fire on one clamped frame).
                _lastValidTimeSec = timeSec;
                SignalValid = true;
                return;
            }

            // θm = the Hampel-gated raw signal. No median: single-sample troughs ARE the bottom
            // of a fast rep and must survive (see class doc).
            float thetaM = rawElbowDeg;

            float thetaS = _filter.Filter(thetaM, dt);

            // grace-latch: recover a bottom lost to a short tracking dropout right at the trough
            bool graceLatched = false;
            if (_inGap)
            {
                float gap = timeSec - _lastValidTimeSec;
                if (ArcState == DepthArcState.AwaitBottom
                    && gap <= CVConstants.GraceLatchMaxGapSec
                    && _descendStreak >= 4
                    && _lastValidTheta <= BottomEnterDeg + CVConstants.GraceLatchNearZoneDeg
                    && thetaM > _lastValidTheta
                    && _anchorOkAtLastValid && anchorOk)
                {
                    LatchBottom(_lastValidTimeSec);
                    graceLatched = true;
                }
                _inGap = false;
            }

            // descent monotonicity streak (for the grace-latch precondition)
            if (thetaM < _prevMedian - 0.25f) _descendStreak++;
            else if (thetaM > _prevMedian + 0.25f) _descendStreak = 0;
            _prevMedian = thetaM;

            MedianElbowDeg = thetaM;
            SmoothedElbowDeg = thetaS;
            CurrentDepth01 = Mathf.Clamp01(
                (CVConstants.AmplitudeGaugeTopDeg - thetaS)
                / (CVConstants.AmplitudeGaugeTopDeg - CVConstants.AmplitudeGaugeBottomDeg));
            SignalValid = true;
            _lastValidTimeSec = timeSec;
            _anchorOkAtLastValid = anchorOk;

            InTopZone = thetaM >= TopEnterDeg;
            InBottomZone = thetaM <= BottomEnterDeg;

            // per-arc body metrics (channel B + FullRom raw material)
            UpdateArcBodyMetrics(frame);

            if (!isArmed) return;

            // watermarks + θm extremes for the adaptive window / turnaround channels
            if (ArcState != DepthArcState.Idle)
            {
                if (CurrentDepth01 < RepMinDepth01) RepMinDepth01 = CurrentDepth01;
                if (CurrentDepth01 > RepMaxDepth01) RepMaxDepth01 = CurrentDepth01;
                if (thetaM < _arcThetaMin) { _arcThetaMin = thetaM; _arcThetaMinTime = timeSec; }
                if (thetaM > _arcThetaMax) _arcThetaMax = thetaM;
                if (_filter.LastSpeed > _maxSpeedThisPhase) _maxSpeedThisPhase = _filter.LastSpeed;
            }

            switch (ArcState)
            {
                case DepthArcState.Idle:
                    // Enter the game strictly from the top (aligned with ArmingElbowTopAngle).
                    _idleTopFrames = InTopZone ? _idleTopFrames + 1 : 0;
                    if (_idleTopFrames >= 2) StartArc(timeSec);
                    break;

                case DepthArcState.AwaitBottom:
                    if (graceLatched) break;
                    UpdateChannelBHint(frame, thetaM);
                    if (TryZoneLatch(thetaM, timeSec, bottom: true))
                    {
                        LatchBottom(timeSec);
                    }
                    // Fast-trough turnaround: the detector undersampled the trough (no sample
                    // landed ≤ BottomEnter), but the signal V-turned just above the zone at high
                    // speed — latch the observed minimum retroactively. A slow hoverer near the
                    // edge has low |v̂| and never triggers this.
                    else if (thetaM >= _arcThetaMin + CVConstants.TurnaroundRiseDeg
                             && _arcThetaMin <= BottomEnterDeg + CVConstants.FastTroughSlackDeg
                             && _maxSpeedThisPhase >= CVConstants.FastTurnaroundMinSpeedDegPerSec)
                    {
                        LatchBottom(_arcThetaMinTime >= 0f ? _arcThetaMinTime : timeSec);
                    }
                    break;

                case DepthArcState.AwaitTop:
                    if (thetaM > _crestMax) _crestMax = thetaM;
                    if (TryZoneLatch(thetaM, timeSec, bottom: false))
                    {
                        LatchTop();
                    }
                    // Fast-crest turnaround: symmetric — the ascent peaked just under TopEnter at
                    // high speed and turned back down (the next rep already started). Without this
                    // the two fast reps merge into one.
                    else if (thetaM <= _crestMax - CVConstants.TurnaroundRiseDeg
                             && _crestMax >= TopEnterDeg - CVConstants.FastCrestSlackDeg
                             && _maxSpeedThisPhase >= CVConstants.FastTurnaroundMinSpeedDegPerSec)
                    {
                        LatchTop();
                    }
                    break;
            }
        }

        /// <summary>Two consecutive-by-time frames in the zone (ZoneLatchSec) OR one frame deep
        /// past the edge (ZoneDeepLatchMarginDeg). Exit hysteresis: leaving the zone requires
        /// crossing Enter±6, so edge chatter doesn't reset the dwell timer.</summary>
        private bool TryZoneLatch(float thetaM, float timeSec, bool bottom)
        {
            if (bottom)
            {
                if (thetaM <= BottomEnterDeg - CVConstants.ZoneDeepLatchMarginDeg) return true;
                if (thetaM <= BottomEnterDeg)
                {
                    if (_bottomZoneEnterTime < 0f) _bottomZoneEnterTime = timeSec;
                    return timeSec - _bottomZoneEnterTime >= CVConstants.ZoneLatchSec;
                }
                if (thetaM > BottomEnterDeg + CVConstants.ZoneExitHysteresisDeg) _bottomZoneEnterTime = -1f;
                return false;
            }
            else
            {
                if (thetaM >= TopEnterDeg + CVConstants.ZoneDeepLatchMarginDeg) return true;
                if (thetaM >= TopEnterDeg)
                {
                    if (_topZoneEnterTime < 0f) _topZoneEnterTime = timeSec;
                    return timeSec - _topZoneEnterTime >= CVConstants.ZoneLatchSec;
                }
                if (thetaM < TopEnterDeg - CVConstants.ZoneExitHysteresisDeg) _topZoneEnterTime = -1f;
                return false;
            }
        }

        private void LatchBottom(float timeSec)
        {
            BottomLatched = true;
            BottomLatchedThisTick = true;
            BottomLatchTimeSec = timeSec;
            ArcState = DepthArcState.AwaitTop;
            _topZoneEnterTime = -1f;
            _crestMax = MedianElbowDeg;
            _maxSpeedThisPhase = 0f;
            BottomAltHintActive = false;
            OnBottomLatched?.Invoke();
        }

        private void LatchTop()
        {
            TopLatchedThisTick = true;
            // Arc complete — snapshot extremes for CommitRepAccepted/Rejected, then start the next.
            _lastArcThetaMin = _arcThetaMin;
            _lastArcThetaMax = _arcThetaMax;
            LastRepMinDepth01 = RepMinDepth01;
            LastRepMaxDepth01 = RepMaxDepth01;
            OnTopLatched?.Invoke();
            StartArc(BottomLatchTimeSec); // timeSec unused for arc start bookkeeping
        }

        private void StartArc(float timeSec)
        {
            ArcState = DepthArcState.AwaitBottom;
            BottomLatched = false;
            BottomLatchTimeSec = -1f;
            _bottomZoneEnterTime = -1f;
            _topZoneEnterTime = -1f;
            _idleTopFrames = 0;
            RepMinDepth01 = RepMaxDepth01 = CurrentDepth01;
            _arcThetaMin = _arcThetaMax = MedianElbowDeg;
            _arcThetaMinTime = timeSec;
            _crestMax = MedianElbowDeg;
            _maxSpeedThisPhase = 0f;
            ArcShoulderWidthMinImg = float.PositiveInfinity;
            ArcShoulderWidthMaxImg = 0f;
            ArcShoulderMidYMin = float.PositiveInfinity;
            ArcShoulderMidYMax = 0f;
            _arcTopShoulderY = -1f;
        }

        private void ResetArc(bool toIdle)
        {
            if (toIdle) ArcState = DepthArcState.Idle;
            BottomLatched = false;
            BottomLatchTimeSec = -1f;
            _bottomZoneEnterTime = -1f;
            _topZoneEnterTime = -1f;
            _idleTopFrames = 0;
            _descendStreak = 0;
            BottomAltHintActive = false;
        }

        private void UpdateArcBodyMetrics(in PoseFrame frame)
        {
            bool ls = frame.Visibility(PoseLandmark.LeftShoulder)  >= CVConstants.MinJointVisibility;
            bool rs = frame.Visibility(PoseLandmark.RightShoulder) >= CVConstants.MinJointVisibility;
            if (!ls || !rs) return;
            float aspect = frame.Aspect;
            Vector2 l = PoseMath.ToSquare(frame.Get(PoseLandmark.LeftShoulder).Pos2D, aspect);
            Vector2 r = PoseMath.ToSquare(frame.Get(PoseLandmark.RightShoulder).Pos2D, aspect);
            float sw = Vector2.Distance(l, r);
            float midY = 0.5f * (l.y + r.y);
            if (sw < ArcShoulderWidthMinImg) ArcShoulderWidthMinImg = sw;
            if (sw > ArcShoulderWidthMaxImg) ArcShoulderWidthMaxImg = sw;
            if (midY < ArcShoulderMidYMin) ArcShoulderMidYMin = midY;
            if (midY > ArcShoulderMidYMax) ArcShoulderMidYMax = midY;
            if (_arcTopShoulderY < 0f && ArcState == DepthArcState.AwaitBottom && InTopZone)
                _arcTopShoulderY = midY;
        }

        /// <summary>Channel B (tucked elbows / wide grip): θm stuck at 100–125 while the shoulders
        /// genuinely descend ≥ 0.6·sw. Behind <see cref="CVConstants.BottomAltChannelEnabled"/>
        /// (false until acceptance recordings); until then we only raise the HUD coaching hint.</summary>
        private void UpdateChannelBHint(in PoseFrame frame, float thetaM)
        {
            if (_arcTopShoulderY < 0f) return;
            bool ls = frame.Visibility(PoseLandmark.LeftShoulder)  >= CVConstants.MinJointVisibility;
            bool rs = frame.Visibility(PoseLandmark.RightShoulder) >= CVConstants.MinJointVisibility;
            if (!ls || !rs) return;
            float aspect = frame.Aspect;
            Vector2 l = PoseMath.ToSquare(frame.Get(PoseLandmark.LeftShoulder).Pos2D, aspect);
            Vector2 r = PoseMath.ToSquare(frame.Get(PoseLandmark.RightShoulder).Pos2D, aspect);
            float sw = Mathf.Max(Vector2.Distance(l, r), 1e-3f);
            float shoulderDrop = 0.5f * (l.y + r.y) - _arcTopShoulderY;
            bool deepDrop = shoulderDrop >= CVConstants.BottomAltShoulderDropFracSw * sw;

            if (CVConstants.BottomAltChannelEnabled
                && thetaM <= CVConstants.BottomAltMaxElbowDeg && deepDrop
                && NoseNearWrists(frame, sw))
            {
                LatchBottom(_lastValidTimeSec);
                return;
            }

            BottomAltHintActive = deepDrop && thetaM >= 100f && thetaM <= 125f;
        }

        private static bool NoseNearWrists(in PoseFrame frame, float sw)
        {
            if (frame.Visibility(PoseLandmark.Nose) < CVConstants.MinJointVisibility) return false;
            bool lw = frame.Visibility(PoseLandmark.LeftWrist)  >= CVConstants.MinJointVisibility;
            bool rw = frame.Visibility(PoseLandmark.RightWrist) >= CVConstants.MinJointVisibility;
            if (!lw && !rw) return false;
            float noseY = frame.Get(PoseLandmark.Nose).Y;
            float wristY = lw && rw
                ? 0.5f * (frame.Get(PoseLandmark.LeftWrist).Y + frame.Get(PoseLandmark.RightWrist).Y)
                : frame.Get(lw ? PoseLandmark.LeftWrist : PoseLandmark.RightWrist).Y;
            return Mathf.Abs(noseY - wristY) <= CVConstants.BottomAltNoseWristBandFracSw * sw;
        }

        // ── adaptive HUD zones (display only this release) ──────────────────────────────────────

        /// <summary>Called by PushupSession on Counter.OnRep — the completed arc's extremes join
        /// the adaptive median window.</summary>
        public void CommitRepAccepted()
        {
            _missedAttempts = 0;
            _botWindow[_adaptHead] = _lastArcThetaMin;
            _topWindow[_adaptHead] = _lastArcThetaMax;
            _adaptHead = (_adaptHead + 1) % _botWindow.Length;
            if (_adaptCount < _botWindow.Length) _adaptCount++;
            if (_adaptCount < CVConstants.AdaptiveMinReps) return;

            float medBot = MedianOf(_botWindow, _adaptCount);
            float medTop = MedianOf(_topWindow, _adaptCount);

            // Targets can only be STRICTER than the absolute envelope; asymmetric tighten caps.
            float targetBottom = Mathf.Clamp(medBot + CVConstants.AdaptiveMarginDeg,
                CVConstants.BottomElbowAngle - CVConstants.AdaptiveMaxTightenBottomDeg,
                CVConstants.BottomElbowAngle);
            float targetTop = Mathf.Clamp(medTop - CVConstants.AdaptiveMarginDeg,
                CVConstants.TopElbowAngle,
                CVConstants.TopElbowAngle + CVConstants.AdaptiveMaxTightenTopDeg);

            // Rate limit 1°/rep.
            HudBottomEnterDeg = Mathf.MoveTowards(HudBottomEnterDeg, targetBottom, 1f);
            HudTopEnterDeg = Mathf.MoveTowards(HudTopEnterDeg, targetTop, 1f);
        }

        /// <summary>Called on Counter.OnRepRejected — the arc's extremes are discarded; repeated
        /// misses decay the HUD bands back toward the absolute floor (never past it).</summary>
        public void CommitRepRejected()
        {
            _missedAttempts++;
            if (_missedAttempts < CVConstants.AdaptiveDecayAfterMissedAttempts) return;
            _missedAttempts = 0;
            HudBottomEnterDeg = Mathf.MoveTowards(HudBottomEnterDeg, CVConstants.BottomElbowAngle,
                CVConstants.AdaptiveDecayStepDeg);
            HudTopEnterDeg = Mathf.MoveTowards(HudTopEnterDeg, CVConstants.TopElbowAngle,
                CVConstants.AdaptiveDecayStepDeg);
        }

        private float MedianOf(float[] src, int n)
        {
            for (int i = 0; i < n; i++) _adaptScratch[i] = src[i];
            for (int i = 1; i < n; i++)
            {
                float key = _adaptScratch[i];
                int j = i - 1;
                while (j >= 0 && _adaptScratch[j] > key) { _adaptScratch[j + 1] = _adaptScratch[j]; j--; }
                _adaptScratch[j + 1] = key;
            }
            return _adaptScratch[n / 2];
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// Per-rep audit pipeline. Owns a ring-buffer of <see cref="RepSample"/>s (one per armed
    /// frame) and a list of <see cref="IRepValidator"/>s. When <see cref="AuditPendingRep"/>
    /// fires (called by <see cref="PushupRepCounter"/> at the top of a rep arc), runs the
    /// validators in declared order and returns the aggregated <see cref="RepVote"/>:
    /// <list type="bullet">
    ///   <item>First HardVeto short-circuits the loop (cheap fail-fast).</item>
    ///   <item>SoftDock penalties accumulate, clamped to
    ///   <see cref="CVConstants.MaxAggregatedSoftDockPenalty"/>.</item>
    ///   <item>All-Pass → <see cref="RepVote.Pass"/>.</item>
    /// </list>
    ///
    /// <para><b>Validator order matters</b> only for reporting (the first soft-dock reason wins
    /// when multiple fire) and for short-circuit speed. Cheap hard-veto checks (TempoSanity,
    /// RepVisibility) run first.</para>
    /// </summary>
    public sealed class AntiCheatAuditor
    {
        private readonly RepSample[] _buffer;
        private int _count;
        private bool _inRep;     // false while only Top frames have been observed (pre-rep lead-in)
        private bool _sawBottom; // set when a Bottom-phase sample arrives during the current rep
        private readonly List<IRepValidator> _validators = new List<IRepValidator>();

        /// <summary>The most recent verdict from <see cref="AuditPendingRep"/>. Defaults to Pass.
        /// HUD and telemetry read this.</summary>
        public RepVote LastVote { get; private set; } = RepVote.Pass;

        /// <summary>The validator <see cref="IRepValidator.Name"/> whose vote produced
        /// <see cref="LastVote"/> (HardVeto or first SoftDock). Empty string if Pass.</summary>
        public string LastVoteSource { get; private set; } = "";

        /// <summary>Latest computed per-rep window stats — for HUD-side display without re-audit.</summary>
        public int LastWindowFrameCount { get; private set; }
        public float LastWindowDurationSec { get; private set; }

        public IReadOnlyList<IRepValidator> Validators => _validators;

        /// <summary>Camera view snapshotted into each audited <see cref="RepWindow"/>. Updated by
        /// PushupSession from the ViewClassifier every frame; the classifier itself never switches
        /// mid-rep, so the value at audit time represents the whole window.</summary>
        public ViewKind CurrentView { get; set; } = ViewKind.Unknown;

        public AntiCheatAuditor() : this(null, null) { }

        /// <summary>Frontal-addendum ctor: with the knee-drop detector and foot-event monitor the
        /// auditor also registers <see cref="KneeCheatGate"/> and <see cref="SupportGeometryGate"/>.</summary>
        public AntiCheatAuditor(KneeDropDetector kneeDrop, FootEventMonitor footMonitor)
        {
            _buffer = new RepSample[CVConstants.RepWindowMaxFrames];

            // Order: cheap hard-fast first, then expensive (correlation, ROM math) so HardVeto
            // short-circuits avoid unnecessary work. Within "hard" group, order is mostly
            // arbitrary — first HardVeto wins.
            _validators.Add(new RepVisibilityGate());
            _validators.Add(new TempoSanityGate());
            _validators.Add(new SupportGeometryGate());
            if (kneeDrop != null)
                _validators.Add(new KneeCheatGate(kneeDrop, footMonitor));
            _validators.Add(new FullRomGate());
            _validators.Add(new BilateralSymmetryGate());
            _validators.Add(new HipDecouplingGate());
        }

        /// <summary>Append a frame snapshot to the rolling window. Caller (PushupSession) must
        /// only call this when the user is armed AND tracking is OK — otherwise the window will
        /// contain garbage frames that bias the validators.
        ///
        /// <para>Internal state machine prevents long pauses at Top (user resting between reps)
        /// from inflating the audit window's duration: while only Top frames have been observed
        /// since the last reset, the buffer is capped at <see cref="CVConstants.RepBodyAxisLeadFrames"/>
        /// (rolling). The moment a non-Top frame arrives, we "enter the rep" and start accumulating
        /// everything until <see cref="AuditPendingRep"/> fires.</para></summary>
        public void RecordSample(in RepSample sample)
        {
            // Transition to "in rep" the first time we see anything other than Top — the body-axis
            // lead-in samples accumulated so far become the "starting reference".
            if (!_inRep && sample.Phase != PushupPhase.Top) _inRep = true;
            if (sample.Phase == PushupPhase.Bottom) _sawBottom = true;

            // Abandoned descent — user started going down then returned to Top without reaching
            // Bottom. Counter won't fire CreditRep (so audit won't be called), but we still need
            // to drop the half-rep frames so they don't bleed into the next rep's window.
            if (_inRep && sample.Phase == PushupPhase.Top && !_sawBottom)
            {
                _count = 0;
                _inRep = false;
                // Fall through to record this Top sample as the start of a new lead-in.
            }

            if (!_inRep)
            {
                // Pre-rep Top phase: maintain a rolling tail of K samples — when the rep actually
                // starts these provide the body-axis estimate. Pauses don't inflate the window.
                int leadCap = CVConstants.RepBodyAxisLeadFrames;
                if (_count >= leadCap)
                {
                    Array.Copy(_buffer, 1, _buffer, 0, leadCap - 1);
                    _count = leadCap - 1;
                }
            }
            else if (_count >= _buffer.Length)
            {
                // In-rep overflow: rep dragged past MaxRepSeconds × headroom. Drop oldest by shift
                // — keep the recent tail. Rare (TempoSanity will HardVeto this rep anyway).
                Array.Copy(_buffer, 1, _buffer, 0, _buffer.Length - 1);
                _count = _buffer.Length - 1;
            }

            _buffer[_count++] = sample;
        }

        /// <summary>Run all validators on the window accumulated since the last reset. Clears the
        /// buffer at the end (next call to <see cref="RecordSample"/> starts a fresh rep window).
        /// Returns <see cref="RepVote.Pass"/> if there's nothing to audit (no samples).</summary>
        public RepVote AuditPendingRep()
        {
            var window = new RepWindow(_buffer, _count, CurrentView);
            LastWindowFrameCount = _count;
            LastWindowDurationSec = window.DurationSec;

            RepVote aggregated = RepVote.Pass;
            string firstSoftSource = "";
            float totalPenalty = 0f;
            RepRejectReason firstSoftReason = RepRejectReason.None;
            string vetoSource = "";

            foreach (var v in _validators)
            {
                RepVote vote = v.Validate(in window);
                if (vote.Kind == RepVoteKind.HardVeto)
                {
                    aggregated = vote;
                    vetoSource = v.Name;
                    break;
                }
                if (vote.Kind == RepVoteKind.SoftDock)
                {
                    totalPenalty += vote.Penalty;
                    if (firstSoftReason == RepRejectReason.None)
                    {
                        firstSoftReason = vote.Reason;
                        firstSoftSource = v.Name;
                    }
                }
            }

            if (aggregated.Kind != RepVoteKind.HardVeto)
            {
                if (totalPenalty > 0f)
                {
                    float capped = Mathf.Min(CVConstants.MaxAggregatedSoftDockPenalty, totalPenalty);
                    aggregated = RepVote.Dock(capped, firstSoftReason);
                    LastVoteSource = firstSoftSource;
                }
                else
                {
                    LastVoteSource = "";
                }
            }
            else
            {
                LastVoteSource = vetoSource;
            }

            LastVote = aggregated;
            _count = 0;
            _inRep = false;
            _sawBottom = false;
            return aggregated;
        }

        /// <summary>Wipe the rolling window without producing a vote. Used on disarm / session
        /// reset / tracking loss — samples from before a disruption shouldn't leak into the next
        /// rep's audit.</summary>
        public void Clear()
        {
            _count = 0;
            _inRep = false;
            _sawBottom = false;
        }
    }
}

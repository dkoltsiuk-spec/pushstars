using System;
using UnityEngine;

namespace PushStars.CV
{
    /// <summary>One completed hands-off-the-floor flight.</summary>
    public readonly struct ClapFlight
    {
        public readonly float TakeoffSec, LandingSec;
        /// <summary>Peak wrist rise, in planted shoulder widths.</summary>
        public readonly float PeakRiseSw;
        /// <summary>Smallest distance between the two hands (closest pair of wrist/finger points)
        /// as a fraction of the planted wrists-apart distance.</summary>
        public readonly float MinGapOfPlanted;
        public readonly bool IsClap;

        public ClapFlight(float takeoff, float landing, float peakRise, float minGap, bool isClap)
        {
            TakeoffSec = takeoff; LandingSec = landing; PeakRiseSw = peakRise;
            MinGapOfPlanted = minGap; IsClap = isClap;
        }

        public override string ToString()
            => $"{(IsClap ? "CLAP" : "flight")} {TakeoffSec:0.00}→{LandingSec:0.00} rise={PeakRiseSw:0.00}sw gap={MinGapOfPlanted:0.00}";
    }

    /// <summary>
    /// Detects the airborne phase of an explosive push-up and whether the hands met in the air.
    ///
    /// <para><b>Signal (frontal camera):</b> while the hands are planted the wrists sit still and
    /// wide apart; this learns their mid-point height, their spacing and the shoulder width. A
    /// flight opens when the wrist mid-point rises <see cref="CVConstants.ClapTakeoffRiseSw"/>
    /// planted shoulder widths and closes when it comes back down. It is a clap when, in the air,
    /// the HANDS touched: the closest pair of hand points (wrist, index, pinky, thumb tips) came
    /// within a small fraction of the planted wrist spacing. Wrists alone are not enough — an
    /// explosive lift swings them inward too (0.32–0.62 of planted), while palms meeting put the
    /// fingertips together (0.02–0.07) and a lift keeps them ≥ 0.21 apart.</para>
    ///
    /// <para>Only armed frames are judged; disarming forgets the baseline. Whether a clap is worth
    /// anything is decided by <see cref="PushupSession"/>: it must pair with a credited rep, which
    /// already passed the plank/ROM anti-cheat.</para>
    ///
    /// Pure C#, frame-driven.
    /// </summary>
    public sealed class ClapDetector
    {
        public bool InFlight { get; private set; }
        /// <summary>The last completed flight that satisfied the duration/height gates.</summary>
        public ClapFlight? LastFlight { get; private set; }
        public float TakeoffSec { get; private set; } = float.NegativeInfinity;
        /// <summary>Current wrist rise in planted shoulder widths (HUD/debug); NaN when unknown.</summary>
        public float RiseSw { get; private set; } = float.NaN;
        public bool HasBaseline => _baselineFrames >= CVConstants.ClapBaselineMinFrames;
        /// <summary>Shoulder width learned with the hands planted (square space) — the unit of
        /// <see cref="RiseSw"/>. 0 until there is a baseline.</summary>
        public float PlantedShoulderWidth => HasBaseline ? _baseSw : 0f;
        /// <summary>Closest the hands have come in the flight under way, as a fraction of the
        /// planted wrists-apart distance (what <see cref="ClapFlight.MinGapOfPlanted"/> will
        /// report on landing). Meaningful only while <see cref="InFlight"/>.</summary>
        public float FlightMinGapOfPlanted => _minGap;

        /// <summary>Raised when a flight lands (clap or not) and passed the flight gates.</summary>
        public event Action<ClapFlight> OnFlightLanded;

        private float _baseY, _baseGap, _baseSw;
        private int _baselineFrames;
        private float _peakRise, _minGap;
        private float _prevGap, _pairGap; // hands-close across two consecutive frames

        public void Reset()
        {
            InFlight = false;
            LastFlight = null;
            TakeoffSec = float.NegativeInfinity;
            RiseSw = float.NaN;
            _baselineFrames = 0;
        }

        public void Tick(in PoseFrame frame, bool armed, float now)
        {
            if (!armed || !frame.IsValid)
            {
                // Disarm mid-flight = the user did not come back to a plank: not a push-up flight.
                if (!armed) Reset();
                else if (InFlight && now - TakeoffSec > CVConstants.ClapMaxFlightSec) InFlight = false;
                return;
            }

            if (!TryWrists(frame, out Vector2 lw, out Vector2 rw, out float sw))
            {
                if (InFlight && now - TakeoffSec > CVConstants.ClapMaxFlightSec) InFlight = false;
                RiseSw = float.NaN;
                return;
            }

            float midY = 0.5f * (lw.y + rw.y);
            float gap = Vector2.Distance(lw, rw);
            float handGap = HandGap(frame, lw, rw);

            if (!HasBaseline)
            {
                Learn(midY, gap, sw);
                RiseSw = float.NaN;
                return;
            }

            // Image Y points down: wrists going UP = smaller y = positive rise.
            float rise = (_baseY - midY) / _baseSw;
            RiseSw = rise;

            if (!InFlight)
            {
                if (rise >= CVConstants.ClapTakeoffRiseSw)
                {
                    InFlight = true;
                    TakeoffSec = now;
                    _peakRise = rise;
                    _minGap = _prevGap = handGap / _baseGap;
                    _pairGap = float.PositiveInfinity;
                }
                else if (Mathf.Abs(rise) < CVConstants.ClapLandRiseSw)
                {
                    Learn(midY, gap, sw); // hands on the floor: keep the planted baseline fresh
                }
                return;
            }

            _peakRise = Mathf.Max(_peakRise, rise);
            float gapOfPlanted = handGap / _baseGap;
            _minGap = Mathf.Min(_minGap, gapOfPlanted);
            _pairGap = Mathf.Min(_pairGap, Mathf.Max(_prevGap, gapOfPlanted));
            _prevGap = gapOfPlanted;

            float dur = now - TakeoffSec;
            if (dur > CVConstants.ClapMaxFlightSec)
            {
                InFlight = false; // got up / waved — not a push-up flight
                return;
            }
            if (rise > CVConstants.ClapLandRiseSw) return;

            InFlight = false;
            if (dur < CVConstants.ClapMinFlightSec
                || _peakRise < CVConstants.ClapMinPeakRiseSw
                || _peakRise > CVConstants.ClapMaxPeakRiseSw) return;

            // Touch caught on a frame, or hands held together across two frames — at 15 fps the
            // contact frame itself can fall between samples.
            bool touched = _minGap <= CVConstants.ClapMaxHandGapOfPlanted
                           || _pairGap <= CVConstants.ClapMaxHandGapPairOfPlanted;
            bool clap = touched && _minGap * _baseGap / _baseSw <= CVConstants.ClapMaxHandGapSw;
            var flight = new ClapFlight(TakeoffSec, now, _peakRise, _minGap, clap);
            LastFlight = flight;
            OnFlightLanded?.Invoke(flight);
        }

        private void Learn(float midY, float gap, float sw)
        {
            if (_baselineFrames == 0) { _baseY = midY; _baseGap = gap; _baseSw = sw; }
            else
            {
                float a = CVConstants.ClapBaselineAlpha;
                _baseY += a * (midY - _baseY);
                _baseGap += a * (gap - _baseGap);
                _baseSw += a * (sw - _baseSw);
            }
            _baselineFrames++;
        }

        private static readonly PoseLandmark[] LeftHand =
            { PoseLandmark.LeftPinky, PoseLandmark.LeftIndex, PoseLandmark.LeftThumb };
        private static readonly PoseLandmark[] RightHand =
            { PoseLandmark.RightPinky, PoseLandmark.RightIndex, PoseLandmark.RightThumb };

        /// <summary>Closest distance between any visible point of the left hand and any visible
        /// point of the right hand (wrists always included), square space.</summary>
        private static float HandGap(in PoseFrame f, Vector2 lw, Vector2 rw)
        {
            float aspect = f.Aspect;
            float best = Vector2.Distance(lw, rw);
            for (int i = -1; i < LeftHand.Length; i++)
            {
                Vector2 l = lw;
                if (i >= 0)
                {
                    if (f.Visibility(LeftHand[i]) < CVConstants.ClapWristMinVis) continue;
                    l = PoseMath.ToSquare(f.Get(LeftHand[i]).Pos2D, aspect);
                }
                for (int j = -1; j < RightHand.Length; j++)
                {
                    Vector2 r = rw;
                    if (j >= 0)
                    {
                        if (f.Visibility(RightHand[j]) < CVConstants.ClapWristMinVis) continue;
                        r = PoseMath.ToSquare(f.Get(RightHand[j]).Pos2D, aspect);
                    }
                    best = Mathf.Min(best, Vector2.Distance(l, r));
                }
            }
            return best;
        }

        /// <summary>Both wrists (square space) and the shoulder width, or false when a wrist or a
        /// shoulder is not usable. A clap needs both hands, so one visible wrist is not enough.</summary>
        private static bool TryWrists(in PoseFrame f, out Vector2 lw, out Vector2 rw, out float sw)
        {
            lw = rw = default;
            sw = 0f;
            if (f.Visibility(PoseLandmark.LeftWrist) < CVConstants.ClapWristMinVis
                || f.Visibility(PoseLandmark.RightWrist) < CVConstants.ClapWristMinVis
                || f.Visibility(PoseLandmark.LeftShoulder) < CVConstants.MinJointVisibility
                || f.Visibility(PoseLandmark.RightShoulder) < CVConstants.MinJointVisibility)
                return false;

            float aspect = f.Aspect;
            lw = PoseMath.ToSquare(f.Get(PoseLandmark.LeftWrist).Pos2D, aspect);
            rw = PoseMath.ToSquare(f.Get(PoseLandmark.RightWrist).Pos2D, aspect);
            sw = Vector2.Distance(PoseMath.ToSquare(f.Get(PoseLandmark.LeftShoulder).Pos2D, aspect),
                                  PoseMath.ToSquare(f.Get(PoseLandmark.RightShoulder).Pos2D, aspect));
            return sw > 1e-3f;
        }
    }
}

using PushStars.CV.AntiCheat;
using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// "The set is over": the player has left the plank and got up — stood, knelt up, or walked
    /// up to the phone and out of the frame. A timed training set ends on this instead of
    /// making the player reach for the screen (owner's request, 2026-10-22).
    ///
    /// <para>Only judged while disarmed: an armed player is by definition still in the set. A rest
    /// that stays low (on the heels with the torso forward, all fours) is a pause, not the end.</para>
    ///
    /// Pure C#, frame-driven. The caller decides whether the set had any reps to end.
    /// </summary>
    public sealed class StandUpDetector
    {
        /// <summary>This frame looks like someone up off the floor: torso upright, or walked up to
        /// the phone (out of frame, too close to read, shoulders below the hips in the crop).
        /// Held through frames that cannot be judged.</summary>
        public bool Away { get; private set; }
        /// <summary>Seconds <see cref="Away"/> has held without a break.</summary>
        public float AwayForSec { get; private set; }
        /// <summary><see cref="Away"/> for <see cref="CVConstants.StandUpHoldSec"/>.</summary>
        public bool StoodUp { get; private set; }

        private float _awaySince = -1f;

        public void Reset()
        {
            Away = StoodUp = false;
            AwayForSec = 0f;
            _awaySince = -1f;
        }

        public void Tick(in PoseFrame frame, bool trackingOk, bool armed, ViewKind view, float nowSec)
        {
            if (armed) { Reset(); return; }

            // Measured (CVRecordings/clap/1158, standing up after the set): κ climbs to ~1.1 as the
            // player rises, then they walk to the phone — κ flips negative in the close crop and
            // tracking drops within a second. A low rest keeps κ under 1.0 and stays tracked.
            if (!trackingOk || !frame.IsValid) Away = true;
            else if (view == ViewKind.Side)
            {
                if (TryTorsoTiltDeg(frame, out float tilt))
                {
                    if (tilt <= CVConstants.StandUpSideTiltDeg) Away = true;
                    else if (tilt >= CVConstants.StandUpSideReleaseTiltDeg) Away = false;
                }
            }
            else if (PlankArmer.TryReliableKappa(frame, out float kappa))
            {
                if (kappa >= CVConstants.PostureUprightKappa || kappa <= CVConstants.StandUpCroppedKappa) Away = true;
                else if (kappa < CVConstants.PostureUprightReleaseKappa) Away = false;
            }

            if (!Away)
            {
                _awaySince = -1f;
                AwayForSec = 0f;
                StoodUp = false;
                return;
            }
            if (_awaySince < 0f) _awaySince = nowSec;
            AwayForSec = nowSec - _awaySince;
            StoodUp = AwayForSec >= CVConstants.StandUpHoldSec;
        }

        /// <summary>Angle of hip-mid → shoulder-mid from vertical, square space (0 = upright).</summary>
        private static bool TryTorsoTiltDeg(in PoseFrame f, out float tilt)
        {
            tilt = 90f;
            bool ls = f.Visibility(PoseLandmark.LeftShoulder) >= CVConstants.MinJointVisibility;
            bool rs = f.Visibility(PoseLandmark.RightShoulder) >= CVConstants.MinJointVisibility;
            bool lh = f.Visibility(PoseLandmark.LeftHip) >= CVConstants.MinJointVisibility;
            bool rh = f.Visibility(PoseLandmark.RightHip) >= CVConstants.MinJointVisibility;
            if ((!ls && !rs) || (!lh && !rh)) return false;
            float aspect = f.Aspect;
            Vector2 shoulder = Mid(f, PoseLandmark.LeftShoulder, PoseLandmark.RightShoulder, ls, rs, aspect);
            Vector2 hip = Mid(f, PoseLandmark.LeftHip, PoseLandmark.RightHip, lh, rh, aspect);
            Vector2 torso = shoulder - hip;
            if (torso.sqrMagnitude < 1e-6f) return false;
            // Image Y points down: an upright torso has the shoulders above (smaller y) the hips.
            tilt = Vector2.Angle(Vector2.down, torso);
            return true;
        }

        private static Vector2 Mid(in PoseFrame f, PoseLandmark a, PoseLandmark b, bool hasA, bool hasB, float aspect)
        {
            if (hasA && hasB) return .5f * (PoseMath.ToSquare(f.Get(a).Pos2D, aspect) + PoseMath.ToSquare(f.Get(b).Pos2D, aspect));
            return PoseMath.ToSquare(f.Get(hasA ? a : b).Pos2D, aspect);
        }
    }
}

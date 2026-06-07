namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// Why <see cref="PlankArmer.IsValidPlank"/> returned false for a given frame. Surfaced on the
    /// HUD ("KNEES DOWN", "BODY SAGGING", ...) and logged as the disarm reason in telemetry.
    /// </summary>
    public enum PlankRejectReason
    {
        /// <summary>Plank predicate passed — frame is a valid plank.</summary>
        Ok = 0,

        /// <summary>No ankle / foot index / knee visible at <see cref="CVConstants.PlankLowerBodyVisibility"/>.
        /// User's lower body is out of frame — we can't tell if it's a plank.</summary>
        LowerBodyNotVisible,

        /// <summary>Body-line angle (shoulder-hip-ankle) below <see cref="CVConstants.ArmingBodyLineAngle"/>.
        /// Hips sagging or piked, or user sitting up.</summary>
        BodySagging,

        /// <summary><see cref="KneeBendDetector"/> classification reports Bent — knee push-up.</summary>
        KneesBent,

        /// <summary>Elbow angle below <see cref="CVConstants.ArmingElbowTopAngle"/> — user is in
        /// the middle of a rep, not at the top. Arming must start from a fully-extended position.</summary>
        NotAtTop,

        /// <summary><see cref="WristAnchorMonitor"/> reports <see cref="AnchorVerdict.Airborne"/>
        /// — wrists are clearly moving through space, not planted on a support.</summary>
        WristsAirborne,

        /// <summary>Tracking quality lost OR frame invalid this tick.</summary>
        TrackingLost,

        /// <summary>Reserved for Stage 2 S10 (BodyHorizontal, world-landmarks only) —
        /// torso plane not roughly perpendicular to the local gravity direction.</summary>
        BodyNotHorizontal,
    }
}

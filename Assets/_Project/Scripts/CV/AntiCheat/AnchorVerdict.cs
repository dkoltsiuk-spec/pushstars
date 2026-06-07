namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// Result of <see cref="WristAnchorMonitor"/> for the current frame — how confidently the
    /// user's wrists appear to be planted on a fixed support (the floor).
    /// </summary>
    public enum AnchorVerdict
    {
        /// <summary>Not enough data yet: sliding window not full, OR both wrists were below
        /// <see cref="CVConstants.MinJointVisibility"/> across the window, OR torso scale couldn't
        /// be computed. Consumers must NOT treat this as airborne — it just means we don't know.</summary>
        Unknown = 0,

        /// <summary>Wrist drift below the soft threshold — hands are planted, no penalty.</summary>
        Anchored = 1,

        /// <summary>Wrist drift between soft and hard threshold — hands wobble. Soft form-dock.</summary>
        Drifting = 2,

        /// <summary>Wrist drift above the hard threshold — hands are clearly moving through space.
        /// Hard veto: counter must disarm.</summary>
        Airborne = 3,
    }
}

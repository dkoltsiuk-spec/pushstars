namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// Result of <see cref="KneeBendDetector"/> after the ribbon smoothing — whether the user
    /// appears to be on their toes (Straight) or on their knees (Bent).
    /// </summary>
    public enum KneeClassification
    {
        /// <summary>Knee landmarks not visible enough to classify, or the ribbon hasn't filled
        /// since startup / reset. Consumers must NOT treat this as Straight — it means unknown.</summary>
        Unknown = 0,

        /// <summary>Hip-knee-ankle angle held above the upper hysteresis for the ribbon window —
        /// real (toe) push-up posture.</summary>
        Straight = 1,

        /// <summary>Hip-knee-ankle angle held below the lower hysteresis for the ribbon window —
        /// knee push-up. Hard veto.</summary>
        Bent = 2,
    }
}

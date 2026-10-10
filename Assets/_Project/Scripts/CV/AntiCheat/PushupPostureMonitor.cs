namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// Notices that an armed player has left the push-up position — sat back to rest, knelt up,
    /// stood — within half a second, instead of waiting out the armer's grace.
    ///
    /// <para><b>Why the armer alone is not enough.</b> Its plank predicate only holds at the TOP of
    /// a rep, so it cannot tell "elbows bent mid-rep" from "elbows bent, sitting on the heels":
    /// both fail it, and while the elbows are bent the Cooling timer is frozen for up to
    /// <see cref="CVConstants.MaxRepSeconds"/> so a slow honest rep is not disarmed at the bottom.
    /// A resting player therefore stayed armed, and everything downstream kept reading their arms
    /// as a push-up: the avatar scrubbed its clip off the elbow angle, every lift of the hands
    /// opened a clap flight, and a stretch could complete a rep arc.</para>
    ///
    /// <para><b>Signals</b> — both hold through a whole rep, bottom included, which is what the
    /// plank predicate cannot offer:
    /// <list type="bullet">
    /// <item>Hands off the floor: the wrists sit above their planted height (the
    /// <see cref="ClapDetector"/>'s baseline). A push-up flight does this too, for 0.2 s.</item>
    /// <item>Torso upright: κ far above anything a horizontal body reads.</item>
    /// </list>
    /// Either one, held for <see cref="CVConstants.PostureLeftHoldSec"/>, raises
    /// <see cref="LeftPose"/>; the session then disarms without the grace.</para>
    ///
    /// <para>A rest that keeps the palms planted and the torso low (all fours, child's pose) is
    /// not seen here — the armer's own predicate and the knee gates still cover it.</para>
    ///
    /// Pure C#, frame-driven.
    /// </summary>
    public sealed class PushupPostureMonitor
    {
        /// <summary>The wrists are above where they were planted.</summary>
        public bool HandsLifted { get; private set; }
        /// <summary>The torso reads as upright.</summary>
        public bool TorsoUpright { get; private set; }
        /// <summary>This frame does not look like a push-up. True through a clap flight as well —
        /// what the arms do meanwhile is not push-up depth.</summary>
        public bool Suspect => HandsLifted || TorsoUpright;
        /// <summary>Seconds <see cref="Suspect"/> has held without a break.</summary>
        public float SuspectForSec { get; private set; }
        /// <summary><see cref="Suspect"/> outlasted any push-up flight: the player is resting.</summary>
        public bool LeftPose { get; private set; }

        private float _suspectSince = -1f;

        public void Reset()
        {
            HandsLifted = TorsoUpright = LeftPose = false;
            SuspectForSec = 0f;
            _suspectSince = -1f;
        }

        /// <param name="riseSw">Wrist rise over the planted baseline, in planted shoulder widths
        /// (<see cref="ClapDetector.RiseSw"/>); NaN when it cannot be judged this frame.</param>
        /// <param name="plantedSw">That planted shoulder width (square space); 0 when unknown.</param>
        public void Tick(in PoseFrame frame, bool trackingOk, bool armed, ViewKind view,
                         float riseSw, float plantedSw, float nowSec)
        {
            if (!armed) { Reset(); return; }

            // A frame that cannot be judged holds each signal: hands that went up and then
            // blurred out of sight have not come back down.
            if (trackingOk && frame.IsValid)
            {
                // Side-on the shoulders overlap, and anything divided by their width is noise.
                bool widthUsable = view != ViewKind.Side && plantedSw >= CVConstants.KappaReliableMinSw;
                if (!widthUsable) HandsLifted = false;
                else if (!float.IsNaN(riseSw))
                {
                    if (riseSw >= CVConstants.ClapTakeoffRiseSw) HandsLifted = true;
                    else if (riseSw <= CVConstants.ClapLandRiseSw) HandsLifted = false;
                }

                if (view == ViewKind.Side) TorsoUpright = false;
                else if (PlankArmer.TryReliableKappa(frame, out float kappa))
                {
                    if (kappa >= CVConstants.PostureUprightKappa) TorsoUpright = true;
                    else if (kappa < CVConstants.PostureUprightReleaseKappa) TorsoUpright = false;
                }
            }

            if (!Suspect)
            {
                _suspectSince = -1f;
                SuspectForSec = 0f;
                LeftPose = false;
                return;
            }
            if (_suspectSince < 0f) _suspectSince = nowSec;
            SuspectForSec = nowSec - _suspectSince;
            LeftPose = SuspectForSec >= CVConstants.PostureLeftHoldSec;
        }
    }
}

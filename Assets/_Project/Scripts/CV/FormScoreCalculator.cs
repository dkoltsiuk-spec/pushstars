using UnityEngine;

namespace PushStars.CV
{
    /// <summary>One frame's form readout.</summary>
    public readonly struct FormReading
    {
        public readonly float ElbowAngleDeg;
        public readonly float BodyLineDeg;
        public readonly float DepthScore;     // 0..100
        public readonly float BodyLineScore;  // 0..100
        public readonly float Form;           // 0..100 (weighted blend)

        public FormReading(float elbow, float bodyLine, float depthScore, float bodyLineScore, float form)
        {
            ElbowAngleDeg = elbow; BodyLineDeg = bodyLine;
            DepthScore = depthScore; BodyLineScore = bodyLineScore; Form = form;
        }
    }

    /// <summary>
    /// Scores pushup technique from a single <see cref="PoseFrame"/>:
    ///   • <b>Depth</b> — how far the elbows bend (deeper bottom = better), clamped between
    ///     <see cref="CVConstants.FullDepthElbowAngle"/> and <see cref="CVConstants.ShallowDepthElbowAngle"/>.
    ///   • <b>Body line</b> — how straight the shoulder–hip–ankle line is (no sagging / piking).
    /// The combined FORM is a weighted blend (see CVConstants weights). Stateless and pure, so the
    /// HUD can show a live value and <see cref="PushupRepCounter"/>'s OnRep can sample the value at
    /// the bottom of each rep.
    /// </summary>
    public static class FormScoreCalculator
    {
        public static FormReading Evaluate(in PoseFrame frame)
        {
            float elbow    = PoseMath.ElbowAngle(frame);
            float bodyLine = PoseMath.BodyLineAngle(frame);

            float depthScore    = DepthScore(elbow);
            float bodyLineScore = BodyLineScore(bodyLine);

            float form = CVConstants.DepthWeight * depthScore
                       + CVConstants.BodyLineWeight * bodyLineScore;

            return new FormReading(elbow, bodyLine, depthScore, bodyLineScore, form);
        }

        /// <summary>Maps the (bottom) elbow angle to a 0..100 depth score — smaller angle = deeper.</summary>
        public static float DepthScore(float elbowAngle)
        {
            float t = Mathf.InverseLerp(CVConstants.ShallowDepthElbowAngle,
                                        CVConstants.FullDepthElbowAngle, elbowAngle);
            return Mathf.Clamp01(t) * 100f;
        }

        /// <summary>Maps body-line deviation from straight to a 0..100 score.</summary>
        public static float BodyLineScore(float bodyLineAngle)
        {
            float deviation = Mathf.Abs(CVConstants.StraightBodyAngle - bodyLineAngle);
            float t = 1f - Mathf.Clamp01(deviation / CVConstants.BodyLineZeroAt);
            return t * 100f;
        }
    }
}

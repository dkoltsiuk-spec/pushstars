using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Minimal on-screen readout for testing the CV pipeline (phase 08). Shows REPS / PHASE / FORM /
    /// TEMPO and tracking quality from a <see cref="PushupSession"/>. Works the same whether the
    /// session is fed by MockPoseSource (no camera) or MediaPipePoseSource (real webcam). Throwaway
    /// debug UI — the real duel HUD is phase 14.
    /// </summary>
    public sealed class PushupDebugHud : MonoBehaviour
    {
        [SerializeField] private PushupSession _session;

        private GUIStyle _style;

        private void OnGUI()
        {
            if (_session == null) return;

            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };

            var f = _session.LastForm;
            string text =
                $"REPS:  {_session.Reps}\n" +
                $"PHASE: {_session.Phase}\n" +
                $"FORM:  {f.Form:0}   (depth {f.DepthScore:0}, line {f.BodyLineScore:0})\n" +
                $"ELBOW: {f.ElbowAngleDeg:0}°\n" +
                $"TEMPO: {_session.TempoRpm:0} rpm\n" +
                $"TRACK: {_session.Quality}";

            // Shadow for readability over the camera feed.
            var shadow = new GUIStyle(_style) { normal = { textColor = Color.black } };
            GUI.Label(new Rect(22, 22, 900, 400), text, shadow);
            GUI.Label(new Rect(20, 20, 900, 400), text, _style);
        }
    }
}

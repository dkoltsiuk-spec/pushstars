using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>One deterministic timeline owns every case layer. The scene supplies the rig;
    /// sampling never allocates objects, consumes gameplay randomness or changes rewards.</summary>
    public sealed class CaseOpeningMotion : MonoBehaviour
    {
        public const float TapDuration = .80f;
        public const float RevealDuration = .90f;
        public const float UpgradeMoment = .27f;
        public RewardScreen Screen;
        public CaseMotionGraphic Behind, InFront;
        private Vector3 _scale, _starsScale, _titleScale;
        private Quaternion _rotation;
        private Vector2 _position;
        private Vector3[] _pipScales;
        private Color _hintColor, _glowColor;
        private bool _captured;
        private Material _material;
        private Material _originalMaterial;
        private static readonly int Shine = Shader.PropertyToID("_Shine");
        private static readonly int White = Shader.PropertyToID("_White");

        public void Initialize()
        {
            if (_captured || Screen == null || Screen.CaseUi.Content == null) return;
            var ui = Screen.CaseUi;
            _scale = ui.Content.localScale; _rotation = ui.Content.localRotation; _position = ui.Content.anchoredPosition;
            _starsScale = ui.StarsContent != null ? ui.StarsContent.localScale : Vector3.one;
            _titleScale = ui.Rarity != null ? ui.Rarity.rectTransform.localScale : Vector3.one;
            _hintColor = ui.Hint != null ? ui.Hint.color : Color.white;
            _glowColor = ui.Glow != null ? ui.Glow.color : Color.white;
            _pipScales = new Vector3[ui.TapPips.Length];
            for (int i = 0; i < _pipScales.Length; i++)
                if (ui.TapPips[i] != null) _pipScales[i] = ui.TapPips[i].rectTransform.localScale;
            if (ui.Artwork != null && ui.Artwork.material != null && ui.Artwork.material.HasProperty(Shine))
            {
                _originalMaterial = ui.Artwork.material;
                _material = new Material(_originalMaterial) { name = "Case motion instance", hideFlags = HideFlags.DontSave };
                ui.Artwork.material = _material;
            }
            _captured = true;
        }

        public void Idle(float time)
        {
            Initialize(); if (!_captured) return;
            float phase = Mathf.Repeat(time, 3);
            float e = phase < .7f ? Mathf.Sin(phase / .7f * Mathf.PI) : 0;
            float wobble = Mathf.Sin(phase * 27) * e;
            Pose(1 + e * .012f, 1 - e * .009f, Mathf.Sin(time * 2) * 1.5f, wobble * 1.8f);
            Effects(time, 0, 0, 0, 0, .65f);
            SetMaterial(Mathf.Repeat(time * .18f, 2.6f) - .6f, 0);
        }

        public void SampleTap(float seconds, bool upgraded, int tap)
        {
            Initialize(); if (!_captured) return;
            float t = Mathf.Clamp(seconds, 0, TapDuration);
            float squash = Mathf.Sin(Mathf.Clamp01(t / .16f) * Mathf.PI);
            float jumpT = Mathf.Clamp01((t - .12f) / .50f);
            float jump = Mathf.Sin(jumpT * Mathf.PI);
            float settle = t > .56f ? Mathf.Sin((t - .56f) / .24f * Mathf.PI * 2) * (1 - Mathf.Clamp01((t - .56f) / .24f)) : 0;
            float kick = Mathf.Sin(jumpT * Mathf.PI * 2) * jump;
            float strength = upgraded ? 1 : .58f;
            Pose(1 + squash * .115f - jump * .045f + settle * .035f,
                1 - squash * .10f + jump * .10f - settle * .028f,
                -squash * 6 + jump * 34 * strength, kick * 7 * strength);
            float orbit = Mathf.Sin(Mathf.Clamp01((t - .14f) / .58f) * Mathf.PI);
            float flash = upgraded ? Mathf.Pow(Mathf.Max(0, 1 - Mathf.Abs(t - UpgradeMoment) / .085f), 2) : 0;
            Effects(t, Mathf.Clamp01((t - .2f) / .56f), orbit * strength, 0, flash * .60f, .6f);
            SetMaterial((t - .14f) * 2.8f, flash * .80f);
            var ui = Screen.CaseUi;
            float celebrate = Mathf.Sin(Mathf.Clamp01((t - UpgradeMoment) / .43f) * Mathf.PI);
            if (ui.StarsContent != null) ui.StarsContent.localScale = _starsScale * (1 + celebrate * (upgraded ? .22f : .035f));
            if (ui.Rarity != null) ui.Rarity.rectTransform.localScale = _titleScale * (1 + celebrate * (upgraded ? .11f : 0));
            for (int i = 0; i < _pipScales.Length; i++)
                if (ui.TapPips[i] != null) ui.TapPips[i].rectTransform.localScale = _pipScales[i] * (1 + (i == tap - 1 ? Mathf.Sin(Mathf.Clamp01(t / .4f) * Mathf.PI) * .48f : 0));
        }

        public void SampleReveal(float seconds)
        {
            Initialize(); if (!_captured) return;
            float t = Mathf.Clamp(seconds, 0, RevealDuration);
            float charge = Mathf.Clamp01(t / .38f);
            float release = Mathf.Clamp01((t - .38f) / .34f);
            float stretch = 1 - Mathf.Pow(1 - release, 3);
            float rumble = t < .38f ? Mathf.Sin(t * 58) * charge : 0;
            // The case stays one intact image: compress, stretch vertically, then
            // transition directly to the prize screen. No lid or interior geometry.
            Pose(1 + charge * .075f * (1 - stretch) - stretch * .035f,
                1 - charge * .065f * (1 - stretch) + stretch * .14f,
                -charge * 5 * (1 - stretch) + stretch * 12, rumble * 1.8f);
            float flash = Mathf.Clamp01((t - .72f) / .18f);
            Effects(t, Mathf.Clamp01((t - .38f) / .52f), Mathf.Sin(release * Mathf.PI) * .65f, stretch, flash * .80f, .8f);
            SetMaterial(release * 1.6f - .2f, flash * .6f);
            var ui = Screen.CaseUi;
            if (ui.Hint != null) { Color c = _hintColor; c.a *= 1 - charge; ui.Hint.color = c; }
            if (ui.Glow != null) { Color c = ui.Glow.color; c.a = .34f + stretch * .36f; ui.Glow.color = c; }
        }

        private void Pose(float x, float y, float lift, float degrees)
        {
            var rt = Screen.CaseUi.Content;
            rt.localScale = Vector3.Scale(_scale, new Vector3(x, y, 1));
            rt.localRotation = _rotation * Quaternion.Euler(0, 0, degrees);
            rt.anchoredPosition = _position + Vector2.up * lift;
        }
        private void Effects(float time, float burst, float orbit, float opening, float flash, float idle)
        {
            Color tint = Screen.CaseUi.Rarity != null ? Screen.CaseUi.Rarity.color : Color.white;
            Apply(Behind); Apply(InFront);
            void Apply(CaseMotionGraphic layer)
            {
                if (layer == null) return;
                layer.Clock = time; layer.Burst = burst; layer.Orbit = orbit; layer.Charge = opening;
                layer.Lift = Screen.CaseUi.Content.anchoredPosition.y - _position.y;
                layer.Flash = flash; layer.Idle = idle; layer.Tint = tint; layer.SetVerticesDirty();
            }
        }
        private void SetMaterial(float shine, float white)
        {
            if (_material == null) return;
            _material.SetFloat(Shine, shine); _material.SetFloat(White, white);
        }
        public void ResetPose()
        {
            if (!_captured || Screen == null) return;
            Pose(1, 1, 0, 0); Effects(0, 0, 0, 0, 0, 0); SetMaterial(-1, 0);
            var ui = Screen.CaseUi;
            if (ui.StarsContent != null) ui.StarsContent.localScale = _starsScale;
            if (ui.Rarity != null) ui.Rarity.rectTransform.localScale = _titleScale;
            if (ui.Hint != null) ui.Hint.color = _hintColor;
            if (ui.Glow != null) { Color c = ui.Glow.color; c.a = _glowColor.a; ui.Glow.color = c; }
            for (int i = 0; i < _pipScales.Length; i++)
                if (ui.TapPips[i] != null) ui.TapPips[i].rectTransform.localScale = _pipScales[i];
        }
        private void OnDisable() => ResetPose();
        private void OnDestroy()
        {
            if (_material == null) return;
            if (Screen != null && Screen.CaseUi.Artwork != null) Screen.CaseUi.Artwork.material = _originalMaterial;
            if (Application.isPlaying) Destroy(_material); else DestroyImmediate(_material);
        }
    }
}

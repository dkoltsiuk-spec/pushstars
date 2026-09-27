using PushStars.Core;
using PushStars.UI.Layout;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>"+N AURA" meme stamp: the number slams into a black screen with a flash, shake and
    /// RGB split, then the skull glitches in and the screen drops to black and white. A loss stamps
    /// a red "-N". Under the stamp, the fight's Aura moments tick in one line at a time. On collect
    /// the skull flies into the camera and the rest eases out after it.
    /// Authored by AssessmentRewardSetup; this only animates.</summary>
    public sealed class AuraStampPresentation : MonoBehaviour
    {
        /// <summary>Matches the boom in aura_stamp.wav (tools/audio/make_aura_stamp.py).</summary>
        public const float Impact = .16f;
        public const float SkullStart = Impact + .75f;
        /// <summary>Matches the 808 in aura_skull.wav (tools/audio/make_aura_skull.py).</summary>
        public const float SkullLock = SkullStart + .38f;
        public const float RevealSeconds = SkullLock + .25f;
        public const float ClaimSeconds = .8f;
        // The group lands level; only the number itself leans (authored on its rect).
        private const float StampTilt = 0f;
        private static readonly Color Cyan = new Color(.2f, .9f, 1f), Magenta = new Color(1f, .24f, .86f);

        /// <summary>Root sits in the scaled Composition; Flash is full-bleed on the canvas.</summary>
        public RectTransform Root, Shaker, Stamp, GhostA, GhostB, Skull;
        public CanvasGroup Body, SkullGroup;
        public AuraStampFxGraphic Fx;
        public Image Flash;
        [Tooltip("Neutral black over the violet backdrop while the screen is black and white.")]
        public Image MonoShade;
        public FightRewardBackdrop Backdrop;
        public RawImage[] SkullSlices = new RawImage[0];
        public TextMeshProUGUI Number, Word, GhostNumberA, GhostWordA, GhostNumberB, GhostWordB, ClaimHint;
        [Tooltip("Captioned Aura moments under the stamp (\"VICTORY +1000\"). Optional.")]
        public TextMeshProUGUI Moments;
        /// <summary>Seconds between moment lines; the first lands just after the stamp.</summary>
        public const float MomentStep = .14f;
        private const float MomentsStart = Impact + .3f;
        private static readonly Color LossRed = new Color(1f, .27f, .3f);
        private int _momentCount;
        /// <summary>When the last moment line has landed (the reveal holds taps until then).</summary>
        public float MomentsRevealSeconds => _momentCount > 0 ? MomentsStart + _momentCount * MomentStep : 0;
        private float _numberSize = -1;

        private bool _active, _idle, _impacted, _skullPlayed, _locked, _captured;
        private float _idleTime;
        private Vector2 _stampHome, _skullHome;
        private Material _sourceMaterial, _material;
        private Texture _outlineGradient;
        private static readonly Color MonoOutline = new Color(.3f, .3f, .32f);

        public void Configure(int amount) => Configure(amount, null);

        public void Configure(long amount, string[] moments)
        {
            if (!_captured) { _stampHome = Stamp.anchoredPosition; _skullHome = Skull.anchoredPosition; _captured = true; }
            _active = true; _idle = _impacted = _skullPlayed = _locked = false;
            Root.gameObject.SetActive(true);
            Flash.gameObject.SetActive(true);
            MonoShade.gameObject.SetActive(true);
            if (Backdrop != null) { Backdrop.Appearance = FightRewardBackdrop.Style.Aura; Backdrop.SetVerticesDirty(); }
            // A private copy so the black-and-white drop can grey the violet keyline and glow.
            if (_material == null)
            {
                _sourceMaterial = Number.fontSharedMaterial;
                _material = new Material(_sourceMaterial) { name = "Aura stamp (runtime)", hideFlags = HideFlags.HideAndDontSave };
                _outlineGradient = _material.GetTexture(ShaderUtilities.ID_OutlineTex);
                Number.fontSharedMaterial = Word.fontSharedMaterial = _material;
            }
            string value = AuraFormat.Delta(amount);
            // The authored size fits "+1000"; longer numbers shrink to the same width.
            if (_numberSize < 0) _numberSize = Number.fontSize;
            float size = _numberSize * Mathf.Min(1f, 5f / Mathf.Max(1, value.Length));
            foreach (var label in new[] { Number, GhostNumberA, GhostNumberB }) { label.text = value; label.fontSize = size; }
            Number.color = amount < 0 ? LossRed : Color.white;
            _momentCount = 0;
            if (Moments != null)
            {
                var lines = new System.Text.StringBuilder();
                foreach (string moment in moments ?? System.Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(moment)) continue;
                    if (_momentCount++ > 0) lines.Append('\n');
                    lines.Append(MomentLine(moment));
                }
                Moments.text = lines.ToString();
                Moments.maxVisibleLines = 0;
                Moments.gameObject.SetActive(_momentCount > 0);
            }
            foreach (var label in new[] { Word, GhostWordA, GhostWordB }) label.text = "AURA";
            // A minus is not collected, it is taken.
            ClaimHint.text = amount < 0 ? "TAP TO CONTINUE" : "TAP TO COLLECT";
            GameAudio.Play(SoundCue.AuraStamp);
            Sample(0);
        }

        public void Sample(float time) => Pose(time, time);

        /// <param name="time">Scripted reveal time.</param>
        /// <param name="clock">Free-running clock for idle loops (equals time during the reveal).</param>
        private void Pose(float time, float clock)
        {
            float d = time - Impact, g = time - SkullStart, l = time - SkullLock;
            if (d >= 0 && !_impacted) { _impacted = true; Haptics.Heavy(); }
            if (g >= 0 && !_skullPlayed) { _skullPlayed = true; GameAudio.Play(SoundCue.AuraSkull); Haptics.Light(); }
            if (l >= 0 && !_locked) { _locked = true; Haptics.Heavy(); }
            int step = Mathf.FloorToInt(clock * 25);

            // Stamp slam.
            float scale, tilt, split, ghostAlpha, alpha;
            if (d < 0)
            {
                float u = Mathf.Clamp01(time / Impact);
                scale = Mathf.Lerp(3.6f, 1, u * u);
                tilt = Mathf.Lerp(-20, StampTilt, u);
                split = 18; ghostAlpha = .55f * u; alpha = Mathf.Clamp01(u * 3);
            }
            else
            {
                scale = 1 - .14f * Mathf.Exp(-d * 14) * Mathf.Cos(d * 38);
                tilt = StampTilt + 3 * Mathf.Exp(-d * 10) * Mathf.Sin(d * 40);
                split = 12 * Mathf.Exp(-d * 9); ghostAlpha = .85f * Mathf.Exp(-d * 6); alpha = 1;
            }

            // Skull reveal: flickers in through the glitch, then locks with a second hit.
            float glitch = 0, mono = 0, jitter = 0;
            if (g >= 0)
            {
                if (l < 0)
                {
                    float build = g / (SkullLock - SkullStart);
                    glitch = 1;
                    mono = Hash(step, 50) < .35f + build * .65f ? 1 : .2f;
                    jitter = 1;
                    split = Mathf.Max(split, (Hash(step, 51) - .5f) * 44);
                    ghostAlpha = Mathf.Max(ghostAlpha, .7f);
                }
                else
                {
                    mono = 1;
                    glitch = l < .2f ? Mathf.Exp(-l * 18) : 0;
                    jitter = glitch;
                }
            }
            if (_idle)
            {
                // Settled: only a short relapse every few seconds keeps the meme alive.
                float relapse = Mathf.Repeat(clock, 2.6f) < .12f ? 1 : 0;
                glitch = relapse * .6f;
                jitter = relapse;
            }

            Stamp.localScale = Vector3.one * scale;
            Stamp.localRotation = Quaternion.Euler(0, 0, tilt);
            Stamp.anchoredPosition = _stampHome + new Vector2((Hash(step, 52) - .5f) * 16 * jitter, 0);
            Number.alpha = Word.alpha = alpha;
            GhostA.anchoredPosition = new Vector2(-split, split * .45f);
            GhostB.anchoredPosition = new Vector2(split, -split * .45f);
            Tint(GhostNumberA, Cyan, mono, ghostAlpha); Tint(GhostWordA, Cyan, mono, ghostAlpha);
            Tint(GhostNumberB, Magenta, mono, ghostAlpha); Tint(GhostWordB, Magenta, mono, ghostAlpha);
            Word.alpha = alpha;
            if (_material != null)
            {
                // A texture cannot be greyed by tint, so black and white swaps the gradient keyline out.
                bool grey = mono > .5f;
                _material.SetTexture(ShaderUtilities.ID_OutlineTex, grey ? Texture2D.whiteTexture : _outlineGradient);
                _material.SetColor(ShaderUtilities.ID_OutlineColor, grey ? MonoOutline : Color.white);
            }

            float skullScale, skullAlpha;
            if (g < 0) { skullAlpha = 0; skullScale = 1; }
            else if (l < 0)
            {
                skullAlpha = Hash(step, 53) > .3f ? 1 : 0;
                skullScale = 1.15f + (Hash(step, 54) - .5f) * .3f;
            }
            else
            {
                skullAlpha = 1;
                skullScale = 1 + .22f * Mathf.Exp(-l * 12) * Mathf.Cos(l * 30);
                if (_idle) skullScale *= 1 + .02f * Mathf.Sin(clock * 2.4f);
            }
            SkullGroup.alpha = skullAlpha;
            Skull.anchoredPosition = _skullHome;
            Skull.localScale = Vector3.one * skullScale;
            Skull.localRotation = Quaternion.identity;
            for (int i = 0; i < SkullSlices.Length; i++)
            {
                bool torn = Hash(step * 17 + i, 55) < .4f;
                SkullSlices[i].rectTransform.anchoredPosition = new Vector2(torn ? (Hash(step * 17 + i, 56) - .5f) * 70 * jitter : 0, 0);
            }

            float shake = d < 0 ? 0 : 18 * Mathf.Exp(-d * 8);
            if (l >= 0) shake += 12 * Mathf.Exp(-l * 9);
            if (_idle) shake = 0;
            Shaker.anchoredPosition = new Vector2(Noise(clock * 55, 0), Noise(clock * 55, 7)) * shake;
            Shaker.localRotation = Quaternion.Euler(0, 0, Noise(clock * 40, 13) * shake * .07f);

            // Short: UI blends in linear space, so a lingering low alpha reads as a grey wash.
            float flash = d < 0 ? 0 : .8f * Mathf.Exp(-d * 16);
            if (l >= 0) flash = Mathf.Max(flash, .4f * Mathf.Exp(-l * 25));
            Flash.color = Grey(new Color(.93f, .82f, 1, flash), mono);
            MonoShade.color = new Color(.025f, .025f, .03f, mono);
            ClaimHint.color = Grey(new Color(.83f, .72f, 1), mono);
            ClaimHint.alpha = _idle ? .65f + .35f * Mathf.Sin(clock * 4) : Mathf.Clamp01((l - .15f) / .25f);
            if (Moments != null && _momentCount > 0)
            {
                int shown = time < MomentsStart ? 0 : Mathf.Min(_momentCount, 1 + Mathf.FloorToInt((time - MomentsStart) / MomentStep));
                if (_idle) shown = _momentCount;
                if (shown != Moments.maxVisibleLines)
                {
                    if (shown > Moments.maxVisibleLines && !_idle) Haptics.Light();
                    Moments.maxVisibleLines = shown;
                }
                Moments.color = Grey(Color.white, mono * .35f);
            }
            Fx.Sample(d, Mathf.Max(0, clock - Impact), mono, glitch);
        }

        public void Settle()
        {
            // Idle first, so the settled pose carries no leftover glitch jitter or shake.
            _idle = true; _idleTime = RevealSeconds;
            Pose(RevealSeconds, RevealSeconds);
        }

        /// <summary>The skull flies into the camera; everything else eases out right behind it.</summary>
        public void SampleClaim(float seconds)
        {
            _idle = false;
            float u = Mathf.Clamp01(seconds / .4f);
            Skull.localScale = Vector3.one * (1 + 9 * Mathf.Pow(u, 2.4f));
            Skull.localRotation = Quaternion.Euler(0, 0, 14 * u * u);
            SkullGroup.alpha = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((u - .5f) / .5f));
            foreach (var slice in SkullSlices) slice.rectTransform.anchoredPosition = Vector2.zero;

            float v = Mathf.Clamp01((seconds - .2f) / (ClaimSeconds - .2f)), ease = 1 - Mathf.Pow(1 - v, 3);
            Body.alpha = 1 - ease;
            Stamp.localScale = Vector3.one * (1 + .6f * ease);
            if (Moments != null) Moments.alpha = 1 - ease;
            ClaimHint.alpha = 1 - Mathf.Clamp01(seconds / .15f);
        }

        private void Update()
        {
            if (!_idle || !_active || ScreenLayoutRoot.IsAnyEditing) return;
            _idleTime += Time.unscaledDeltaTime;
            SampleIdle(_idleTime);
        }

        /// <summary>Settled loop (skull breathing, periodic glitch relapse) at a free-running clock.</summary>
        public void SampleIdle(float clock) => Pose(RevealSeconds, clock);

        public void ResetPresentation()
        {
            _active = _idle = false;
            if (_material != null)
            {
                Number.fontSharedMaterial = Word.fontSharedMaterial = _sourceMaterial;
                if (Application.isPlaying) Destroy(_material); else DestroyImmediate(_material);
                _material = null;
            }
            if (Root == null || !_captured) return;
            Stamp.localScale = Vector3.one;
            Stamp.localRotation = Quaternion.Euler(0, 0, StampTilt);
            Stamp.anchoredPosition = _stampHome;
            Skull.anchoredPosition = _skullHome; Skull.localScale = Vector3.one; Skull.localRotation = Quaternion.identity;
            foreach (var slice in SkullSlices) slice.rectTransform.anchoredPosition = Vector2.zero;
            Shaker.anchoredPosition = Vector2.zero; Shaker.localRotation = Quaternion.identity;
            Body.alpha = 1; SkullGroup.alpha = 0;
            if (Moments != null) { Moments.alpha = 1; Moments.maxVisibleLines = 99; }
            Fx.Sample(-1, 0);
        }

        private void OnDisable() => ResetPresentation();

        /// <summary>"VICTORY +1000" → caption, then the amount tinted green (gain) or red (loss).
        /// A line without an amount ("ROBOT UNLOCKED!") is gold.</summary>
        private static string MomentLine(string moment)
        {
            int split = moment.LastIndexOf(' ');
            string tail = split > 0 ? moment.Substring(split + 1) : "";
            if (tail.Length > 1 && (tail[0] == '+' || tail[0] == '-') && char.IsDigit(tail[1]))
                return moment.Substring(0, split) + "  <color=" + (tail[0] == '+' ? "#5CFF7A" : "#FF4D55") + ">" + tail + "</color>";
            return "<color=#FFC93C>" + moment + "</color>";
        }

        private static void Tint(TMP_Text label, Color color, float mono, float alpha)
        {
            color = Grey(color, mono); color.a = alpha;
            label.color = color;
        }

        private static Color Grey(Color color, float mono)
        {
            float grey = color.r * .3f + color.g * .59f + color.b * .11f;
            var result = Color.Lerp(color, new Color(grey, grey, grey), mono);
            result.a = color.a;
            return result;
        }

        private static float Noise(float x, float seed) => Mathf.PerlinNoise(x, seed) * 2 - 1;

        private static float Hash(int index, int salt)
        {
            uint h = (uint)(index * 374761393 + salt * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777216f;
        }
    }
}

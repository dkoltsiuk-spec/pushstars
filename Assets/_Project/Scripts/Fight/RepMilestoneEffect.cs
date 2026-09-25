using PushStars.Core;
using PushStars.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Presentation only: one ten-rep celebration per set, no score/reward writes.</summary>
    [DisallowMultipleComponent]
    public sealed class RepMilestoneEffect : MonoBehaviour
    {
        public const float Duration = 1.65f;
        private RectTransform _root, _badge;
        private CanvasGroup _group;
        private RepMilestoneGraphic _art;
        private TextMeshProUGUI _number, _caption;
        private float _elapsed;
        private bool _fired, _playing, _impact;
        public bool IsPlaying => _playing;

        public void Observe(int acceptedSetReps)
        {
            if (_fired || acceptedSetReps < 10) return;
            _fired = true;
            Play();
        }

        public void ResetSet() { Cancel(); _fired = false; }
        public void Cancel()
        {
            _playing = false;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        private void Play()
        {
            EnsureArtwork();
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _elapsed = 0;
            _impact = false;
            _playing = true;
            Sample(0);
        }

        private void EnsureArtwork()
        {
            if (_root != null) return;
            _root = Rect("TenRepCelebration", transform);
            _root.anchorMin = Vector2.zero; _root.anchorMax = Vector2.one;
            _root.sizeDelta = Vector2.zero;
            _group = _root.gameObject.AddComponent<CanvasGroup>();
            _group.interactable = false; _group.blocksRaycasts = false;
            _art = _root.gameObject.AddComponent<RepMilestoneGraphic>();
            _art.raycastTarget = false;
            _badge = Rect("MilestoneLabel", _root);
            _number = Label("Number", _badge, "10", 104, FightTypography.Role.Value);
            _number.rectTransform.anchoredPosition = new Vector2(0, 8);
            _caption = Label("Caption", _badge, "PUSH-UPS!", 22, FightTypography.Role.Heading);
            _caption.rectTransform.anchoredPosition = new Vector2(0, -66);
            _caption.color = new Color32(255, 239, 180, 255);
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.gameObject.layer = parent.gameObject.layer;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.sizeDelta = new Vector2(300, 150);
            return rect;
        }

        private static TextMeshProUGUI Label(string name, Transform parent, string text, float size, FightTypography.Role role)
        {
            var label = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text; label.fontSize = size; label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false; FightTypography.Apply(label, role);
            return label;
        }

        private void Update()
        {
            if (!_playing) return;
            _elapsed += Time.unscaledDeltaTime;
            if (!_impact && _elapsed >= .24f)
            { _impact = true; Haptics.Medium(); GameAudio.Play(SoundCue.RewardComplete); }
            Sample(_elapsed);
            if (_elapsed >= Duration) Cancel();
        }

        // The same deterministic timeline is used by the isolated editor visual check.
        public void Sample(float seconds)
        {
            if (_root == null) return;
            float unit = Mathf.Min(_root.rect.width / 390f, _root.rect.height / 700f);
            var centre = new Vector2(0, _root.rect.height * .02f);
            float enter = Mathf.Clamp01((seconds - .22f) / .34f);
            float bounce = 1f + 2.70158f * Mathf.Pow(enter - 1f, 3) + 1.70158f * Mathf.Pow(enter - 1f, 2);
            float exit = Mathf.Clamp01((seconds - 1.20f) / .45f);
            float alpha = Mathf.Clamp01((seconds - .22f) / .06f) * (1f - exit * exit);
            _badge.anchoredPosition = centre + Vector2.up * (exit * exit * 38f * unit);
            _badge.localScale = Vector3.one * (unit * Mathf.Max(.01f, bounce) * (1f - .1f * exit));
            _badge.localRotation = Quaternion.Euler(0, 0, -7 * Mathf.Sin(enter * Mathf.PI) * (1 - enter));
            _number.alpha = alpha; _caption.alpha = alpha;
            _group.alpha = seconds >= Duration ? 0 : 1;
            _art.Draw(seconds, centre, unit, bounce, exit);
        }

        private void OnDisable() => Cancel();
        private void OnDestroy()
        {
            if (_root == null) return;
            if (Application.isPlaying) Destroy(_root.gameObject); else DestroyImmediate(_root.gameObject);
        }
    }
}

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PushStars.CV;
using PushStars.CV.AntiCheat;

namespace PushStars.Fight
{
    /// <summary>
    /// The duel HUD (phase 08.9) — the product replacement for the IMGUI debug HUD in the fight
    /// flow: big player rep counter, boss counter, countdown timer, FORM readout, a guidance
    /// banner (why counting is paused), and the 3-2-1 countdown. Audio feedback carries over
    /// from the tuning HUD unchanged: the user can't watch the screen from a plank, so the
    /// 880Hz rep beep and the veto buzz stay the primary confirmation channel.
    ///
    /// All references are wired by the FightSceneSetup editor tool; this component only mutates
    /// what it's given.
    /// </summary>
    public sealed class FightHud : MonoBehaviour
    {
        public enum BannerTone { Warn, Good }

        [Header("Score row")]
        [SerializeField] private TextMeshProUGUI _playerReps;
        [SerializeField] private TextMeshProUGUI _form;
        [SerializeField] private TextMeshProUGUI _timer;
        [SerializeField] private TextMeshProUGUI _bossName;
        [SerializeField] private TextMeshProUGUI _bossReps;

        [Header("Guidance")]
        [SerializeField] private GameObject _bannerRoot;
        [SerializeField] private TextMeshProUGUI _bannerText;
        [SerializeField] private TextMeshProUGUI _countdown;

        [Header("Feedback source")]
        [SerializeField] private PushupSession _session;
        [SerializeField] private bool _sounds = true;

        private static readonly Color WarnColor = new Color(1f, 0.75f, 0.2f);
        private static readonly Color GoodColor = new Color(0.55f, 1f, 0.45f);
        private static readonly Color VetoColor = new Color(1f, 0.35f, 0.3f);

        private AudioSource _audio;
        private AudioClip _beep;
        private AudioClip _buzz;
        private float _vetoToastUntil;
        private float _goFlashUntil;
        private Vector3 _playerRepsBaseScale = Vector3.one;
        private float _playerRepsPopTime = -10f;

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _beep = MakeTone("repBeep", 880f, 0.10f);
            _buzz = MakeTone("vetoBuzz", 220f, 0.25f, thirdHarmonic: true);
            if (_playerReps != null) _playerRepsBaseScale = _playerReps.rectTransform.localScale;
        }

        private void OnEnable()
        {
            if (_session == null) return;
            _session.OnRep += HandleRep;
            _session.OnRepRejected += HandleRepRejected;
        }

        private void OnDisable()
        {
            if (_session == null) return;
            _session.OnRep -= HandleRep;
            _session.OnRepRejected -= HandleRepRejected;
        }

        private void HandleRep(int _)
        {
            if (_sounds && _beep != null) _audio.PlayOneShot(_beep);
            _playerRepsPopTime = Time.time;
        }

        private void HandleRepRejected(RepVote vote)
        {
            if (_sounds && _buzz != null) _audio.PlayOneShot(_buzz);
            // Short loud toast through the banner — the phase-14 reject UX in miniature.
            _vetoToastUntil = Time.time + 1.4f;
            SetBanner("ПОВТОР НЕ ЗАСЧИТАН", VetoColor);
        }

        private void Update()
        {
            // Rep-counter pop: quick overshoot that settles in ~0.25s.
            if (_playerReps != null)
            {
                float t = (Time.time - _playerRepsPopTime) / 0.25f;
                float k = t < 1f ? 1f + 0.35f * (1f - t) * Mathf.Sin(t * Mathf.PI) : 1f;
                _playerReps.rectTransform.localScale = _playerRepsBaseScale * k;
            }

            // "ВПЕРЁД!" flash auto-hides.
            if (_goFlashUntil > 0f && Time.time >= _goFlashUntil)
            {
                _goFlashUntil = 0f;
                if (_countdown != null) _countdown.gameObject.SetActive(false);
            }
        }

        // ── Score row ────────────────────────────────────────────────────────────────────────────
        public void SetBossName(string name) { if (_bossName != null) _bossName.text = name; }
        public void SetPlayerReps(int reps)  { if (_playerReps != null) _playerReps.text = reps.ToString(); }
        public void SetBossReps(int reps)    { if (_bossReps != null) _bossReps.text = reps.ToString(); }

        public void SetTimer(int seconds)
        {
            if (_timer == null) return;
            _timer.text = $"{seconds / 60}:{seconds % 60:00}";
            _timer.color = seconds <= 10 ? VetoColor : Color.white;
        }

        public void SetForm(float form)
        {
            if (_form != null) _form.text = $"FORM {form:0}";
        }

        // ── Guidance banner ──────────────────────────────────────────────────────────────────────
        public void ShowBanner(string text, BannerTone tone)
        {
            // A live veto toast wins over the ambient hint until it expires.
            if (Time.time < _vetoToastUntil) return;
            SetBanner(text, tone == BannerTone.Good ? GoodColor : WarnColor);
        }

        public void HideBanner()
        {
            if (Time.time < _vetoToastUntil) return;
            if (_bannerRoot != null) _bannerRoot.SetActive(false);
        }

        private void SetBanner(string text, Color color)
        {
            if (_bannerRoot == null || _bannerText == null) return;
            _bannerRoot.SetActive(true);
            _bannerText.text = text;
            _bannerText.color = color;
        }

        // ── Countdown ────────────────────────────────────────────────────────────────────────────
        public void ShowCountdown(string text)
        {
            if (_countdown == null) return;
            _goFlashUntil = 0f;
            _countdown.gameObject.SetActive(true);
            _countdown.text = text;
        }

        public void FlashGo()
        {
            if (_countdown == null) return;
            _countdown.gameObject.SetActive(true);
            _countdown.text = "ВПЕРЁД!";
            _goFlashUntil = Time.time + 0.8f;
            if (_sounds && _beep != null) _audio.PlayOneShot(_beep);
        }

        public void HideCountdown()
        {
            _goFlashUntil = 0f;
            if (_countdown != null) _countdown.gameObject.SetActive(false);
        }

        // ── Procedural clips (same recipe as the debug HUD — no audio assets) ────────────────────
        private static AudioClip MakeTone(string name, float freq, float dur, bool thirdHarmonic = false)
        {
            const int rate = 44100;
            int n = (int)(rate * dur);
            var samples = new float[n];
            for (int i = 0; i < n; i++)
            {
                float time = (float)i / rate;
                float attack = Mathf.Clamp01(i / (rate * 0.004f));
                float decay = Mathf.Clamp01((n - i) / (rate * 0.05f));
                float wave = Mathf.Sin(2f * Mathf.PI * freq * time);
                if (thirdHarmonic) wave += 0.35f * Mathf.Sin(2f * Mathf.PI * freq * 3f * time);
                samples[i] = wave * 0.45f * attack * decay;
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}

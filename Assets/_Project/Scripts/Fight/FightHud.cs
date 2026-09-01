using UnityEngine;
using TMPro;
using PushStars.CV;
using PushStars.CV.AntiCheat;

namespace PushStars.Fight
{
    /// <summary>
    /// The duel HUD: the screen is split between the two fighters — opponent above, player below —
    /// and each half carries its own rep count, FORM and tempo over its own body. The timer sits on
    /// the seam between them, where both can be read without moving your eyes off either.
    ///
    /// <para>A level test has no opponent, so <see cref="ConfigureSolo"/> hides that half entirely
    /// rather than parking a zero there for a minute.</para>
    ///
    /// <para>Audio feedback carries over from the tuning HUD unchanged: the user cannot watch the
    /// screen from a plank, so the 880Hz rep beep and the veto buzz stay the primary channel.</para>
    ///
    /// All references are wired by the FightSceneSetup editor tool; this component only mutates
    /// what it's given.
    /// </summary>
    public sealed class FightHud : MonoBehaviour
    {
        public enum BannerTone { Warn, Good }

        [Header("Opponent half (top)")]
        [SerializeField] private GameObject _opponentPanel;
        [SerializeField] private TextMeshProUGUI _opponentName;
        [SerializeField] private TextMeshProUGUI _opponentReps;
        [SerializeField] private TextMeshProUGUI _opponentForm;
        [SerializeField] private TextMeshProUGUI _opponentTempo;

        [Header("Player half (bottom)")]
        [SerializeField] private GameObject _playerPanel;
        [SerializeField] private TextMeshProUGUI _playerName;
        [SerializeField] private TextMeshProUGUI _playerReps;
        [SerializeField] private TextMeshProUGUI _playerForm;
        [SerializeField] private TextMeshProUGUI _playerTempo;

        [Header("Shared")]
        [SerializeField] private TextMeshProUGUI _timer;
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
        /// <summary>Whether this set has an opponent at all. A level test must not get
        /// its empty half back when the scoreboards are re-shown.</summary>
        private bool _showOpponent = true;

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

        // ── Layout modes ─────────────────────────────────────────────────────────────────────────

        /// <summary>Two fighters, two counters.</summary>
        public void ConfigureDuel(string opponentName, string playerName)
        {
            _showOpponent = true;
            if (_opponentPanel != null) _opponentPanel.SetActive(true);
            SetText(_opponentName, opponentName);
            SetText(_opponentReps, "0");
            SetText(_playerName, playerName);
            SetText(_playerReps, "0");
        }

        /// <summary>Level test: there is no opponent, so the whole upper half goes rather than
        /// standing empty for a minute. The caption says what the screen is instead.</summary>
        public void ConfigureSolo(string caption)
        {
            _showOpponent = false;
            if (_opponentPanel != null) _opponentPanel.SetActive(false);
            SetText(_playerName, caption);
            SetText(_playerReps, "0");
        }

        /// <summary>Hides both scoreboards while the ready card is up. The card shows the same
        /// fighters from the same angle, and a row of zeros behind it reads as a duel already in
        /// progress and lost.</summary>
        public void SetScoresVisible(bool visible)
        {
            if (_opponentPanel != null) _opponentPanel.SetActive(visible && _showOpponent);
            if (_playerPanel != null) _playerPanel.SetActive(visible);
        }

        // ── Live values ──────────────────────────────────────────────────────────────────────────

        public void SetPlayerReps(int reps) => SetText(_playerReps, reps.ToString());
        public void SetOpponentReps(int reps) => SetText(_opponentReps, reps.ToString());

        public void SetPlayerForm(float form) => SetText(_playerForm, $"{form:0}");
        public void SetOpponentForm(float form) => SetText(_opponentForm, $"{form:0}");

        /// <summary>Seconds per rep, which is how a lifter thinks about pace — reps per minute is a
        /// number you have to divide before it means anything mid-set.</summary>
        public void SetPlayerTempo(float repsPerMinute) => SetText(_playerTempo, TempoText(repsPerMinute));
        public void SetOpponentTempo(float secondsPerRep) =>
            SetText(_opponentTempo, secondsPerRep > 0.01f ? $"{secondsPerRep:0.0}с" : "—");

        private static string TempoText(float repsPerMinute)
            => repsPerMinute > 0.01f ? $"{60f / repsPerMinute:0.0}с" : "—";

        public void SetTimer(int seconds)
        {
            if (_timer == null) return;
            _timer.text = $"{seconds / 60}:{seconds % 60:00}";
            _timer.color = seconds <= 10 ? VetoColor : Color.white;
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

        private static void SetText(TextMeshProUGUI label, string text)
        {
            if (label != null) label.text = text;
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

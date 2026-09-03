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
    /// <para>A level test is not that screen with one half blanked — <see cref="ConfigureSolo"/>
    /// switches to a layout of its own, where the count is the middle of the screen instead of one
    /// side of a comparison. Both layouts show the same four live numbers, so the setters write to
    /// whichever one is up and no caller has to know which screen it is on.</para>
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

        [Header("Level test (solo)")]
        [Tooltip("The whole solo layout: its own top bar, one centred counter, its own clock.")]
        [SerializeField] private GameObject _soloPanel;
        [SerializeField] private TextMeshProUGUI _soloCaption;
        [SerializeField] private TextMeshProUGUI _soloReps;
        [SerializeField] private TextMeshProUGUI _soloForm;
        [SerializeField] private TextMeshProUGUI _soloTempo;
        [SerializeField] private TextMeshProUGUI _soloTimer;
        [SerializeField] private GameObject _soloPauseOverlay;

        [Tooltip("The dark bolts that close in on the corners once the set is live.")]
        [SerializeField] private CornerAccents _soloCorners;

        [Tooltip("The player's band of the screen. A duel gives it the bottom half; a level test " +
                 "has the screen to itself and stands the body in the middle of it.")]
        [SerializeField] private RectTransform _playerHalf;

        [Tooltip("The opponent's band, and the 3D stage that renders into it. A level test has no " +
                 "opponent, so both go — the stage as well as the picture, or a camera nobody can " +
                 "see keeps rendering a body nobody is fighting.")]
        [SerializeField] private GameObject _opponentHalf;
        [SerializeField] private GameObject _opponentStage;

        [Tooltip("The duel's clock, on the seam between the two fighters. The level test has its " +
                 "own down by the button that ends the set.")]
        [SerializeField] private GameObject _timerPlate;

        [Tooltip("Where the guidance banner sits in a level test, above the safe-area bottom. The " +
                 "duel parks it in the player's half, which in this layout is across the body.")]
        [SerializeField] private float _soloBannerY = 96f;

        [Tooltip("Anchors the player's band takes in a level test — the same shape, moved up, so " +
                 "the render texture's aspect still matches its rect and the body is not stretched.")]
        [SerializeField] private Vector2 _soloHalfAnchorY = new Vector2(0.17f, 0.65f);

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

        /// <summary>Whether the solo layout is the one on screen. The two layouts show the same
        /// four live numbers in different places, so the setters write to whichever set is up
        /// rather than every caller having to know which screen it is on.</summary>
        private bool _solo;
        private TextMeshProUGUI _repsOut, _formOut, _tempoOut, _timerOut;

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _beep = MakeTone("repBeep", 880f, 0.10f);
            _buzz = MakeTone("vetoBuzz", 220f, 0.25f, thirdHarmonic: true);

            if (_bannerRoot != null && _bannerRoot.transform is RectTransform bannerRect)
                _duelBannerY = bannerRect.anchoredPosition.y;

            // The duel layout until told otherwise — the scene opens on it, and a mode that never
            // configures anything still gets working labels rather than four silent nulls.
            UseDuelLabels();
        }

        /// <summary>Where the banner sat when the scene was built — the duel's place for it, kept
        /// so switching back to a duel restores it without a second number to keep in step.</summary>
        private float _duelBannerY;

        private void SetBannerY(float y)
        {
            if (_bannerRoot == null) return;
            if (_bannerRoot.transform is RectTransform rect)
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
        }

        private void UseDuelLabels()
        {
            _repsOut = _playerReps;
            _formOut = _playerForm;
            _tempoOut = _playerTempo;
            _timerOut = _timer;
            if (_repsOut != null) _playerRepsBaseScale = _repsOut.rectTransform.localScale;
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
            if (_repsOut != null)
            {
                float t = (Time.time - _playerRepsPopTime) / 0.25f;
                float k = t < 1f ? 1f + 0.35f * (1f - t) * Mathf.Sin(t * Mathf.PI) : 1f;
                _repsOut.rectTransform.localScale = _playerRepsBaseScale * k;
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
            _solo = false;
            _showOpponent = true;
            UseDuelLabels();
            if (_soloPanel != null) _soloPanel.SetActive(false);
            if (_soloCorners != null) _soloCorners.gameObject.SetActive(false);
            if (_opponentPanel != null) _opponentPanel.SetActive(true);
            if (_playerPanel != null) _playerPanel.SetActive(true);
            if (_opponentHalf != null) _opponentHalf.SetActive(true);
            if (_opponentStage != null) _opponentStage.SetActive(true);
            if (_timerPlate != null) _timerPlate.SetActive(true);
            if (_bannerRoot != null) SetBannerY(_duelBannerY);
            SetText(_opponentName, opponentName);
            SetText(_opponentReps, "0");
            SetText(_playerName, playerName);
            SetText(_playerReps, "0");
        }

        /// <summary>
        /// Level test: not a duel with one half blanked, but its own screen.
        ///
        /// <para>There is no opponent to compare against, so nothing is a scoreboard — the count is
        /// the whole point and it goes in the middle, big, with the pace and the technique reading
        /// either side of it and the clock down by the button that ends the set. The duel layout's
        /// two bands, its seam clock and its name badges all go; the body moves up out of the
        /// bottom band into the middle of the screen it now has to itself.</para>
        /// </summary>
        public void ConfigureSolo(string caption)
        {
            _solo = true;
            _showOpponent = false;
            if (_opponentPanel != null) _opponentPanel.SetActive(false);
            if (_playerPanel != null) _playerPanel.SetActive(false);

            // There is no second fighter, so nothing of one is left standing: not the band, not
            // the stage rendering into it, not the clock that sat on the seam between them.
            if (_opponentHalf != null) _opponentHalf.SetActive(false);
            if (_opponentStage != null) _opponentStage.SetActive(false);
            if (_timerPlate != null) _timerPlate.SetActive(false);
            SetBannerY(_soloBannerY);
            if (_soloPanel != null) _soloPanel.SetActive(true);
            // On, but still off-screen at zero alpha: they arrive on the cue below, not with the
            // page. CornerAccents parks them there in Awake.
            if (_soloCorners != null) _soloCorners.gameObject.SetActive(true);
            SetPaused(false);

            _repsOut = _soloReps;
            _formOut = _soloForm;
            _tempoOut = _soloTempo;
            _timerOut = _soloTimer;
            if (_repsOut != null) _playerRepsBaseScale = _repsOut.rectTransform.localScale;

            SetText(_soloCaption, caption);
            SetText(_repsOut, "0");
            SetText(_formOut, "—");
            SetText(_tempoOut, "—");

            // Same rect shape as the duel band, moved up: the stage's render texture was sized to
            // that aspect, and RawImage maps texture onto rect per-axis with no aspect option, so
            // a rect of a different shape here would quietly squash the body.
            if (_playerHalf != null)
            {
                _playerHalf.anchorMin = new Vector2(0f, _soloHalfAnchorY.x);
                _playerHalf.anchorMax = new Vector2(1f, _soloHalfAnchorY.y);
                _playerHalf.offsetMin = Vector2.zero;
                _playerHalf.offsetMax = Vector2.zero;
            }
        }

        /// <summary>Hides the scoreboards while the ready card is up. The card shows the same
        /// fighters from the same angle, and a row of zeros behind it reads as a duel already in
        /// progress and lost.</summary>
        public void SetScoresVisible(bool visible)
        {
            if (_solo)
            {
                if (_soloPanel != null) _soloPanel.SetActive(visible);
                return;
            }

            if (_opponentPanel != null) _opponentPanel.SetActive(visible && _showOpponent);
            if (_playerPanel != null) _playerPanel.SetActive(visible);
        }

        /// <summary>The cue the corner bolts come in on: the clock has started, and the thing being
        /// measured is now the thing you are doing. Nothing else on this screen says so.</summary>
        public void PlayCornerAccents()
        {
            if (_soloCorners != null) _soloCorners.Play();
        }

        /// <summary>Curtain over the level test while it is paused. It covers the body on purpose:
        /// a pause that leaves the set visible and the clock stopped is somewhere to practise.</summary>
        public void SetPaused(bool paused)
        {
            if (_soloPauseOverlay != null) _soloPauseOverlay.SetActive(paused);
        }

        // ── Live values ──────────────────────────────────────────────────────────────────────────

        public void SetPlayerReps(int reps) => SetText(_repsOut, reps.ToString());
        public void SetOpponentReps(int reps) => SetText(_opponentReps, reps.ToString());

        /// <summary>Technique, 0–100. The level test spells it as a percentage and colours it —
        /// it is the one number on that screen a player can still act on mid-set.</summary>
        public void SetPlayerForm(float form)
        {
            SetText(_formOut, _solo ? $"{form:0}%" : $"{form:0}");
            if (_solo && _formOut != null)
                _formOut.color = form >= 85f ? GoodColor : form >= 70f ? WarnColor : VetoColor;
        }

        public void SetOpponentForm(float form) => SetText(_opponentForm, $"{form:0}");

        /// <summary>Seconds per rep, which is how a lifter thinks about pace — reps per minute is a
        /// number you have to divide before it means anything mid-set.</summary>
        public void SetPlayerTempo(float repsPerMinute)
            => SetText(_tempoOut, repsPerMinute > 0.01f
                                  ? $"{60f / repsPerMinute:0.0}{(_solo ? "s" : "с")}"
                                  : "—");

        public void SetOpponentTempo(float secondsPerRep) =>
            SetText(_opponentTempo, secondsPerRep > 0.01f ? $"{secondsPerRep:0.0}с" : "—");

        public void SetTimer(int seconds)
        {
            if (_timerOut == null) return;
            // Padded on the level test, where the clock is the only number moving and a line that
            // changes width every ten seconds reads as a glitch; bare in a duel, where it sits on
            // the seam and every character costs room.
            _timerOut.text = _solo ? $"{seconds / 60:00}:{seconds % 60:00}"
                                   : $"{seconds / 60}:{seconds % 60:00}";
            _timerOut.color = seconds <= 10 ? VetoColor : Color.white;
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

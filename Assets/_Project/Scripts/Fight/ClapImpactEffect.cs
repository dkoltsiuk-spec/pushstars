using PushStars.Core;
using PushStars.UI;
using TMPro;
using UnityEngine;

namespace PushStars.Fight
{
    /// <summary>
    /// A clap push-up in a duel, made to land. Two beats, because the detector gives two:
    /// <see cref="Takeoff"/> when the hands leave the floor (air drawn in, the music drops, the
    /// player's half tightens) and <see cref="Land"/> when the clap is confirmed on the way down —
    /// the hit itself, sounded at once so it meets the player's own palms striking the floor.
    /// The character is a few frames behind the player by design, so the flash goes with the
    /// sound and the wave waits for the character's palms. That wave rolls into the opponent's
    /// half and shakes it: a duel has no health bar, so this is
    /// what makes the clap read as a blow. The Aura the clap is worth is shown here, on the
    /// spot, instead of only on the result screen.
    ///
    /// <para>Each clap of the fight plays one note higher; the last one Aura pays for is the
    /// finisher. Presentation only — no score or reward is written here.</para>
    ///
    /// <para>No haptics on purpose: the phone is propped on the floor watching the player, and a
    /// buzz there shakes the camera the rep counter is reading.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ClapImpactEffect : MonoBehaviour
    {
        /// <summary>Palms planted to a clean screen. Shorter than the fastest clap-to-clap cycle.</summary>
        public const float Duration = .95f;
        private const float AnticipationInSec = .1f, AnticipationOutSec = .18f;
        /// <summary>A flight that never confirms a clap (a lift without one) lets go after this.</summary>
        private const float AnticipationHoldSec = .6f;
        private const float StampAtSec = .05f, AuraAtSec = .17f, FadeAtSec = .62f;
        private const float RiserVolume = .5f, ImpactVolume = .9f, FinisherVolume = 1f;
        private static readonly Color AuraTint = new Color(.9f, .8f, 1f);

        private FightHud _hud;
        private RectTransform _root, _stampRect, _auraRect;
        private ClapImpactGraphic _art;
        private TextMeshProUGUI _stamp, _aura;
        private AudioClip _riser, _finisherClip;
        private AudioClip[] _impacts;

        // The live HUD pieces the hit moves, and where it found them.
        private RectTransform _player, _opponent, _clock;
        private Vector2 _playerHome, _opponentHome;
        private Vector3 _playerScale, _clockScale;
        private Rect _playerArea, _opponentArea;
        private Vector2 _origin, _target;
        private float _unit = 1f, _hitAt;
        private bool _held;

        private float _takeoffAt = -100f, _landAt = -100f, _plantIn;
        private bool _finisher, _manual;

        /// <summary>Builds the artwork and loads the clips now, at the start of the fight: the
        /// takeoff beat is a quarter of a second long and cannot afford a first-use hitch.</summary>
        public void Bind(FightHud hud)
        {
            _hud = hud;
            EnsureArtwork();
            EnsureClips();
        }

        /// <summary>The hands left the floor off a push. May never be followed by a landing.</summary>
        public void Takeoff()
        {
            if (_hud == null || Time.unscaledTime - _landAt < .3f) return;
            _manual = false;
            _takeoffAt = Time.unscaledTime;
            Hold();
            EnsureClips();
            GameAudio.PlayClip(_riser, RiserVolume);
        }

        /// <summary>A clap was confirmed on landing.</summary>
        /// <param name="clapNumber">Which clap of this fight, from 1.</param>
        /// <param name="paidClaps">How many claps a fight pays Aura for; that one is the finisher.</param>
        /// <param name="aura">Aura this clap adds, 0 once the fight's cap is spent.</param>
        /// <param name="plantIn">Seconds until the character's palms are back on the floor.</param>
        public void Land(int clapNumber, int paidClaps, long aura, float plantIn)
        {
            if (_hud == null) return;
            _manual = false;
            Stage(clapNumber, paidClaps, aura);
            _plantIn = Mathf.Clamp(plantIn, 0f, .2f);
            _takeoffAt = -100f;
            _landAt = Time.unscaledTime;
            EnsureClips();
            if (_finisher) GameAudio.PlayClip(_finisherClip, FinisherVolume);
            else GameAudio.PlayClip(_impacts[Mathf.Clamp(clapNumber, 1, _impacts.Length) - 1], ImpactVolume);
            Sample(1f, 0f);
        }

        /// <summary>One frame of the timeline with no clock and no sound — the editor's frame
        /// capture steps through the effect with this. <paramref name="age"/> is seconds since
        /// the clap was confirmed, negative before it.</summary>
        public void Preview(int clapNumber, int paidClaps, long aura, float anticipation, float age, float plantIn)
        {
            _manual = true;
            Stage(clapNumber, paidClaps, aura);
            _plantIn = plantIn;
            Sample(age >= 0f && age < plantIn ? 1f : anticipation, age);
        }

        public void Cancel()
        {
            _takeoffAt = _landAt = -100f;
            Release();
        }

        private void Stage(int clapNumber, int paidClaps, long aura)
        {
            Hold();
            _finisher = clapNumber == paidClaps;
            _stamp.text = clapNumber <= 1 ? "CLAP!" : "CLAP ×" + clapNumber;
            _aura.text = _finisher ? $"+{aura}  MAX AURA" : aura > 0 ? $"+{aura} AURA" : "";
        }

        private void Update()
        {
            if (_manual || !_held) return;
            float now = Time.unscaledTime, age = now - _landAt, flight = now - _takeoffAt;
            // Confirmed but still airborne: the half stays leaned in until the palms plant.
            if (age < _plantIn + Duration) { Sample(age < _plantIn ? 1f : 0f, age); return; }
            if (flight < AnticipationHoldSec + AnticipationOutSec)
            {
                float a = Mathf.Clamp01(flight / AnticipationInSec)
                          * (1f - Mathf.Clamp01((flight - AnticipationHoldSec) / AnticipationOutSec));
                Sample(a, -1f);
                return;
            }
            Release();
        }

        /// <summary>The whole effect as a function of two numbers, so a captured frame and a live
        /// one cannot disagree. <paramref name="confirmed"/> is seconds since the clap was
        /// confirmed; everything but the flash runs off the palms planting, a moment later.</summary>
        private void Sample(float anticipation, float confirmed)
        {
            if (!_held) return;
            float age = confirmed >= 0f ? confirmed - _plantIn : -1f;
            bool landed = age >= 0f;
            // Player's half: leans in while airborne, kicks and rattles on the hit.
            float kick = landed && age < .26f ? Sq(1f - age / .26f) : 0f;
            _player.localScale = _playerScale * (1f + .028f * anticipation + .05f * kick);
            _player.anchoredPosition = _playerHome + (landed ? Rattle(age, .3f, 10f, 0f) : Vector2.zero);

            float since = age - _hitAt;
            if (_opponent != null)
                _opponent.anchoredPosition = _opponentHome + (landed ? Rattle(since, ClapImpactGraphic.HitSec, _finisher ? 16f : 11f, 1.7f) : Vector2.zero);
            if (_clock != null)
            {
                float u = landed ? since / .3f : -1f;
                _clock.localScale = _clockScale * (u > 0f && u < 1f ? 1f + .24f * Mathf.Sin(Mathf.PI * u) * (1f - u) : 1f);
            }

            StampLabel(_stampRect, _stamp, age - StampAtSec, age, 0f, _finisher ? 1.12f : 1f);
            StampLabel(_auraRect, _aura, age - AuraAtSec, age, -10f, 1f);
            _art.Draw(anticipation, confirmed, age, _finisher, _unit, _hitAt, _origin, _target, _playerArea, _opponentArea);
        }

        /// <summary>Slammed in oversized, settled, then lifted away.</summary>
        private void StampLabel(RectTransform rect, TextMeshProUGUI label, float since, float age, float fromY, float size)
        {
            bool on = since >= 0f && age < Duration && label.text.Length > 0;
            if (label.gameObject.activeSelf != on) label.gameObject.SetActive(on);
            if (!on) return;
            float pop = since < .09f ? Mathf.Lerp(1.7f, .9f, since / .09f) : Mathf.Lerp(.9f, 1f, Mathf.Clamp01((since - .09f) / .08f));
            float fade = Mathf.Clamp01((age - FadeAtSec) / (Duration - FadeAtSec));
            rect.localScale = Vector3.one * (pop * size * _unit);
            rect.anchoredPosition = HomeOf(rect) + Vector2.up * ((fromY * (1f - Mathf.Clamp01(since / .12f)) + 16f * fade * fade) * _unit);
            label.alpha = Mathf.Clamp01(since / .03f) * (1f - fade);
        }

        private Vector2 HomeOf(RectTransform rect)
            => new Vector2(_origin.x, _playerArea.yMin + (rect == _stampRect ? 90f : 42f) * _unit);

        /// <summary>A decaying two-axis shake; zero outside its window, so the piece ends exactly home.</summary>
        private Vector2 Rattle(float since, float seconds, float amplitude, float phase)
        {
            if (since < 0f || since >= seconds) return Vector2.zero;
            float decay = Sq(1f - since / seconds) * amplitude * _unit;
            return new Vector2(Mathf.Sin(since * 96f + phase), Mathf.Cos(since * 71f + phase * 2f) * .7f) * decay;
        }

        private static float Sq(float x) => x * x;

        // ── Holding and letting go of the HUD ────────────────────────────────────────────────────

        /// <summary>Takes the HUD pieces where they stand right now. Measured once per play, before
        /// anything is moved, so a clap that lands while the last one still rings does not adopt a
        /// shaken position as home.</summary>
        private void Hold()
        {
            EnsureArtwork();
            if (_held) return;
            _hud.ClapImpactTargets(out _player, out _opponent, out _clock);
            if (_player == null) return;
            _held = true;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _unit = _root.rect.width / 390f;
            _playerHome = _player.anchoredPosition; _playerScale = _player.localScale;
            if (_opponent != null) _opponentHome = _opponent.anchoredPosition;
            if (_clock != null) _clockScale = _clock.localScale;

            _playerArea = Area(_player);
            _opponentArea = _opponent != null ? Area(_opponent) : default;
            // Palms and the opponent's chest, from the same mockup numbers that frame the bodies.
            _origin = new Vector2(_playerArea.center.x, _playerArea.yMin + (FightHud.DuelPlayerFloor + 8f) * _unit);
            _target = new Vector2(_opponentArea.center.x, _opponentArea.yMin + (FightHud.DuelOpponentFloor + 80f) * _unit);
            _hitAt = _opponent != null ? ClapImpactGraphic.FrontReachSec(_playerArea.yMax - _origin.y, _unit) : float.PositiveInfinity;
        }

        private void Release()
        {
            if (!_held) return;
            _held = false;
            if (_player != null) { _player.anchoredPosition = _playerHome; _player.localScale = _playerScale; }
            if (_opponent != null) _opponent.anchoredPosition = _opponentHome;
            if (_clock != null) _clock.localScale = _clockScale;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        private Rect Area(RectTransform target)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 min = _root.InverseTransformPoint(corners[0]), max = _root.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void EnsureArtwork()
        {
            if (_root != null) return;
            _root = Child("ClapImpact", transform);
            _root.anchorMin = Vector2.zero; _root.anchorMax = Vector2.one;
            _root.sizeDelta = Vector2.zero;
            var group = _root.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false; group.blocksRaycasts = false;
            _art = _root.gameObject.AddComponent<ClapImpactGraphic>();
            _stamp = Label("Stamp", "CLAP!", 58, FightTypography.Role.Title, Color.white);
            _stampRect = _stamp.rectTransform;
            _stampRect.localRotation = Quaternion.Euler(0, 0, 5);
            _aura = Label("Aura", "", 30, FightTypography.Role.Heading, AuraTint);
            _auraRect = _aura.rectTransform;
            _root.gameObject.SetActive(false);
        }

        private void EnsureClips()
        {
            if (_impacts != null) return;
            _riser = Resources.Load<AudioClip>(GameAudio.ResourceFolder + "clap_riser");
            _finisherClip = Resources.Load<AudioClip>(GameAudio.ResourceFolder + "clap_max");
            _impacts = new AudioClip[4];
            for (int i = 0; i < _impacts.Length; i++)
                _impacts[i] = Resources.Load<AudioClip>(GameAudio.ResourceFolder + "clap_impact_" + (i + 1));
        }

        private static RectTransform Child(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.gameObject.layer = parent.gameObject.layer;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.sizeDelta = new Vector2(380, 80);
            return rect;
        }

        private TextMeshProUGUI Label(string name, string text, float size, FightTypography.Role role, Color color)
        {
            var label = Child(name, _root).gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text; label.fontSize = size; label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap; label.raycastTarget = false;
            FightTypography.Apply(label, role);
            label.color = color;
            label.gameObject.SetActive(false);
            return label;
        }

        private void OnDisable() => Cancel();
        private void OnDestroy()
        {
            if (_root == null) return;
            if (Application.isPlaying) Destroy(_root.gameObject); else DestroyImmediate(_root.gameObject);
        }
    }
}

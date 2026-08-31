using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Owns which body stands on the <see cref="CharacterStage"/> and lets the player flip between
    /// the male and the female figure. The choice survives restarts.
    ///
    /// <para><b>Why the roster owns the model rather than the scene.</b> The stage is built once by
    /// the editor tool with one character already parented under its AvatarRoot — that is what makes
    /// the scene look right in edit mode, before anything runs. At runtime the saved choice may be
    /// the other one, so the roster re-seats the stage from its own prefabs on Start regardless of
    /// what the scene was authored with. One code path builds the character, whichever body it
    /// is, and the swap is then the same operation as the first load.</para>
    ///
    /// <para>Everything around the character — camera framing, render texture, the turntable's
    /// swipe, the UI drawn on top — hangs off the stage and its AvatarRoot, never off the model,
    /// so none of it moves when the body changes. The import tool sizes both prefabs to the same
    /// 1.80 m, which is what keeps the shot identical across the flip.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterRoster : MonoBehaviour
    {
        /// <summary>Kept beside the "settings.*" keys the settings store writes. Appearance is not
        /// a settings-screen toggle, so it is not routed through ISettingsStore — one key, owned
        /// here, is the whole persistence story.</summary>
        private const string PrefsKey = "character.gender";

        [Header("Stage")]
        [Tooltip("Stage the character is seated on. Found on this GameObject when left empty.")]
        [SerializeField] private CharacterStage _stage;

        [Header("Bodies")]
        [Tooltip("MainMan.prefab — imported by Tools ▸ Push Stars ▸ Character ▸ Import Main Characters.")]
        [SerializeField] private GameObject _malePrefab;

        [Tooltip("MainWoman.prefab — same import tool, same retargeted clips.")]
        [SerializeField] private GameObject _femalePrefab;

        [Header("Animation")]
        [Tooltip("Idle state both controllers rest in.")]
        [SerializeField] private string _idleState = "Idle";

        [Tooltip("One-shot break the idle accent schedules. Empty disables the accent.")]
        [SerializeField] private string _accentState = "WarriorIdle";

        [Header("Switch UI (optional)")]
        [Tooltip("Button that flips the body. Wired here so the label below can follow it.")]
        [SerializeField] private Button _switchButton;

        [Tooltip("Label showing the body currently on the stage.")]
        [SerializeField] private TextMeshProUGUI _switchLabel;

        [SerializeField] private string _maleLabel   = "М";
        [SerializeField] private string _femaleLabel = "Ж";

        private GameObject _current;

        /// <summary>The body on the stage right now.</summary>
        public CharacterGender Gender { get; private set; }

        /// <summary>Raised after the new body is on the stage. Anything that has to re-acquire the
        /// character's Animator — a duel intro, a wardrobe preview — listens here rather than
        /// caching the model across a swap.</summary>
        public event Action<CharacterGender> GenderChanged;

        private void Awake()
        {
            if (_stage == null) _stage = GetComponent<CharacterStage>();
            if (_stage == null) _stage = GetComponentInChildren<CharacterStage>();

            Gender = Load();
            if (_switchButton != null) _switchButton.onClick.AddListener(Toggle);
        }

        // Start, not Awake: CharacterStage builds its render texture in Awake, and the character
        // has to land on a stage that is already wired to the UI surface showing it.
        private void Start() => Apply(Gender);

        private void OnDestroy()
        {
            if (_switchButton != null) _switchButton.onClick.RemoveListener(Toggle);
        }

        /// <summary>Flips to the other body. Hooked to the switch button; safe to call from
        /// anywhere else (a profile screen, a debug key).</summary>
        public void Toggle()
            => SetGender(Gender == CharacterGender.Male ? CharacterGender.Female : CharacterGender.Male);

        /// <summary>Puts a specific body on the stage and remembers it. A no-op when that body is
        /// already standing there, so a UI that re-asserts its state does not rebuild the model.</summary>
        public void SetGender(CharacterGender gender)
        {
            if (gender == Gender && _current != null) return;

            Gender = gender;
            Save(gender);
            Apply(gender);
        }

        /// <summary>The prefab for a body, falling back to the other one when that character is not
        /// in the project. A half-imported cast is the normal state while a model is being
        /// re-exported, and an empty stage says nothing about why — the other body standing there
        /// is both a better screen and a clearer signal.</summary>
        public GameObject PrefabFor(CharacterGender gender)
        {
            var wanted = gender == CharacterGender.Female ? _femalePrefab : _malePrefab;
            if (wanted != null) return wanted;

            return gender == CharacterGender.Female ? _malePrefab : _femalePrefab;
        }

        private void Apply(CharacterGender gender)
        {
            RefreshSwitchLabel();

            var prefab = PrefabFor(gender);
            if (prefab == null)
            {
                // Neither body is in the project: leave whatever the scene was built with standing
                // rather than emptying the stage.
                Debug.LogWarning("[CharacterRoster] No character prefabs assigned — " +
                                 "run Tools ▸ Push Stars ▸ Character ▸ Import Main Characters, " +
                                 "then rebuild the Main VS screen.");
                return;
            }
            if (_stage == null)
            {
                Debug.LogWarning("[CharacterRoster] No CharacterStage assigned — nothing to seat the character on.");
                return;
            }

            _current = Instantiate(prefab);
            _current.name = prefab.name;
            _stage.SetAvatar(_current);   // re-parents, re-centres, moves onto the stage's layer

            AttachIdleBehaviour(_current);
            GenderChanged?.Invoke(gender);
        }

        /// <summary>Both controllers carry the same state names, so the idle behaviour is
        /// configured on the fresh model instead of being carried across from the old one — these
        /// components belong to the body they animate, and the body is replaced whole.</summary>
        private void AttachIdleBehaviour(GameObject character)
        {
            var animator = character.GetComponentInChildren<Animator>();
            if (animator == null) return;

            if (string.IsNullOrEmpty(_accentState)) return;

            var accent = character.GetComponent<CharacterIdleAccent>();
            if (accent == null) accent = character.AddComponent<CharacterIdleAccent>();
            accent.Configure(animator, _idleState, _accentState);
        }

        private void RefreshSwitchLabel()
        {
            if (_switchLabel == null) return;
            _switchLabel.text = Gender == CharacterGender.Female ? _femaleLabel : _maleLabel;
        }

        /// <summary>The saved body, readable without a roster in the scene. The fight screen builds
        /// its own character (it needs a push-up-capable controller the menu stage has no use for)
        /// and still has to put the same body on screen the player picked in the menu.</summary>
        public static CharacterGender SavedGender => Load();

        /// <summary>Records the choice from outside a roster — the onboarding gender picker runs
        /// in its own scene, before any stage exists.</summary>
        public static void SaveGender(CharacterGender gender) => Save(gender);

        private static CharacterGender Load()
            => PlayerPrefs.GetInt(PrefsKey, (int)CharacterGender.Male) == (int)CharacterGender.Female
                ? CharacterGender.Female
                : CharacterGender.Male;

        private static void Save(CharacterGender gender)
        {
            PlayerPrefs.SetInt(PrefsKey, (int)gender);
            PlayerPrefs.Save();
        }
    }
}

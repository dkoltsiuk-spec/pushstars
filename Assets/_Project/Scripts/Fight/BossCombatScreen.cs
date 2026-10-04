using System;
using System.Collections.Generic;
using System.Linq;
using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    [DefaultExecutionOrder(450)]
    public sealed class BossCombatScreen : MonoBehaviour
    {
        public bool Preparation;
        public GameObject Root;
        public RectTransform Content;
        public RectTransform PreparationSafeBounds;
        public GameObject[] Legacy;
        public FightAvatar PlayerStage, BossStage;
        public RawImage PlayerPortrait, BossPortrait, PlayerIcon;
        public BossHealthBarGraphic PlayerHpFill, BossHpFill;
        public TMP_Text PlayerHpText, BossHpText, BossName, PlayerName, Reps, Form, Tempo, Timer, Best, Reward;
        public TMP_Text SourcePlayerName, ActionLabel;
        public Button Action, Home;
        public BossStarsGraphic Stars;
        public RectTransform Guidance;
        public TMP_Text GuidanceText;
        public TMP_Text Countdown;
        public TMP_Text[] DamageLabels;
        public GoblinForestPresentation Forest;
        private FightController _fight;
        private BossCombatState _health;
        private bool _configured;
        private float _playerFill = 1, _bossFill = 1;
        private int _damageIndex;
        private readonly List<(TMP_Text label, float time, Vector2 origin)> _numbers = new();
        private ClawSlashGraphic _clawSlash;
        private TMP_Text _clapLabel;
        private float _clapAt = -100f;
        private Vector2 _bossPortraitHome;
        private Color _bossPortraitTint = Color.white;
        private static RenderTexture _headSnapshot;
        private static Rect _headSnapshotUv;
        private float _headCaptureAt;
        private bool _headCaptured;
        private Image _playerPreparationShadow, _bossPreparationShadow;
        private string _lastPlayerName;
        public float BossDefeatPresentationSeconds { get; private set; } = .6f;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHeadSnapshot()
        {
            if (_headSnapshot != null)
            {
                _headSnapshot.Release();
                if (Application.isPlaying) Destroy(_headSnapshot); else DestroyImmediate(_headSnapshot);
            }
            _headSnapshot = null;
        }
        public bool Active => Root != null && Root.activeSelf;

        private void Start()
        {
            Configure(FightRequest.HasRequest && FightRequest.Mode == FightMode.Boss);
        }

        public void Configure(bool active)
        {
            _preparationFrames.Clear();
            Root.SetActive(active);
            if (!active || _configured) return;
            _configured = true;
            _headCaptureAt = Time.unscaledTime + .6f;
            foreach (var item in Legacy) if (item != null) item.SetActive(false);
            foreach (var avatar in new[] { PlayerStage, BossStage })
                if (avatar != null && avatar.StageCamera != null) avatar.StageCamera.aspect = PushStars.UI.AvatarWideCamera.TextureAspect(avatar.StageCamera, avatar.StageCamera.targetTexture);
            _fight = FindObjectsByType<FightController>(FindObjectsSortMode.None).FirstOrDefault(f => f.gameObject.scene == gameObject.scene);
            string id = FightRequest.BossId ?? BossCatalog.Current.Id;
            var bossIcon = Content.Find("BossIcon")?.GetComponent<Image>();
            var portrait = Resources.Load<Sprite>("Bosses/" + (BossCatalog.Find(id)?.PrefabId ?? id) + "-portrait");
            if (bossIcon != null && portrait != null) bossIcon.sprite = portrait;
            if (Forest != null) Forest.Configure(!Preparation && BossCatalog.ChapterOf(id)?.Id == "forest");
            _health = !Preparation && _fight != null ? _fight.BossHealth : null;
            _health ??= new BossCombatState(id);
            _health.Damaged += OnDamage;
            _health.ClapStrike += OnClapStrike;
            if (!Preparation) BuildClapStrike();
            if (!Preparation) StagePlayerOnGround();
            var profile = BossCatalog.Bosses.FirstOrDefault(b => b.Id == id) ?? BossCatalog.Current;
            BossName.text = profile.DisplayName;
            Stars.Filled = Mathf.Clamp(BossCatalog.StageInChapter(profile.Id), 1, 5); Stars.SetVerticesDirty();
            if (Best != null) Best.text = LocalProfile.BestReps.ToString();
            if (Reward != null) Reward.text = "+" + FightConfig.BossWinXpBonus;
            if (Preparation)
            {
                _playerPreparationShadow = Content.Find("PlayerGroundShadow")?.GetComponent<Image>();
                _bossPreparationShadow = Content.Find("OpponentGroundShadow")?.GetComponent<Image>();
                var backdrop = Root.GetComponentInChildren<PreparationArenaBackdrop>(true);
                string chapter = BossCatalog.ChapterOf(id)?.Id;
                string bossLocation = chapter == "lava" ? "lava-forge" : chapter == "ice" ? "ice-temple" : "jungle";
                backdrop?.SetMaps(bossLocation, bossLocation);
            }
            Home.onClick.AddListener(() => FightScreenNavigation.Navigate(FightScreen.Home));
            Action.onClick.AddListener(() =>
            {
                if (Preparation) FightScreenNavigation.Navigate(FightScreen.Battle);
                else if (_fight != null && _fight.CanFinishEarly) _fight.FinishEarly();
                else _fight?.ConfirmBossReady();
            });
            if (!Preparation && Guidance != null)
            {
                Guidance.SetParent(Content, false); Guidance.SetAsLastSibling();
                Guidance.anchorMin = Guidance.anchorMax = Guidance.pivot = new Vector2(.5f, .5f);
                Guidance.sizeDelta = new Vector2(350, 40); Guidance.anchoredPosition = new Vector2(0, -326);
                if (GuidanceText != null) GuidanceText.fontSize = 16;
            }
            if (!Preparation && Countdown != null)
            {
                var rect = Countdown.rectTransform;
                rect.SetParent(Content, false); rect.SetAsLastSibling();
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(370, 110);
                rect.localScale = Vector3.one; Countdown.fontSize = 64;
            }
            foreach (var label in DamageLabels) label.gameObject.SetActive(false);
            Refresh(0);
            if (Preparation && Application.isPlaying)
                (Root.GetComponent<MatchFoundImpact>() ?? Root.AddComponent<MatchFoundImpact>()).Play();
        }

        private void LateUpdate()
        {
            if (!Active || !_configured) return;
            // Legacy HUD setters can reactivate their old scoreboard at countdown boundaries.
            foreach (var item in Legacy) if (item != null && item.activeSelf) item.SetActive(false);
            Refresh(Time.unscaledDeltaTime);
        }

        public void Refresh(float delta)
        {
            if (_health == null) return;
            var area = (Preparation && PreparationSafeBounds != null ? PreparationSafeBounds : (RectTransform)Root.transform).rect;
            Content.localScale = Vector3.one * Mathf.Min(area.width / 390, area.height / 844);
            if (Forest != null && Content.localScale.x > 0) Forest.ApplyLayout(area.size / Content.localScale.x);
            _playerFill = Mathf.MoveTowards(_playerFill, _health.PlayerHp / (float)BossCombatState.PlayerMaxHp, delta * 2.8f);
            _bossFill = Mathf.MoveTowards(_bossFill, _health.BossHp / (float)_health.BossMaxHp, delta * 2.8f);
            PlayerHpFill.FillAmount = _playerFill; BossHpFill.FillAmount = _bossFill;
            PlayerHpText.text = _health.PlayerHp + " / " + BossCombatState.PlayerMaxHp;
            BossHpText.text = _health.BossHp + " / " + _health.BossMaxHp;
            string playerName = SourcePlayerName != null && !string.IsNullOrWhiteSpace(SourcePlayerName.text) ? SourcePlayerName.text : "YOU";
            playerName = PushStars.UI.ProfileIdentityEditor.ResolveName(playerName);
            if (playerName != _lastPlayerName)
            {
                _lastPlayerName = playerName;
                int separator = playerName.LastIndexOf('_');
                PlayerName.text = Preparation && PlayerName.GetPreferredValues(playerName).x > PlayerName.rectTransform.rect.width && separator > 0
                    ? playerName.Insert(separator + 1, "\n") : playerName;
            }
            if (!Preparation && _fight != null)
            {
                Reps.text = _fight.PlayerBattleReps.ToString();
                Form.text = _fight.PlayerBattleForm > 0 ? _fight.PlayerBattleForm.ToString("0") : "—";
                Tempo.text = _fight.PlayerBattleTempo > .01f ? (60 / _fight.PlayerBattleTempo).ToString("0.0") + "s" : "—";
                int seconds = Mathf.CeilToInt(_fight.BattleSecondsLeft); Timer.text = $"{seconds / 60}:{seconds % 60:00}";
                bool canFinish = _fight.CanFinishEarly;
                bool canReady = !_fight.BossReady;
                Action.gameObject.SetActive(canReady || canFinish);
                Action.interactable = canReady || canFinish;
                ActionLabel.text = canFinish ? "FINISH" : "READY";
            }
            CopyBody(PlayerPortrait, PlayerStage, Preparation, smoothPlayer: !Preparation);
            CopyBody(BossPortrait, BossStage, Preparation, !Preparation && _health.BossHp == 0);
            CopyHead();
            for (int i = _numbers.Count - 1; i >= 0; i--)
            {
                var number = _numbers[i]; float t = (Time.unscaledTime - number.time) / .9f;
                if (t >= 1) { number.label.gameObject.SetActive(false); _numbers.RemoveAt(i); continue; }
                number.label.rectTransform.anchoredPosition = number.origin + Vector2.up * (48 * t);
                number.label.rectTransform.localScale = Vector3.one * (1 + .22f * Mathf.Sin(Mathf.Clamp01(t * 3) * Mathf.PI));
                number.label.alpha = 1 - t * t;
            }
            AnimateClapStrike();
        }

        // ── Clap push-up: double claw strike ─────────────────────────────────────────────────────

        /// <summary>Runtime-built so the authored scene needs no change: the slash sits right above
        /// the boss portrait (below HP bars, numbers and the countdown); "×2" reuses the damage
        /// number styling.</summary>
        private void BuildClapStrike()
        {
            if (BossPortrait == null || _clawSlash != null) return;
            var go = new GameObject("ClapClawSlash", typeof(RectTransform), typeof(CanvasRenderer), typeof(ClawSlashGraphic));
            go.layer = gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(BossPortrait.transform.parent, false);
            rect.SetSiblingIndex(BossPortrait.transform.GetSiblingIndex() + 1);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero; rect.sizeDelta = Vector2.zero;
            _clawSlash = go.GetComponent<ClawSlashGraphic>();
            _bossPortraitHome = BossPortrait.rectTransform.anchoredPosition;
            _bossPortraitTint = BossPortrait.color;
            if (DamageLabels.Length == 0 || DamageLabels[0] == null) return;
            _clapLabel = Instantiate(DamageLabels[0], DamageLabels[0].transform.parent);
            _clapLabel.name = "ClapX2";
            _clapLabel.text = "×2";
            _clapLabel.color = Color.white;
            _clapLabel.alignment = TextAlignmentOptions.Center;
            _clapLabel.rectTransform.localRotation = Quaternion.Euler(0, 0, -8);
            _clapLabel.transform.SetAsLastSibling();
            _clapLabel.gameObject.SetActive(false);
        }

        private void OnClapStrike(int damage)
        {
            if (_clawSlash == null) return;
            var portrait = BossPortrait.rectTransform;
            // Rake the torso: a touch above the portrait's centre, sized to the boss body.
            var size = portrait.rect.size;
            _clawSlash.Play(_bossPortraitHome + Vector2.up * (size.y * .06f), Mathf.Min(size.x, size.y) * .56f);
            _clapAt = Time.unscaledTime;
            if (_clapLabel != null) { _clapLabel.gameObject.SetActive(true); _clapLabel.alpha = 1; }
        }

        /// <summary>Hit-stop shake + red flash on the boss, and the "×2" pop; restores exactly.</summary>
        private void AnimateClapStrike()
        {
            if (_clawSlash == null) return;
            float t = Time.unscaledTime - _clapAt;
            var portrait = BossPortrait.rectTransform;
            if (t < .32f)
            {
                float k = 1 - t / .32f;
                portrait.anchoredPosition = _bossPortraitHome + new Vector2(
                    Mathf.Sin(t * 95f) * 7f * k, Mathf.Sin(t * 71f + 1.3f) * 4f * k);
                float flash = Mathf.Clamp01(k * 1.4f);
                BossPortrait.color = Color.Lerp(_bossPortraitTint, _bossPortraitTint * new Color(1f, .5f, .48f), flash);
            }
            else if (portrait.anchoredPosition != _bossPortraitHome || BossPortrait.color != _bossPortraitTint)
            {
                portrait.anchoredPosition = _bossPortraitHome;
                BossPortrait.color = _bossPortraitTint;
            }

            if (_clapLabel == null || !_clapLabel.gameObject.activeSelf) return;
            float u = t / .95f;
            if (u >= 1) { _clapLabel.gameObject.SetActive(false); return; }
            // Upper-left of the boss: the damage number already floats up on the right.
            var origin = _bossPortraitHome + new Vector2(-portrait.rect.width * .40f, portrait.rect.height * .22f);
            _clapLabel.rectTransform.anchoredPosition = origin + Vector2.up * (30 * u);
            // Stamp in big, settle, then fade.
            float pop = u < .12f ? Mathf.Lerp(2.1f, .92f, u / .12f) : Mathf.Lerp(.92f, 1f, Mathf.Clamp01((u - .12f) / .1f));
            _clapLabel.rectTransform.localScale = Vector3.one * pop;
            _clapLabel.alpha = 1 - Mathf.Clamp01((u - .6f) / .4f);
        }

        private readonly System.Collections.Generic.Dictionary<RawImage,
            (FightAvatar avatar, Texture texture, Vector2 size, Rect body, Rect uv)> _preparationFrames = new();

        private void CopyBody(RawImage target, FightAvatar avatar, bool cropPortrait, bool grounded = false, bool smoothPlayer = false)
        {
            if (target == null || avatar == null || avatar.StageCamera == null) return;
            PushStars.UI.AvatarWideImage.Configure(target, avatar.StageCamera);
            var texture = avatar.StageCamera.targetTexture; target.texture = texture;
            if (texture == null) return;
            // The live player's stage renders straight into this portrait (see
            // StagePlayerOnGround): full texture, camera aspect matched to the rect. Cropping to
            // the body would cancel the mirror anchor's distance and position measurements.
            if (smoothPlayer)
            {
                target.uvRect = new Rect(0f, 0f, 1f, 1f);
                return;
            }
            Vector2 size = target.rectTransform.rect.size;
            if (cropPortrait && avatar.IsPreparationFramed
                && _preparationFrames.TryGetValue(target, out var held)
                && held.avatar == avatar && held.texture == texture && held.size == size)
            {
                target.uvRect = held.uv;
                PlacePreparationShadow(target, held.body);
                return;
            }
            _preparationFrames.Remove(target);
            if (!avatar.TryGetBodyViewport(out var body)) return;
            float aspect = target.rectTransform.rect.width / target.rectTransform.rect.height;
            float textureAspect = PushStars.UI.AvatarWideCamera.TextureAspect(avatar.StageCamera, texture);
            // Preparation shares PVP's crown-to-sole framing and planted contact shadows.
            // Fit width as well as height so broad shoulders and ears stay inside the frame.
            float height = body.height * (cropPortrait ? 1f / .95f : 1.10f);
            height = Mathf.Max(height, body.width * textureAspect / aspect * 1.08f);
            float width = height * aspect / textureAspect;
            float centerY = cropPortrait ? body.yMin - (height - body.height) * .1f + height * .5f : body.center.y;
            if (grounded) centerY = body.yMin + height * .45f;
            target.uvRect = new Rect(body.center.x - width * .5f, centerY - height * .5f, width, height);
            if (cropPortrait && avatar.IsPreparationFramed)
                _preparationFrames[target] = (avatar, texture, size, body, target.uvRect);
            if (cropPortrait) PlacePreparationShadow(target, body);
        }

        private void PlacePreparationShadow(RawImage portrait, Rect body)
        {
            var shadow = portrait == PlayerPortrait ? _playerPreparationShadow : _bossPreparationShadow;
            if (shadow == null) return;
            var box = portrait.rectTransform;
            var uv = portrait.uvRect;
            float figure = box.rect.height * body.height / uv.height;
            float height = figure * .09f;
            var contact = new Vector2(box.rect.xMin + (body.center.x - uv.xMin) / uv.width * box.rect.width,
                box.rect.yMin + (body.yMin - uv.yMin) / uv.height * box.rect.height + height * .65f);
            shadow.rectTransform.position = box.TransformPoint(contact);
            shadow.rectTransform.sizeDelta = new Vector2(figure * .40f, height);
        }

        /// <summary>The player's stage was authored for the duel half, hidden in a boss battle:
        /// its camera kept that half's tall aspect and the push-up shot was fitted to that half's
        /// crop, so the body came out small and floating once shrunk into this wide portrait.
        /// Point the stage at the portrait itself and stand the push-up on its ground line.</summary>
        private void StagePlayerOnGround()
        {
            if (PlayerStage == null || PlayerPortrait == null || PlayerStage.StageCamera == null) return;
            PlayerStage.StageCamera.GetComponentInParent<PushStars.UI.CharacterStage>()?.SetDisplayTarget(PlayerPortrait);
            // Mockup: the hands span ~213 of the 390-wide screen; palms just above the portrait's
            // bottom edge (clear of the guidance band).
            PlayerStage.SetPushupShot(213f, 24f);
            // The push-up brings its own crisp contact ellipse (FightAvatar); the authored soft
            // sprite under it would double it.
            if (Content.Find("PlayerShadow") is RectTransform playerShadow) playerShadow.gameObject.SetActive(false);
            CrispenShadow(Content.Find("BossShadow") as RectTransform);
        }

        /// <summary>Mockups draw contact shadows as flat, crisp ellipses; the authored soft
        /// "ground_shadow" sprite reads as a blur. Same rect and tint, hard edge.</summary>
        private static void CrispenShadow(RectTransform soft)
        {
            if (soft == null || !soft.TryGetComponent(out Image image) || !image.enabled) return;
            var go = new GameObject(soft.name + "Crisp", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(PushStars.UI.HardEllipseGraphic));
            go.layer = soft.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(soft.parent, false);
            rect.SetSiblingIndex(soft.GetSiblingIndex());
            rect.anchorMin = soft.anchorMin; rect.anchorMax = soft.anchorMax; rect.pivot = soft.pivot;
            rect.anchoredPosition = soft.anchoredPosition;
            // The soft sprite's visible core is narrower than its rect.
            rect.sizeDelta = new Vector2(soft.sizeDelta.x * .9f, Mathf.Max(soft.sizeDelta.y, soft.sizeDelta.x * .17f));
            var ellipse = go.GetComponent<PushStars.UI.HardEllipseGraphic>();
            ellipse.color = new Color(0f, 0f, 0f, .45f);
            ellipse.raycastTarget = false;
            image.enabled = false;
        }

        internal static Rect CameraViewport(Vector2 display, Vector2 texture)
        {
            float aspect = Mathf.Max(1f, display.x) / Mathf.Max(1f, display.y);
            float textureAspect = Mathf.Max(1f, texture.x) / Mathf.Max(1f, texture.y);
            // Contain the complete camera image without stretching or zooming in.
            float width = Mathf.Max(1f, aspect / textureAspect);
            float height = Mathf.Max(1f, textureAspect / aspect);
            return new Rect((1f - width) * .5f, (1f - height) * .5f, width, height);
        }

        private void CopyHead()
        {
            PushStars.UI.AvatarWideImage.Configure(PlayerIcon, null);
            if (_headSnapshot != null && (!Preparation || _headCaptured))
            {
                PlayerIcon.texture = _headSnapshot; PlayerIcon.uvRect = _headSnapshotUv; return;
            }
            if (PlayerStage == null || PlayerStage.Character == null || PlayerStage.StageCamera.targetTexture == null) return;
            var animator = PlayerStage.Character.GetComponentInChildren<Animator>();
            var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head == null || !PlayerStage.TryGetBodyViewport(out var body)) return;
            var camera = PlayerStage.StageCamera; var tex = camera.targetTexture;
            var point = camera.WorldToViewportPoint(head.position);
            float h = body.height * .18f, w = h / PushStars.UI.AvatarWideCamera.TextureAspect(camera, tex) * PlayerIcon.rectTransform.rect.width / PlayerIcon.rectTransform.rect.height;
            // A head icon keeps its small UI box; compensate in UVs so its saved snapshot
            // also stays proportional after the battle camera leaves preparation mode.
            if (camera.TryGetComponent<PushStars.UI.AvatarWideCamera>(out var lens) && lens.enabled)
                w /= PushStars.UI.AvatarWideCamera.WidthMultiplier;
            var uv = new Rect(point.x - w * .5f, point.y - h * .27f, w, h);
            PlayerIcon.texture = tex; PlayerIcon.uvRect = uv;
            // Keep the standing preparation portrait when the live body goes into a push-up.
            if (!_headCaptured && (!Application.isPlaying || Time.unscaledTime >= _headCaptureAt))
            {
                ResetHeadSnapshot();
                _headSnapshot = new RenderTexture(tex.width, tex.height, 0) { name = "BossPlayerHeadSnapshot", hideFlags = HideFlags.DontSave };
                Graphics.Blit(tex, _headSnapshot); _headSnapshotUv = uv; _headCaptured = true;
            }
        }

        private void OnDamage(bool boss, int damage)
        {
            if (boss && Forest != null && Forest.Effects != null && Forest.Effects.isActiveAndEnabled)
            {
                var portrait = BossPortrait.rectTransform;
                Forest.Effects.Hit(portrait.anchoredPosition + Vector2.up * (portrait.rect.height * .1f));
                if (_health.BossHp == 0)
                {
                    var presentation = BossStage != null && BossStage.Character != null
                        ? BossStage.Character.GetComponentInChildren<BossAvatarPresentation>() : null;
                    float fallTime = presentation != null ? presentation.BeginDefeat() : .6f;
                    float contactTime = Mathf.Min(fallTime * .45f, 1.1f);
                    BossDefeatPresentationSeconds = Mathf.Max(.6f, contactTime + 1.3f);
                    Forest.Effects.QueueFall(portrait.anchoredPosition + Vector2.down * (portrait.rect.height * .5f - 12), contactTime);
                }
            }
            if (DamageLabels.Length == 0) return;
            var label = DamageLabels[_damageIndex++ % DamageLabels.Length];
            _numbers.RemoveAll(n => n.label == label);
            label.text = "-" + damage; label.gameObject.SetActive(true); label.alpha = 1;
            var origin = new Vector2(120 + (_damageIndex % 2) * 10, boss ? 245 : -135);
            _numbers.Add((label, Time.unscaledTime, origin));
        }
        private void OnDestroy()
        {
            if (_health == null) return;
            _health.Damaged -= OnDamage;
            _health.ClapStrike -= OnClapStrike;
        }
    }
}

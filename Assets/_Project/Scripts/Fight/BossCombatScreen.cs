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
        private static RenderTexture _headSnapshot;
        private static Rect _headSnapshotUv;
        private float _headCaptureAt;
        private bool _headCaptured;
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
            Root.SetActive(active);
            if (!active || _configured) return;
            _configured = true;
            _headCaptureAt = Time.unscaledTime + .6f;
            foreach (var item in Legacy) if (item != null) item.SetActive(false);
            foreach (var avatar in new[] { PlayerStage, BossStage })
                if (avatar != null && avatar.StageCamera != null) avatar.StageCamera.ResetAspect();
            _fight = FindObjectsByType<FightController>(FindObjectsSortMode.None).FirstOrDefault(f => f.gameObject.scene == gameObject.scene);
            string id = FightRequest.BossId ?? BossCatalog.Current.Id;
            if (Forest != null) Forest.Configure(!Preparation && id == "novice");
            _health = !Preparation && _fight != null ? _fight.BossHealth : null;
            _health ??= new BossCombatState(id);
            _health.Damaged += OnDamage;
            var profile = BossCatalog.Bosses.FirstOrDefault(b => b.Id == id) ?? BossCatalog.Current;
            BossName.text = profile.DisplayName;
            Stars.Filled = Mathf.Clamp(BossCatalog.Bosses.ToList().IndexOf(profile) + 1, 1, 5); Stars.SetVerticesDirty();
            if (Best != null) Best.text = LocalProfile.BestReps.ToString();
            if (Reward != null) Reward.text = "+" + FightConfig.BossWinXpBonus;
            Home.onClick.AddListener(() => FightScreenNavigation.Navigate(FightScreen.Home));
            Action.onClick.AddListener(() =>
            {
                if (Preparation) FightScreenNavigation.Navigate(FightScreen.Battle);
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
            var area = ((RectTransform)Root.transform).rect;
            Content.localScale = Vector3.one * Mathf.Min(area.width / 390, area.height / 844);
            if (Forest != null && Content.localScale.x > 0) Forest.ApplyLayout(area.size / Content.localScale.x);
            _playerFill = Mathf.MoveTowards(_playerFill, _health.PlayerHp / (float)BossCombatState.PlayerMaxHp, delta * 2.8f);
            _bossFill = Mathf.MoveTowards(_bossFill, _health.BossHp / (float)_health.BossMaxHp, delta * 2.8f);
            PlayerHpFill.FillAmount = _playerFill; BossHpFill.FillAmount = _bossFill;
            PlayerHpText.text = _health.PlayerHp + " / " + BossCombatState.PlayerMaxHp;
            BossHpText.text = _health.BossHp + " / " + _health.BossMaxHp;
            string playerName = SourcePlayerName != null && !string.IsNullOrWhiteSpace(SourcePlayerName.text) ? SourcePlayerName.text : "ТЫ";
            playerName = PushStars.UI.ProfileIdentityEditor.ResolveName(playerName);
            int separator = playerName.LastIndexOf('_');
            PlayerName.text = Preparation && playerName.Length > 12 && separator > 0
                ? playerName.Insert(separator + 1, "\n") : playerName;
            if (!Preparation && _fight != null)
            {
                Reps.text = _fight.PlayerBattleReps.ToString();
                Form.text = _fight.PlayerBattleForm > 0 ? _fight.PlayerBattleForm.ToString("0") : "—";
                Tempo.text = _fight.PlayerBattleTempo > .01f ? (60 / _fight.PlayerBattleTempo).ToString("0.0") + "s" : "—";
                int seconds = Mathf.CeilToInt(_fight.BattleSecondsLeft); Timer.text = $"{seconds / 60}:{seconds % 60:00}";
                Action.interactable = !_fight.BossReady;
                ActionLabel.text = _fight.IsBossBattleLive ? "FIGHT" : "READY";
            }
            CopyBody(PlayerPortrait, PlayerStage, Preparation);
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
        }

        private static void CopyBody(RawImage target, FightAvatar avatar, bool cropPortrait, bool grounded = false)
        {
            if (target == null || avatar == null || avatar.StageCamera == null) return;
            var texture = avatar.StageCamera.targetTexture; target.texture = texture;
            if (texture == null || !avatar.TryGetBodyViewport(out var body)) return;
            float aspect = target.rectTransform.rect.width / target.rectTransform.rect.height;
            float textureAspect = (float)texture.width / texture.height;
            float height = body.height * (cropPortrait ? .64f : 1.10f);
            if (!cropPortrait) height = Mathf.Max(height, body.width * textureAspect / aspect * 1.08f);
            float width = height * aspect / textureAspect;
            float centerY = body.center.y + (cropPortrait ? body.height * .20f : 0);
            if (grounded) centerY = body.yMin + height * .45f;
            target.uvRect = new Rect(body.center.x - width * .5f, centerY - height * .5f, width, height);
        }

        private void CopyHead()
        {
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
            float h = body.height * .18f, w = h * tex.height / tex.width * PlayerIcon.rectTransform.rect.width / PlayerIcon.rectTransform.rect.height;
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
        private void OnDestroy() { if (_health != null) _health.Damaged -= OnDamage; }
    }
}

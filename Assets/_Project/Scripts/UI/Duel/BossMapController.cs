using System;
using System.Collections;
using System.Collections.Generic;
using PushStars.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Authored chapters stacked upward; nodes bind ladder bosses, rewards bind island collectibles.</summary>
    public sealed class BossMapController : MonoBehaviour
    {
        [Serializable]
        public sealed class Node
        {
            public int BossIndex;
            public Image Platform, Disc, Face;
            public Button Button, Fight;
            public Image ProgressPlate, ProgressFace;
        }

        public enum RewardKind { Gems, Chest }

        /// <summary>A collectible on an island: the gem islet or the main-boss chest.</summary>
        [Serializable]
        public sealed class RewardNode
        {
            public string ChapterId;
            public RewardKind Kind;
            public Button Button;
            /// <summary>Optional platform under the reward (grass once reached, stone while locked).</summary>
            public Image Platform;
            public Image Icon;
            public RewardBounce Bounce;
        }

        /// <summary>Opens the case-award screen for a just-granted case. Registered by the fight
        /// assembly (this one cannot reference it); when absent the case waits under CASES.</summary>
        public static Func<string, bool> CaseAwardHandler;

        public GameObject Background, Home, Map, BottomNav;
        public GameObject[] CharacterDecor, HomeOnly;
        public Button Island, Close;
        public RectTransform HomeComposition, MapContent;
        public RectTransform[] ResponsiveControls;
        public ScrollRect Scroll;
        public CanvasGroup MapGroup;
        public Node[] Nodes;
        public RewardNode[] Rewards = new RewardNode[0];
        /// <summary>Home island's gem bubble; hidden once the first island's gems are collected.</summary>
        public GameObject HomeGemBubble;
        public Sprite Grass, Stone, Complete, Current, Locked, Goblin, LockedGoblin;
        public Sprite ProgressCurrent, ProgressLocked;
        public Sprite FinalGoblin, FinalLockedGoblin;
        public FriendDuelController FriendDuel;
        public SearchOpponentController Battle;
        public Toast Hint;
        private bool _boss, _cached, _busy;
        private bool[] _decorActive, _homeActive;
        private bool _navActive;
        private Vector3[] _controlScales;
        private int _progress = -1;
        private readonly List<GameObject> _flyingGems = new List<GameObject>();
        private Coroutine _gemFlight;
        private RectTransform _pressed;
        private Vector3 _pressScale;
        private Vector2 _pressPosition;
        public bool IsMapOpen => Map != null && Map.activeSelf;

        private void Start()
        {
            Island.onClick.AddListener(OpenMap);
            Close.onClick.AddListener(CloseMap);
            foreach (var node in Nodes)
            {
                var captured = node;
                node.Button.onClick.AddListener(() => SelectNode(captured));
                node.Fight.onClick.AddListener(() => SelectNode(captured));
            }
            foreach (var reward in Rewards)
            {
                var captured = reward;
                reward.Button.onClick.AddListener(() => SelectReward(captured));
            }
            RefreshMode();
        }

        private void Update()
        {
            RefreshMode();
            if (!_boss) return;
            Fit();
            if (_progress != BossCatalog.ClearedCount) RefreshProgress();
            if (IsMapOpen && Input.GetKeyDown(KeyCode.Escape)) CloseMap();
        }

        public void RefreshMode()
        {
            bool boss = SelectedGameMode.Current == GameMode.Boss && (FriendDuel == null || !FriendDuel.HasRoom);
            if (_cached && boss == _boss) return;
            if (!_cached)
            {
                _decorActive = Capture(CharacterDecor);
                _homeActive = Capture(HomeOnly);
                _navActive = BottomNav.activeSelf;
                _controlScales = new Vector3[ResponsiveControls.Length];
                for (int i = 0; i < ResponsiveControls.Length; i++) _controlScales[i] = ResponsiveControls[i].localScale;
                _cached = true;
            }
            _boss = boss;
            if (!boss) { ResetInteraction(); RestoreControlScales(); }
            Background.SetActive(boss);
            Home.SetActive(boss);
            Map.SetActive(false);
            Restore(HomeOnly, _homeActive);
            BottomNav.SetActive(_navActive);
            for (int i = 0; i < CharacterDecor.Length; i++)
                CharacterDecor[i].SetActive(!boss && _decorActive[i]);
            if (boss) { RefreshProgress(); Fit(); }
        }

        public void RefreshProgress()
        {
            _progress = BossCatalog.ClearedCount;
            foreach (var node in Nodes)
            {
                // With everything cleared the last boss stays current: it keeps its FIGHT rematch.
                bool current = node.BossIndex == BossCatalog.CurrentIndex;
                bool cleared = BossCatalog.IsCleared(node.BossIndex) && !current;
                node.Platform.sprite = current || cleared ? Grass : Stone;
                node.Disc.sprite = current ? Current : cleared ? Complete : Locked;
                bool final = node.BossIndex == BossCatalog.Bosses.Count - 1;
                var face = final && FinalGoblin != null ? FinalGoblin : Goblin;
                var lockedFace = final && FinalLockedGoblin != null ? FinalLockedGoblin : LockedGoblin;
                node.Face.sprite = current || cleared ? face : lockedFace;
                node.Fight.gameObject.SetActive(current);
                node.ProgressPlate.sprite = current || cleared ? ProgressCurrent : ProgressLocked;
                node.ProgressPlate.color = cleared ? new Color(.65f, 1f, .35f) : Color.white;
                node.ProgressFace.sprite = current || cleared ? face : lockedFace;
                node.Face.color = node.ProgressFace.color = final && !current && !cleared
                    ? new Color(.45f, .45f, .45f, 1) : Color.white;
            }
            RefreshRewards();
        }

        private void RefreshRewards()
        {
            if (HomeGemBubble != null && BossCatalog.Chapters.Count > 0)
                HomeGemBubble.SetActive(BossMapRewards.Gems(BossCatalog.Chapters[0]) != BossMapRewardState.Collected);
            foreach (var reward in Rewards)
            {
                var state = State(reward);
                if (reward.Platform != null) reward.Platform.sprite = state == BossMapRewardState.Locked ? Stone : Grass;
                reward.Icon.gameObject.SetActive(reward.Kind == RewardKind.Chest || state != BossMapRewardState.Collected);
                reward.Icon.color = state == BossMapRewardState.Locked ? new Color(.5f, .52f, .48f, 1)
                    : state == BossMapRewardState.Collected ? new Color(1, 1, 1, .4f) : Color.white;
                if (reward.Bounce != null) reward.Bounce.Active = state == BossMapRewardState.Ready;
            }
        }

        private static BossMapRewardState State(RewardNode reward)
        {
            var chapter = BossCatalog.FindChapter(reward.ChapterId);
            return reward.Kind == RewardKind.Gems ? BossMapRewards.Gems(chapter) : BossMapRewards.Chest(chapter);
        }

        private void SelectReward(RewardNode reward)
        {
            if (!IsMapOpen || _busy) return;
            StartCoroutine(Press((RectTransform)reward.Button.transform, () => Collect(reward)));
        }

        private void Collect(RewardNode reward)
        {
            var chapter = BossCatalog.FindChapter(reward.ChapterId);
            var state = State(reward);
            if (chapter == null || !chapter.Playable) { Hint?.Show("This island is coming soon"); return; }
            if (state == BossMapRewardState.Collected) { Hint?.Show("Already collected"); return; }
            if (state == BossMapRewardState.Locked)
            {
                Hint?.Show(reward.Kind == RewardKind.Gems
                    ? $"Defeat {chapter.GemsAfterBoss + 1} bosses to reach the gems"
                    : "Defeat the island's main boss to open the case");
                return;
            }
            if (reward.Kind == RewardKind.Gems)
            {
                long before = CaseRewards.GemsBalance;
                if (!BossMapRewards.TryCollectGems(chapter)) return;
                GameAudio.Play(SoundCue.RewardBurst);
                Hint?.Show($"+{chapter.GemReward} GEMS");
                StopGemFlight();
                _gemFlight = StartCoroutine(FlyGems(reward.Icon, before, chapter.GemReward));
                RefreshRewards();
                return;
            }
            if (!BossMapRewards.TryCollectChest(chapter, out string caseId)) return;
            GameAudio.Play(SoundCue.CaseUnlock);
            RefreshRewards();
            if (CaseAwardHandler == null || !CaseAwardHandler(caseId)) Hint?.Show("Case added to your CASES");
        }

        /// <summary>Gem icons burst from the islet and arc into the HUD pill, which counts up as they land.</summary>
        private IEnumerator FlyGems(Image source, long before, int amount)
        {
            var pill = FindChild(transform.root, "GemPill");
            if (pill == null) yield break;
            var target = FindChild(pill, "GemIcon") ?? pill;
            var layer = (RectTransform)Map.transform;
            HudBalanceHold.Set("GemPill", before);
            const int count = 7;
            const float burst = .22f, stagger = .06f, travel = .5f;
            float unit = layer.lossyScale.x;
            Vector3 from = source.rectTransform.position, to = target.position;
            var icons = new RectTransform[count];
            var scatter = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                var icon = new GameObject("FlyingGem", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                icon.SetParent(layer, false); icon.sizeDelta = new Vector2(34, 34);
                var image = icon.GetComponent<Image>(); image.sprite = source.sprite; image.raycastTarget = false;
                icon.position = from; icon.localScale = Vector3.zero;
                icons[i] = icon; _flyingGems.Add(icon.gameObject);
                float angle = i / (float)count * Mathf.PI * 2 + .4f;
                scatter[i] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * .7f + .3f) * (60 * unit);
            }
            int landed = 0;
            float end = burst + stagger * (count - 1) + travel;
            for (float elapsed = 0; landed < count; elapsed += Time.unscaledDeltaTime)
            {
                for (int i = 0; i < count; i++)
                {
                    if (icons[i] == null) continue;
                    float start = burst + stagger * i;
                    if (elapsed < start)
                    {
                        float k = UITween.EaseOutBack(Mathf.Clamp01(elapsed / burst));
                        icons[i].position = from + scatter[i] * k;
                        icons[i].localScale = Vector3.one * k;
                        continue;
                    }
                    float t = Mathf.Clamp01((elapsed - start) / travel), e = UITween.EaseInQuad(t);
                    Vector3 a = from + scatter[i], control = Vector3.Lerp(a, to, .5f) + Vector3.left * (80 * unit);
                    icons[i].position = Vector3.Lerp(Vector3.Lerp(a, control, e), Vector3.Lerp(control, to, e), e);
                    icons[i].localScale = Vector3.one * Mathf.Lerp(1, .6f, t);
                    if (t < 1) continue;
                    _flyingGems.Remove(icons[i].gameObject);
                    Destroy(icons[i].gameObject); icons[i] = null;
                    landed++;
                    HudBalanceHold.Set("GemPill", before + amount * landed / count);
                    GameAudio.Play(SoundCue.RewardTick, 1 + .25f * landed / count);
                }
                if (elapsed > end + 1) break;
                yield return null;
            }
            HudBalanceHold.Release("GemPill");
            GameAudio.Play(SoundCue.RewardComplete);
            _gemFlight = null;
        }

        private void StopGemFlight()
        {
            if (_gemFlight != null) StopCoroutine(_gemFlight);
            _gemFlight = null;
            foreach (var gem in _flyingGems) if (gem != null) Destroy(gem);
            _flyingGems.Clear();
            HudBalanceHold.Release("GemPill");
        }

        private static Transform FindChild(Transform parent, string name)
        {
            foreach (var child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == name && child != parent) return child;
            return null;
        }

        public void OpenMap()
        {
            if (!_boss || IsMapOpen || _busy) return;
            StartCoroutine(Press((RectTransform)Island.transform, () =>
            {
                SetMapVisible(true);
                StartCoroutine(RevealMap());
            }));
        }

        private IEnumerator RevealMap()
        {
            yield return UITween.Fade(MapGroup, 0, 1, .18f, 0);
        }

        public void CloseMap()
        {
            if (!IsMapOpen || _busy) return;
            StartCoroutine(Press((RectTransform)Close.transform, () =>
            {
                SetMapVisible(false);
            }));
        }

        private void SetMapVisible(bool visible)
        {
            Map.SetActive(visible);
            Home.SetActive(_boss && !visible);
            for (int i = 0; i < HomeOnly.Length; i++) HomeOnly[i].SetActive(!visible && _homeActive[i]);
            BottomNav.SetActive(!visible && _navActive);
            if (!visible) return;
            RefreshProgress(); Fit();
            Canvas.ForceUpdateCanvases();
            Scroll.StopMovement();
            Scroll.verticalNormalizedPosition = 0;
        }

        private void SelectNode(Node node)
        {
            if (!IsMapOpen || _busy) return;
            StartCoroutine(Press((RectTransform)node.Button.transform, () =>
            {
                if (node.BossIndex == BossCatalog.CurrentIndex) Battle.Show();
                else Hint?.Show(BossCatalog.IsCleared(node.BossIndex)
                    ? "You've already defeated this boss" : "Defeat the previous boss first");
            }));
        }

        /// <summary>The home island's reward bubble: rewards are collected on the map itself.</summary>
        public void ShowRewardHint() => OpenMap();

        private IEnumerator Press(RectTransform target, Action complete)
        {
            _busy = true; _pressed = target;
            _pressScale = target.localScale; _pressPosition = target.anchoredPosition;
            const float down = .075f, up = .14f;
            for (float elapsed = 0; elapsed < down + up; elapsed += Time.unscaledDeltaTime)
            {
                float amount = elapsed < down ? UITween.EaseOutQuad(elapsed / down)
                    : 1 - UITween.EaseOutCubic((elapsed - down) / up);
                target.localScale = _pressScale * Mathf.Lerp(1, .94f, amount);
                target.anchoredPosition = _pressPosition + Vector2.down * (5 * amount);
                yield return null;
            }
            target.localScale = _pressScale; target.anchoredPosition = _pressPosition;
            _pressed = null; _busy = false;
            complete();
        }

        private void Fit()
        {
            var panel = (RectTransform)Home.transform;
            for (int i = 0; i < ResponsiveControls.Length; i++)
                ResponsiveControls[i].localScale = _controlScales[i] * Mathf.Min(1, panel.rect.width / 390f);
            float scale = Mathf.Min(panel.rect.width / 390f, Mathf.Max(.35f, (panel.rect.height - 330) / 405f));
            HomeComposition.localScale = Vector3.one * scale;
            HomeComposition.anchoredPosition = new Vector2(0, 225 + 202.5f * scale);
            float mapScale = Scroll.viewport.rect.width / 390f;
            // The content rect stays in viewport units; its authored chapters scale inside it.
            float height = 0;
            foreach (RectTransform chapter in MapContent)
            {
                chapter.localScale = Vector3.one * mapScale;
                chapter.anchoredPosition = new Vector2(0, height);
                height += chapter.rect.height * mapScale;
            }
            MapContent.sizeDelta = new Vector2(0, Mathf.Max(height, Scroll.viewport.rect.height));
        }

        private static bool[] Capture(GameObject[] objects)
        {
            var result = new bool[objects.Length];
            for (int i = 0; i < objects.Length; i++) result[i] = objects[i].activeSelf;
            return result;
        }
        private static void Restore(GameObject[] objects, bool[] states)
        {
            if (states == null) return;
            for (int i = 0; i < objects.Length; i++) objects[i].SetActive(states[i]);
        }
        private void ResetInteraction()
        {
            StopAllCoroutines();
            StopGemFlight();
            if (_pressed != null)
            { _pressed.localScale = _pressScale; _pressed.anchoredPosition = _pressPosition; }
            _pressed = null; _busy = false;
            if (MapGroup != null) MapGroup.alpha = 1;
        }
        private void RestoreControlScales()
        {
            if (_controlScales == null) return;
            for (int i = 0; i < ResponsiveControls.Length; i++) ResponsiveControls[i].localScale = _controlScales[i];
        }
        private void OnDisable()
        {
            ResetInteraction();
            if (!_cached) return;
            RestoreControlScales();
            Background.SetActive(false); Home.SetActive(false); Map.SetActive(false);
            Restore(CharacterDecor, _decorActive); Restore(HomeOnly, _homeActive);
            BottomNav.SetActive(_navActive);
            _cached = false;
        }
    }
}

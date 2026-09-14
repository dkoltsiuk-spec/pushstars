using System;
using System.Collections;
using PushStars.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>One authored chapter today; append chapter rects and nodes to extend the map.</summary>
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

        public GameObject Background, Home, Map, BottomNav;
        public GameObject[] CharacterDecor, HomeOnly;
        public Button Island, Close;
        public RectTransform HomeComposition, MapContent;
        public RectTransform[] ResponsiveControls;
        public ScrollRect Scroll;
        public CanvasGroup MapGroup;
        public Node[] Nodes;
        public Sprite Grass, Stone, Complete, Current, Locked, Goblin, LockedGoblin;
        public Sprite ProgressCurrent, ProgressLocked;
        public FriendDuelController FriendDuel;
        public SearchOpponentController Battle;
        public Toast Hint;
        private bool _boss, _cached, _busy;
        private bool[] _decorActive, _homeActive;
        private bool _navActive;
        private Vector3[] _controlScales;
        private int _progress = -1;
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
            RefreshMode();
        }

        private void Update()
        {
            RefreshMode();
            if (!_boss) return;
            Fit();
            if (_progress != BossCatalog.CurrentIndex) RefreshProgress();
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
            _progress = BossCatalog.CurrentIndex;
            foreach (var node in Nodes)
            {
                bool current = node.BossIndex == _progress;
                bool cleared = node.BossIndex < _progress;
                node.Platform.sprite = current || cleared ? Grass : Stone;
                node.Disc.sprite = current ? Current : cleared ? Complete : Locked;
                node.Face.sprite = current || cleared ? Goblin : LockedGoblin;
                node.Fight.gameObject.SetActive(current);
                node.ProgressPlate.sprite = current || cleared ? ProgressCurrent : ProgressLocked;
                node.ProgressPlate.color = cleared ? new Color(.65f, 1f, .35f) : Color.white;
                node.ProgressFace.sprite = current || cleared ? Goblin : LockedGoblin;
            }
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
                else Hint?.Show(node.BossIndex < BossCatalog.CurrentIndex
                    ? "Этот босс уже побеждён" : "Сначала победи предыдущего босса");
            }));
        }

        public void ShowRewardHint()
        {
            Hint?.Show("Награды этого острова появятся позже");
        }

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

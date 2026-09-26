using System.Collections;
using System.Collections.Generic;
using PushStars.Core;
using PushStars.UI;
using PushStars.UI.Layout;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>One-shot presentation of rewards already saved by the reward flow.</summary>
    public sealed class HomeRewardFlight : MonoBehaviour
    {
        private static long _aura, _trophies, _xp, _gems;
        private static string _destination;
        private readonly List<Arrival> _arrivals = new List<Arrival>();
        private RectTransform _overlay;

        private sealed class Arrival
        {
            public RectTransform Target;
            public Vector3 Scale;
            public float Hit = -10;
            // Counter that counts up as icons land (null for the XP tab, which has no number).
            public string Pill;
            public TMP_Text Number;
            public long From, Amount;
            public int Count, Landed;
        }

        private static readonly string[] Pills = { "AuraPill", "GemPill", "TrophyPill" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Clear();
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneLoaded += SceneLoaded;
        }

        public static void Clear()
        {
            _aura = _trophies = _xp = _gems = 0;
            _destination = null;
        }

        public static void QueueSummary(FightRewardFlow.Summary summary)
        {
            _aura = System.Math.Max(0, summary.Aura);
            _trophies = System.Math.Max(0, summary.Trophies);
            _xp = System.Math.Max(0, summary.EnergyXp);
        }

        public static void QueueGems(int amount) => _gems += System.Math.Max(0, amount);
        public static void QueueAura(int amount) => _aura += System.Math.Max(0, amount);

        public static void ReturnTo(string scene) => _destination = scene;

        private static void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (string.IsNullOrEmpty(_destination) ||
                (scene.name != _destination && scene.name != _destination + "Remote")) return;
            _destination = null;
            if (_aura <= 0 && _trophies <= 0 && _xp <= 0 && _gems <= 0) return;
            // Balances are already saved. Show them as they were until the icons land, from the
            // very first frame of home, so the counters never jump ahead of the flight.
            if (_aura > 0) HudBalanceHold.Set("AuraPill", CaseRewards.AuraBalance - _aura);
            if (_gems > 0) HudBalanceHold.Set("GemPill", CaseRewards.GemsBalance - _gems);
            if (_trophies > 0) HudBalanceHold.Set("TrophyPill", LocalProfile.Trophies - _trophies);
            var host = new GameObject("HomeRewardFlight");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<HomeRewardFlight>();
        }

        private IEnumerator Start()
        {
            long aura = _aura, trophies = _trophies, xp = _xp, gems = _gems;
            Clear(); // Consume before playback: leaving home early cannot replay the award.
            if (ScreenTransition.IsBusy)
            {
                // Burst out of the opening iris: the rewards come from where the last screen's
                // prize was, so the move from that screen into home reads as one motion.
                while (ScreenTransition.IsBusy && ScreenTransition.RevealProgress < .2f) yield return null;
            }
            else
            {
                // Let home navigation, safe area and layout settle before resolving visible targets.
                yield return null;
                yield return null;
                yield return new WaitForSecondsRealtime(.2f);
            }
            if (ScreenLayoutRoot.IsAnyEditing) { Destroy(gameObject); yield break; }
            Canvas.ForceUpdateCanvases();

            var canvasObject = new GameObject("RewardFlightOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(390, 844);
            scaler.matchWidthOrHeight = .5f;
            _overlay = (RectTransform)canvasObject.transform;
            Canvas.ForceUpdateCanvases();
            var theme = Resources.Load<PushStarsTheme>("PushStarsTheme");
            int groups = 0;
            AddGroup(aura, "AuraPill", "Icon", theme != null ? theme.IconAura : null, ref groups);
            AddGroup(trophies, "TrophyPill", "Cup", theme != null ? theme.IconCup : null, ref groups);
            AddGroup(gems, "GemPill", "Icon", theme != null ? theme.IconGem : null, ref groups);
            AddGroup(xp, "XpTrack", null, theme != null ? theme.IconXP : null, ref groups);
            if (groups > 0)
            {
                // The burst out of the opening screen has its own sound; arrivals then tick.
                GameAudio.Play(SoundCue.RewardBurst);
                yield return new WaitForSecondsRealtime(1.9f + groups * .18f);
                GameAudio.Play(SoundCue.RewardComplete);
            }
            Destroy(gameObject);
        }

        private void AddGroup(long amount, string targetName, string iconName, Sprite fallback, ref int groups)
        {
            if (amount <= 0) return;
            RectTransform target = null;
            foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var rect in root.GetComponentsInChildren<RectTransform>(false))
            {
                if (rect.name != targetName || !rect.gameObject.activeInHierarchy) continue;
                var ownerCanvas = rect.GetComponentInParent<Canvas>();
                if (ownerCanvas == null || !ownerCanvas.isActiveAndEnabled) continue;
                bool hidden = false;
                foreach (var group in rect.GetComponentsInParent<CanvasGroup>())
                    if (group.alpha <= .01f) hidden = true;
                if (!hidden) { target = rect; break; }
            }
            // XP lives in Profile; the home tab has no XP counter, so collect it into
            // the Profile navigation button when that progress bar is not visible.
            if (target == null && targetName == "XpTrack")
                foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var tab in root.GetComponentsInChildren<TabButton>(false))
                    if (tab.TabId == TabId.Profile && tab.gameObject.activeInHierarchy)
                        target = tab.transform as RectTransform;
            if (target == null) { HudBalanceHold.Release(targetName); return; } // No visible HUD here.
            var icon = iconName != null ? target.Find(iconName)?.GetComponent<Image>() : null;
            var sprite = icon != null && icon.sprite != null ? icon.sprite : fallback;
            if (sprite == null) { HudBalanceHold.Release(targetName); return; }
            int count = (int)System.Math.Min(9, amount);
            var arrival = new Arrival { Target = target, Scale = target.localScale, Amount = amount, Count = count };
            if (System.Array.IndexOf(Pills, targetName) >= 0 && HudBalanceHold.TryGet(targetName, out long from))
            {
                arrival.Pill = targetName; arrival.From = from;
                arrival.Number = target.Find("Number")?.GetComponent<TMP_Text>();
            }
            _arrivals.Add(arrival);
            var aim = icon != null ? icon.rectTransform : target;
            var origin = new Vector2((groups - 1) * 42, -30);
            float delay = groups++ * .18f;
            for (int i = 0; i < count; i++)
                StartCoroutine(Fly(sprite, origin, aim, arrival, i, count, delay));
            StartCoroutine(AmountLabel(amount, aim, delay + .95f));
        }

        private IEnumerator Fly(Sprite sprite, Vector2 origin, RectTransform target, Arrival arrival,
            int index, int count, float delay)
        {
            yield return new WaitForSecondsRealtime(delay + index * .045f);
            var image = new GameObject("FlyingReward", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(_overlay, false);
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.sizeDelta = new Vector2(32, 32);
            float angle = index * 2.39996f;
            var scatter = origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (24 + index * 4);
            float elapsed = 0;
            const float burst = .26f, duration = 1.05f;
            while (elapsed < duration && target != null && target.gameObject.activeInHierarchy)
            {
                elapsed += Time.unscaledDeltaTime;
                float flight = Mathf.Clamp01((elapsed - burst) / (duration - burst));
                float eased = flight * flight * flight;
                var end = Position(target);
                var control = (scatter + end) * .5f + new Vector2((index % 2 == 0 ? 1 : -1) * 65, -45);
                rect.anchoredPosition = elapsed < burst
                    ? Vector2.Lerp(origin, scatter, 1 - Mathf.Pow(1 - elapsed / burst, 3))
                    : (1 - eased) * (1 - eased) * scatter + 2 * (1 - eased) * eased * control + eased * eased * end;
                float scale = Mathf.Lerp(.35f, 1.1f, Mathf.Clamp01(elapsed / .14f)) * Mathf.Lerp(1, .42f, flight * flight);
                rect.localScale = Vector3.one * scale;
                rect.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(flight * Mathf.PI) * (index % 2 == 0 ? 22 : -22));
                yield return null;
            }
            Destroy(image.gameObject);
            if (target != null && target.gameObject.activeInHierarchy)
            {
                arrival.Hit = Time.unscaledTime;
                if (index % 2 == 0) GameAudio.Play(SoundCue.RewardTick, 1 + .25f * index / count);
            }
            Land(arrival);
        }

        /// <summary>Each landing icon advances its counter; the last one hands it back to live data.</summary>
        private static void Land(Arrival arrival)
        {
            if (arrival.Pill == null) return;
            arrival.Landed++;
            long shown = arrival.From + arrival.Amount * arrival.Landed / arrival.Count;
            if (arrival.Landed >= arrival.Count) HudBalanceHold.Release(arrival.Pill);
            else HudBalanceHold.Set(arrival.Pill, shown);
            if (arrival.Number != null) arrival.Number.text = shown.ToString("N0");
        }

        private IEnumerator AmountLabel(long amount, RectTransform target, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (target == null) yield break;
            var label = new GameObject("RewardAmount", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(_overlay, false);
            FightTypography.Apply(label, FightTypography.Role.Caption);
            label.text = "+" + amount;
            label.fontSize = 19;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.rectTransform.sizeDelta = new Vector2(120, 32);
            float elapsed = 0;
            while (elapsed < .8f && target != null && target.gameObject.activeInHierarchy)
            {
                elapsed += Time.unscaledDeltaTime;
                var position = Position(target) + new Vector2(0, -35 + elapsed * 16);
                position.x = Mathf.Clamp(position.x, _overlay.rect.xMin + 62, _overlay.rect.xMax - 62);
                label.rectTransform.anchoredPosition = position;
                label.color = new Color(1, 1, 1, 1 - Mathf.InverseLerp(.4f, .8f, elapsed));
                yield return null;
            }
            Destroy(label.gameObject);
        }

        private Vector2 Position(RectTransform target)
        {
            var canvas = target.GetComponentInParent<Canvas>().rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var screen = RectTransformUtility.WorldToScreenPoint(camera, target.TransformPoint(target.rect.center));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_overlay, screen, null, out var point);
            return point;
        }

        private void LateUpdate()
        {
            foreach (var arrival in _arrivals)
                if (arrival.Target != null)
                {
                    float t = Mathf.Clamp01((Time.unscaledTime - arrival.Hit) / .24f);
                    arrival.Target.localScale = arrival.Scale * (1 + .13f * Mathf.Sin(t * Mathf.PI));
                }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            HudBalanceHold.ReleaseAll();
            foreach (var arrival in _arrivals)
                if (arrival.Target != null) arrival.Target.localScale = arrival.Scale;
            if (_overlay != null) Destroy(_overlay.gameObject);
        }
    }
}

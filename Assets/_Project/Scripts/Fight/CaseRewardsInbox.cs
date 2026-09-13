using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PushStars.Core;
using PushStars.UI;

namespace PushStars.Fight
{
    /// <summary>Entry point for unclaimed cases after returning home or restarting the app.</summary>
    public sealed class CaseRewardsInbox : MonoBehaviour
    {
        private Button _button;
        private TextMeshProUGUI _label;
        private int _lastCount = -1;
        private long _lastBalance = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Main" && scene.name != "MainRemote") return;
            foreach (var root in scene.GetRootGameObjects())
            {
                var shell = root.GetComponentInChildren<MainShellView>(true);
                if (shell == null) continue;
                var canvas = shell.GetComponentInParent<Canvas>();
                if (canvas == null) canvas = root.GetComponentInChildren<Canvas>();
                if (canvas != null && canvas.GetComponent<CaseRewardsInbox>() == null)
                    canvas.gameObject.AddComponent<CaseRewardsInbox>();
                return;
            }
        }

        private void Start()
        {
            var safe = new GameObject("CaseInboxSafeArea", typeof(RectTransform));
            safe.transform.SetParent(transform, false);
            var safeRect = (RectTransform)safe.transform;
            safeRect.anchorMin = Vector2.zero; safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = safeRect.offsetMax = Vector2.zero;
            safe.AddComponent<SafeAreaFitter>();
            var card = new GameObject("CaseInbox", typeof(RectTransform), typeof(Image), typeof(Button));
            card.transform.SetParent(safe.transform, false);
            var rect = (RectTransform)card.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, .5f);
            rect.pivot = new Vector2(1, .5f);
            rect.anchoredPosition = new Vector2(-10, 0);
            rect.sizeDelta = new Vector2(106, 82);
            var background = card.GetComponent<Image>();
            background.color = new Color32(26, 24, 67, 240);
            _button = card.GetComponent<Button>();
            _button.targetGraphic = background;
            _button.onClick.AddListener(Open);

            var icon = new GameObject("Case", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            icon.transform.SetParent(card.transform, false);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(.5f, .7f);
            icon.rectTransform.sizeDelta = new Vector2(46, 40);
            icon.sprite = Resources.Load<Sprite>("Rewards/CaseCommon");
            icon.preserveAspect = true; icon.raycastTarget = false;
            _label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            _label.transform.SetParent(card.transform, false);
            _label.rectTransform.anchorMin = Vector2.zero;
            _label.rectTransform.anchorMax = new Vector2(1, .45f);
            _label.rectTransform.offsetMin = new Vector2(3, 2);
            _label.rectTransform.offsetMax = new Vector2(-3, -2);
            FightTypography.Apply(_label, FightTypography.Role.Caption);
            _label.fontSize = 12;
            _label.alignment = TextAlignmentOptions.Center;
            _label.raycastTarget = false;
            Refresh();
        }

        private void Update()
        {
            if (_button == null) return;
            if (_lastCount != CaseRewards.PendingCount || _lastBalance != CaseRewards.GemsBalance) Refresh();
            _button.gameObject.SetActive(_lastCount > 0 || _lastBalance > 0);
        }

        private void Refresh()
        {
            _lastCount = CaseRewards.PendingCount;
            _lastBalance = CaseRewards.GemsBalance;
            _label.text = $"КЕЙСЫ · {_lastCount}\nКРИСТАЛЛЫ · {_lastBalance}";
            _button.interactable = _lastCount > 0;
        }

        private void Open()
        {
            if (!CaseRewards.HasPendingCase) return;
            FightScreenNavigation.OpenCase(CaseRewards.Pending.Id, gameObject.scene.name);
        }
    }
}

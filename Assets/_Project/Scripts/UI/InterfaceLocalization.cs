using System.Collections.Generic;
using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PushStars.UI
{
    /// <summary>Localizes existing authored labels and runtime dialogs before the canvas draws.
    /// Keeps English source copy so switching back never translates an already translated label.</summary>
    public sealed class InterfaceLocalization : MonoBehaviour
    {
        private sealed class Label
        {
            public TMP_Text Text;
            public string Source, Output, Language;
            public bool AutoSize, Translated;
            public float Min, Max, Size;
        }
        private readonly Dictionary<int, Label> _labels = new Dictionary<int, Label>();
        private readonly List<int> _removed = new List<int>();
        private bool _refreshing;
        private static InterfaceLocalization _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => _instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (_instance != null) return;
            var root = new GameObject("InterfaceLocalization");
            DontDestroyOnLoad(root);
            _instance = root.AddComponent<InterfaceLocalization>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += SceneLoaded;
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(TextChanged);
            Canvas.preWillRenderCanvases += Refresh;
            Localization.LanguageChanged += Refresh;
            Discover();
        }
        private void OnDisable()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(TextChanged);
            Canvas.preWillRenderCanvases -= Refresh;
            Localization.LanguageChanged -= Refresh;
        }
        private void SceneLoaded(Scene scene, LoadSceneMode mode) { Discover(); Refresh(); }
        private void Discover()
        {
            foreach (var text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Track(text);
        }
        private static bool UserData(TMP_Text text)
        {
            var input = text.GetComponentInParent<TMP_InputField>(true);
            if (input != null && input.textComponent == text) return true;
            string name = text.name;
            return name == "PlayerName" || name == "OpponentName" || name == "FriendName"
                || name == "Friend" || name == "Nickname" || name == "Code" || name == "LanguageLabel"
                || name == "NativeLanguageName" || name == "Name" || name == "NameDarkLayer";
        }
        private Label Track(TMP_Text text)
        {
            if (text == null || UserData(text)) return null;
            int id = text.GetInstanceID();
            if (!_labels.TryGetValue(id, out var label))
            {
                label = new Label { Text = text, Source = text.text, AutoSize = text.enableAutoSizing,
                    Min = text.fontSizeMin, Max = text.fontSizeMax, Size = text.fontSize };
                _labels.Add(id, label);
            }
            return label;
        }
        private void TextChanged(Object changed)
        {
            if (_refreshing || !(changed is TMP_Text text)) return;
            var label = Track(text);
            if (label == null) return;
            string source = text.text;
            Apply(label);
            if (text.text == source) return;
            // Newly created dialogs announce themselves after TMP generates their first mesh.
            // Rebuild that mesh immediately so the first visible frame uses the selected language.
            _refreshing = true;
            try { text.ForceMeshUpdate(); }
            finally { _refreshing = false; }
        }
        private void Apply(Label label)
        {
            var text = label.Text;
            if (text.text == label.Output && label.Language == Localization.Language) return;
            if (text.text != label.Output) label.Source = text.text;
            label.Output = Localization.Text(label.Source);
            label.Language = Localization.Language;
            bool translated = label.Output != label.Source;
            if (translated || label.Translated)
            {
                text.enableAutoSizing = translated || label.AutoSize;
                text.fontSizeMin = translated ? Mathf.Min(label.Min, label.Size * .65f) : label.Min;
                text.fontSizeMax = translated && !label.AutoSize ? label.Size : label.Max;
                if (!translated && !label.AutoSize) text.fontSize = label.Size;
            }
            label.Translated = translated;
            if (text.text != label.Output) text.text = label.Output;
        }
        public void Refresh()
        {
            if (_refreshing) return;
            _refreshing = true;
            try
            {
                _removed.Clear();
                foreach (var item in _labels)
                {
                    var label = item.Value;
                    if (label.Text == null) _removed.Add(item.Key);
                    else if (label.Language != Localization.Language || label.Text.text != label.Output)
                    {
                        if (UserData(label.Text)) _removed.Add(item.Key);
                        else Apply(label);
                    }
                }
                foreach (int id in _removed) _labels.Remove(id);
            }
            finally { _refreshing = false; }
        }
    }
}

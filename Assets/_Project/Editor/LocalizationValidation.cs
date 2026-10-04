using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PushStars.Core;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>Catalog invariants, persistence and source-preserving runtime label regression.</summary>
    public static class LocalizationValidation
    {
        [Serializable] private sealed class Catalog { public Entry[] entries; }
        [Serializable] private sealed class Entry { public string en, ru, ptBR; }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        [MenuItem("Tools/Push Stars/Validate Interface Languages")]
        public static void Run()
        {
            bool existed = PlayerPrefs.HasKey("settings.language");
            string original = PlayerPrefs.GetString("settings.language");
            try
            {
                var catalog = JsonUtility.FromJson<Catalog>(Resources.Load<TextAsset>("Localization/interface").text);
                Require(catalog.entries.Length >= 500, "Interface catalog was not loaded");
                foreach (var entry in catalog.entries)
                {
                    Require(!string.IsNullOrWhiteSpace(entry.en) && !string.IsNullOrWhiteSpace(entry.ru)
                        && !string.IsNullOrWhiteSpace(entry.ptBR), "Empty translation: " + entry.en);
                    string Slots(string value) => string.Join(",", Regex.Matches(value, @"\{\d+\}")
                        .Cast<Match>().Select(m => m.Value).OrderBy(s => s));
                    Require(Slots(entry.en) == Slots(entry.ru) && Slots(entry.en) == Slots(entry.ptBR), "Format slots changed: " + entry.en);
                    string Tags(string value) => string.Join(",", Regex.Matches(value, @"<[^>]+>")
                        .Cast<Match>().Select(m => m.Value).OrderBy(s => s));
                    Require(Tags(entry.en) == Tags(entry.ru) && Tags(entry.en) == Tags(entry.ptBR), "Rich text tags changed: " + entry.en);
                }
                foreach (string language in new[] { "en", "ru", "pt-BR" })
                {
                    new PlayerPrefsSettingsStore().Language = language;
                    Require(new PlayerPrefsSettingsStore().Language == language, "Preference did not survive a new store");
                    Require(Localization.Text("SETTINGS") == (language == "en" ? "SETTINGS" : language == "ru"
                        ? catalog.entries.First(e => e.en == "SETTINGS").ru : catalog.entries.First(e => e.en == "SETTINGS").ptBR), "Settings copy mismatch");
                    string rich = Localization.Text("UNLOCKS AT 300 <sprite name=\"aura\"> · WIN FIGHTS TO FILL THE BAR");
                    Require(rich.Contains("300") && rich.Contains("<sprite name=\"aura\">"), "Sprite tag or amount changed");
                    string message = Localization.Text("Send this code to your friend.\nExpires in 4:03");
                    Require(message.Contains("4:03") && message.Contains("\n"), "Invitation timer changed");
                    Require(Localization.Text("custom_player_923") == "custom_player_923", "Unknown text was translated");
                    Require(Localization.Text("  SETTINGS  ").StartsWith("  ") && Localization.Text("  SETTINGS  ").EndsWith("  "), "Copy spacing was lost");
                    if (language != "en")
                        Require(!Localization.Text("Total workout:\n3 sets, 60 sec. rest").Contains("rest"), "Nested workout copy remained English");
                }
                new PlayerPrefsSettingsStore().Language = "PT-br";
                Require(new PlayerPrefsSettingsStore().Language == "pt-BR", "Locale casing was not normalized");
                new PlayerPrefsSettingsStore().Language = "pt";
                Require(new PlayerPrefsSettingsStore().Language == "pt-BR", "Portuguese compatibility alias failed");
                new PlayerPrefsSettingsStore().Language = "unsupported";
                Require(new PlayerPrefsSettingsStore().Language == "en", "Unknown locale did not fall back to English");
                Directory.CreateDirectory("output/localization");
                File.WriteAllText("output/localization/catalog-validation.txt", "PASS: " + catalog.entries.Length + " entries; placeholders, rich text, all three preferences, timer templates, fallback and spacing.\n");
                Debug.Log("[Localization] Catalog and preferences passed.");
            }
            finally
            {
                if (existed) PlayerPrefs.SetString("settings.language", original); else PlayerPrefs.DeleteKey("settings.language");
                PlayerPrefs.Save(); Localization.NotifyLanguageChanged();
            }
        }

        public static void RuntimeLabels()
        {
            Require(EditorApplication.isPlaying, "Run label validation in Play Mode");
            var bridge = Object.FindFirstObjectByType<InterfaceLocalization>();
            Require(bridge != null, "Localization was not initialized before scene load");
            string original = new PlayerPrefsSettingsStore().Language;
            var root = new GameObject("LocalizationRegression", typeof(RectTransform), typeof(Canvas));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            TMP_Text Make(string name, string source)
            {
                var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
                label.transform.SetParent(root.transform, false);
                label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSetup.RegularAsset);
                label.text = source;
                label.ForceMeshUpdate();
                return label;
            }
            try
            {
                new PlayerPrefsSettingsStore().Language = "en";
                var label = Make("Caption", "BATTLE");
                var player = Make("PlayerName", "BATTLE");
                var input = new GameObject("Input", typeof(RectTransform), typeof(TMP_InputField)).GetComponent<TMP_InputField>();
                input.transform.SetParent(root.transform, false);
                var typed = Make("TypedText", "BATTLE"); typed.transform.SetParent(input.transform, false); input.textComponent = typed;
                var hidden = Make("HiddenCaption", "SETTINGS"); hidden.gameObject.SetActive(false);
                foreach (string language in new[] { "ru", "pt-BR", "en" })
                {
                    new PlayerPrefsSettingsStore().Language = language; bridge.Refresh();
                    var fresh = Make("FreshCaption", "BATTLE");
                    Require(fresh.text == Localization.Text("BATTLE") && fresh.textInfo.characterCount == fresh.text.Length,
                        "A new label's first mesh did not use the selected language");
                    Object.DestroyImmediate(fresh.gameObject);
                    Require(label.text == Localization.Text("BATTLE"), "Active label did not switch");
                    Require(hidden.text == Localization.Text("SETTINGS"), "Inactive label did not switch");
                    Require(player.text == "BATTLE" && typed.text == "BATTLE", "User content changed");
                    label.text = "READY"; bridge.Refresh();
                    Require(label.text == Localization.Text("READY"), "Dynamic assignment did not translate");
                    label.text = "BATTLE"; bridge.Refresh();
                }
                Require(label.text == "BATTLE", "English source did not restore after switching back");
                Object.DestroyImmediate(root);
                bridge.Refresh();
                File.WriteAllText("output/localization/runtime-validation.txt", "PASS: runtime initialization, live switching, dynamic and inactive labels, English restoration, nickname and input protection, destroyed labels.\n");
                Debug.Log("[Localization] Runtime labels passed.");
            }
            finally { if (root != null) Object.DestroyImmediate(root); new PlayerPrefsSettingsStore().Language = original; }
        }
    }
}

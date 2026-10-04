using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>Shared interface catalog. English authored copy is the stable lookup key.</summary>
    public static class Localization
    {
        [Serializable] private sealed class Catalog { public Entry[] entries; }
        [Serializable] private sealed class Entry { public string en, ru, ptBR; }
        private sealed class Template { public Entry Entry; public Regex Pattern; }
        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Template> Templates = new List<Template>();
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();
        private static readonly Regex Spaces = new Regex("[ \\t]+");
        private static readonly Regex Slots = new Regex(@"\{(\d+)\}");
        private static readonly Regex Tags = new Regex(@"(<[^>]+>)");
        private static bool _loaded;
        private static string _cachedLanguage;
        private static string _currentLanguage;
        public static event Action LanguageChanged;
        public static string Language => _currentLanguage ?? (_currentLanguage = new PlayerPrefsSettingsStore().Language);
        public static int EntryCount { get { Load(); return Entries.Count; } }

        public static string NormalizeLanguage(string language)
        {
            if (string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)) return "ru";
            if (string.Equals(language, "pt-BR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(language, "pt", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
            return "en";
        }

        public static string NativeName(string language) => NormalizeLanguage(language) == "ru" ? "Русский"
            : NormalizeLanguage(language) == "pt-BR" ? "Português (Brasil)" : "English";

        public static void NotifyLanguageChanged()
        {
            _currentLanguage = new PlayerPrefsSettingsStore().Language;
            Cache.Clear();
            _cachedLanguage = Language;
            LanguageChanged?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntime()
        {
            _currentLanguage = _cachedLanguage = null;
            Cache.Clear();
            LanguageChanged = null;
        }

        private static string Key(string source) => Spaces.Replace(source.Trim(), " ");

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            var asset = Resources.Load<TextAsset>("Localization/interface");
            if (asset == null) { Debug.LogError("Interface localization catalog is missing."); return; }
            var catalog = JsonUtility.FromJson<Catalog>(asset.text);
            foreach (var entry in catalog.entries)
            {
                string key = Key(entry.en);
                Entries.Add(key, entry);
                if (!Slots.IsMatch(key)) continue;
                string pattern = "^";
                int offset = 0;
                foreach (Match slot in Slots.Matches(key))
                {
                    pattern += Regex.Escape(key.Substring(offset, slot.Index - offset));
                    pattern += "(?<p" + slot.Groups[1].Value + ">.+?)";
                    offset = slot.Index + slot.Length;
                }
                pattern += Regex.Escape(key.Substring(offset)) + "$";
                Templates.Add(new Template { Entry = entry, Pattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) });
            }
            // More specific copy must win over a generic suffix such as "{0} sets, {1}".
            Templates.Sort((a, b) => Slots.Replace(b.Entry.en, "").Length.CompareTo(Slots.Replace(a.Entry.en, "").Length));
        }

        public static string Text(string source)
        {
            if (string.IsNullOrEmpty(source) || Language == "en") return source;
            if (_cachedLanguage != Language) { Cache.Clear(); _cachedLanguage = Language; }
            if (Cache.TryGetValue(source, out var cached)) return cached;
            Load();
            string key = Key(source), translated = null;
            if (Entries.TryGetValue(key, out var entry)) translated = Value(entry);
            else foreach (var template in Templates)
            {
                var match = template.Pattern.Match(key);
                if (!match.Success) continue;
                translated = Slots.Replace(Value(template.Entry), slot => match.Groups["p" + slot.Groups[1].Value].Value);
                break;
            }
            if (translated == null && Tags.IsMatch(source))
            {
                var parts = Tags.Split(source);
                for (int i = 0; i < parts.Length; i++) if (!parts[i].StartsWith("<", StringComparison.Ordinal)) parts[i] = Text(parts[i]);
                translated = string.Concat(parts);
            }
            if (translated == null && source.Contains("\n"))
            {
                var lines = source.Split('\n');
                for (int i = 0; i < lines.Length; i++) lines[i] = Text(lines[i]);
                translated = string.Join("\n", lines);
            }
            if (translated == null) translated = source;
            else if (UppercaseCopy(key))
            {
                var parts = Tags.Split(translated);
                for (int i = 0; i < parts.Length; i++) if (!parts[i].StartsWith("<", StringComparison.Ordinal)) parts[i] = parts[i].ToUpperInvariant();
                translated = string.Concat(parts);
            }
            if (translated != source)
            {
                int start = 0, end = source.Length;
                while (start < end && char.IsWhiteSpace(source[start])) start++;
                while (end > start && char.IsWhiteSpace(source[end - 1])) end--;
                translated = source.Substring(0, start) + translated.Trim() + source.Substring(end);
            }
            if (Cache.Count >= 1024) Cache.Clear();
            Cache[source] = translated;
            return translated;
        }

        private static string Value(Entry entry) => Language == "ru" ? entry.ru : entry.ptBR;
        private static bool UppercaseCopy(string value)
        {
            bool letters = false;
            foreach (char c in Tags.Replace(value, ""))
            {
                if (char.IsLower(c)) return false;
                if (char.IsLetter(c)) letters = true;
            }
            return letters;
        }
    }
}

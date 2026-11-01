using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PushStars.Core
{
    /// <summary>Which chosen nicknames other players may see: checked here as the name is typed and
    /// when another phone's name arrives, and on the server when it is saved and shown. Keep the
    /// lists and the steps identical to functions/src/nameFilter.js — nameFilter.test.js compares
    /// the lists, and it and NameFilterTests.cs pin the same samples.</summary>
    public static class NameFilter
    {
        public const int MinChars = 2, MaxChars = 18;

        private static readonly string[] Anywhere =
        {
            // English
            "fuck", "shit", "bitch", "cunt", "asshole", "ashole", "nigg", "faggot", "retard", "whore", "slut",
            "porn", "penis", "vagina", "dildo", "wank", "rapist", "pussy", "pusy", "blowjob", "motherf",
            // Hate
            "hitler", "nazi", "isis", "heilh", "sieghe",
            // Russian
            "хуй", "хуе", "хуя", "пизд", "ебат", "ебан", "ебал", "ебл", "долбоеб", "уеб", "заеб", "бляд", "блят",
            "мудак", "мудил", "пидор", "пидар", "педик", "гандон", "шлюх", "залуп", "дроч", "нигер", "хохол", "жид",
            // Russian in Latin letters
            "pizd", "blyat", "blyad", "mudak", "pidor", "pidar", "huesos", "gandon", "shluh", "zalup",
            // Portuguese
            "caralho", "buceta", "arrombad", "piroca", "xoxota", "vagabund", "cuzao", "filhodaput", "bosta",
            // Passing for the game or its staff
            "admin", "moderator", "pushupstars",
        };

        private static readonly string[] WholeWords =
        {
            "ass", "arse", "dick", "cock", "cum", "tits", "boob", "boobs", "rape", "fag", "fags", "kys", "nude", "nudes",
            "spic", "kike", "chink", "coon", "tranny", "bastard", "sex", "kkk",
            "бля", "сука", "суки", "сучка", "хер", "нацист", "гей", "kurva", "suka", "huy", "hui", "nahui", "blya", "ebat", "eban", "ebanat",
            "puta", "porra", "merda", "foda", "viado", "cu",
            "apple", "google", "support", "staff", "official", "developer", "dev",
        };

        // Hate numbers read as they are, before digits fold into letters.
        private static readonly string[] Numbers = { "1488" };

        private static readonly Dictionary<char, char> Digits = new Dictionary<char, char>
        { ['0'] = 'o', ['1'] = 'i', ['3'] = 'e', ['4'] = 'a', ['5'] = 's', ['7'] = 't', ['8'] = 'b', ['9'] = 'g' };
        private static readonly Dictionary<char, char> ToLatin = new Dictionary<char, char>
        { ['а'] = 'a', ['в'] = 'b', ['е'] = 'e', ['к'] = 'k', ['м'] = 'm', ['н'] = 'h', ['о'] = 'o', ['р'] = 'p', ['с'] = 'c', ['т'] = 't', ['у'] = 'y', ['х'] = 'x' };
        private static readonly Dictionary<char, char> ToCyrillic = new Dictionary<char, char>
        { ['a'] = 'а', ['b'] = 'в', ['c'] = 'с', ['e'] = 'е', ['h'] = 'н', ['k'] = 'к', ['m'] = 'м', ['o'] = 'о', ['p'] = 'р', ['t'] = 'т', ['x'] = 'х', ['y'] = 'у' };

        private static readonly string[] Roots = Anywhere.Select(Plain).ToArray();
        private static readonly HashSet<string> Whole = new HashSet<string>(WholeWords.Select(Plain));

        /// <summary>Lowercase, accents off (й becomes и, ё becomes е, in the lists as well), letters
        /// and digits only, digits read as letters.</summary>
        private static string Plain(string text)
        {
            var plain = new StringBuilder(text.Length);
            // Normalize refuses broken Unicode, and a peer's name can be anything.
            string decomposed;
            try { decomposed = text.Normalize(NormalizationForm.FormD); }
            catch (ArgumentException) { decomposed = text; }
            foreach (char c in decomposed.ToLowerInvariant())
            {
                if (char.IsDigit(c)) { if (Digits.TryGetValue(c, out char letter)) plain.Append(letter); }
                else if (char.IsLetter(c)) plain.Append(c);
            }
            return plain.ToString();
        }

        private static string Mapped(string text, Dictionary<char, char> map)
        {
            var mapped = new StringBuilder(text.Length);
            foreach (char c in text) mapped.Append(map.TryGetValue(c, out char other) ? other : c);
            return mapped.ToString();
        }

        private static string Collapsed(string text)
        {
            var collapsed = new StringBuilder(text.Length);
            foreach (char c in text)
                if (collapsed.Length == 0 || collapsed[collapsed.Length - 1] != c) collapsed.Append(c);
            return collapsed.ToString();
        }

        /// <summary>Words as a reader sees them: split at anything else than a letter or digit, and at aB in camelCase.</summary>
        private static IEnumerable<string> Words(string name)
        {
            var word = new StringBuilder();
            char previous = '\0';
            foreach (char c in name)
            {
                if (!char.IsLetterOrDigit(c))
                {
                    if (word.Length > 0) yield return word.ToString();
                    word.Clear(); previous = '\0';
                    continue;
                }
                if (char.IsUpper(c) && previous != '\0' && !char.IsUpper(previous))
                {
                    yield return word.ToString();
                    word.Clear();
                }
                word.Append(c);
                previous = c;
            }
            if (word.Length > 0) yield return word.ToString();
        }

        private static string[] Folds(string text) => new[] { text, Mapped(text, ToLatin), Mapped(text, ToCyrillic) };

        public static bool HasAllowedCharacters(string name)
        {
            foreach (char c in name ?? "")
                if (!char.IsLetterOrDigit(c) && c != ' ' && c != '_' && c != '-' && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) return false;
            return !string.IsNullOrEmpty(name);
        }

        public static bool IsObjectionable(string name)
        {
            string text = name ?? "";
            if (Numbers.Any(number => text.Contains(number))) return true;
            foreach (string fold in Folds(Plain(text)))
            {
                string squeezed = Collapsed(fold);
                if (Roots.Any(root => fold.Contains(root) || squeezed.Contains(root))) return true;
                if (Whole.Contains(fold)) return true;   // "s u k a", "a.s.s"
            }
            foreach (string word in Words(text))
                foreach (string fold in Folds(Plain(word)))
                    if (Whole.Contains(fold)) return true;
            return false;
        }

        /// <summary>What another phone calls itself, as this phone shows it: a live duel's peer
        /// introduces itself directly, without the server, so its word is not taken for it. Falls
        /// back to the name the server would show.</summary>
        public static string Shown(string name, string uid)
        {
            string text = (name ?? "").Trim();
            if (text.Length > 20) text = text.Substring(0, 20);
            bool usable = !PlayerNames.IsPlaceholder(text) && HasAllowedCharacters(text) && !IsObjectionable(text);
            return usable ? text : PlayerNames.For(uid ?? "");
        }
    }
}

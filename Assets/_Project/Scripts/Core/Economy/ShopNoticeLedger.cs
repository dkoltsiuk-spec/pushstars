using System;
using System.Collections.Generic;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>Persists which shop offers and unlocks have been seen, without touching the wallet.</summary>
    public sealed class ShopNoticeLedger
    {
        [Serializable]
        private sealed class Save
        {
            public List<string> seen = new List<string>();
        }

        private HashSet<string> _seen;
        private readonly Action<string> _persist;

        public ShopNoticeLedger(string json, Action<string> persist)
        {
            _persist = persist;
            Save save = null;
            if (!string.IsNullOrEmpty(json))
            {
                try { save = JsonUtility.FromJson<Save>(json); }
                catch (ArgumentException) { }
            }
            _seen = new HashSet<string>(save?.seen ?? new List<string>(), StringComparer.Ordinal);
        }

        public bool HasUnseen(IEnumerable<string> notices)
        {
            foreach (string notice in notices)
                if (!_seen.Contains(notice)) return true;
            return false;
        }

        public void MarkSeen(IEnumerable<string> notices)
        {
            var next = new HashSet<string>(notices, StringComparer.Ordinal);
            if (_seen.SetEquals(next)) return;
            var save = new Save { seen = new List<string>(next) };
            save.seen.Sort(StringComparer.Ordinal);
            _persist(JsonUtility.ToJson(save));
            _seen = next;
        }
    }
}

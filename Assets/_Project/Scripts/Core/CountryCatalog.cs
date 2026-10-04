using System;
using System.Collections.Generic;
using UnityEngine;

namespace PushStars.Core
{
    public static class CountryCatalog
    {
        [Serializable] public sealed class Entry
        {
            public string code, en, ru, ptBR;
            public string DisplayName(string language) => language=="ru"?ru:language=="pt-BR"?ptBR:en;
        }
        [Serializable] private sealed class Data { public Entry[] entries; }
        private static Entry[] _entries;
        private static readonly Dictionary<string,Entry> ByCode=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
        public static IReadOnlyList<Entry> Entries { get { Load();return _entries; } }
        private static void Load()
        {
            if(_entries!=null)return;
            var json=Resources.Load<TextAsset>("CountryCatalog");
            _entries=json==null?Array.Empty<Entry>():JsonUtility.FromJson<Data>(json.text).entries;
            foreach(var entry in _entries)ByCode[entry.code]=entry;
        }
        public static string Normalize(string code)
        {
            Load();code=code?.Trim().ToUpperInvariant();
            return code!=null && ByCode.ContainsKey(code)?code:"";
        }
        public static Entry Find(string code)
        {Load();return code!=null && ByCode.TryGetValue(code,out var entry)?entry:null;}
    }
}

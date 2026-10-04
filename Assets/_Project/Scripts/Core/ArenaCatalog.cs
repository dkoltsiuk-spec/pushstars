using System;
using UnityEngine;

namespace PushStars.Core
{
    public enum ArenaAccess { Free, Purchase, Aura }

    [Serializable]
    public sealed class ArenaDefinition
    {
        public string Id, Title;
        public Sprite Home;
        public Sprite Battle;
        public ArenaAccess Access;
        public int Price;
    }

    [CreateAssetMenu(menuName = "Push Stars/Arena Catalog")]
    public sealed class ArenaCatalog : ScriptableObject
    {
        public const string DefaultId = "crystal";
        public ArenaDefinition[] Arenas = Array.Empty<ArenaDefinition>();
        public Sprite SelectionPanel;
        private static ArenaCatalog _instance;
        public static ArenaCatalog Instance => _instance != null ? _instance : (_instance = Resources.Load<ArenaCatalog>("ArenaCatalog"));
        public ArenaDefinition Find(string id) => Array.Find(Arenas, a => a.Id == id);
        public static string Normalize(string id) => Instance != null && Instance.Find(id) != null ? id : DefaultId;
        public static ArenaDefinition Get(string id) => Instance != null ? Instance.Find(Normalize(id)) : null;
    }

    public static class ArenaProfile
    {
        public const string SelectionKey = "arena.selected";
        public static event Action Changed;
        public static bool Owns(string id)
        {
            var arena = ArenaCatalog.Instance != null ? ArenaCatalog.Instance.Find(id) : null;
            return arena != null && (arena.Access == ArenaAccess.Free || PlayerPrefs.GetInt("arena.owned." + arena.Id, 0) == 1);
        }
        public static string SelectedId
        {
            get
            {
                var id = ArenaCatalog.Normalize(PlayerPrefs.GetString(SelectionKey, ArenaCatalog.DefaultId));
                return Owns(id) ? id : ArenaCatalog.DefaultId;
            }
        }
        public static bool Select(string id)
        {
            if (ArenaCatalog.Instance == null || ArenaCatalog.Instance.Find(id) == null || !Owns(id)) return false;
            PlayerPrefs.SetString(SelectionKey, id); PlayerPrefs.Save(); Changed?.Invoke(); return true;
        }
        // Called only after a validated grant/purchase; selecting an item never spends currency.
        public static void Grant(string id)
        {
            if (ArenaCatalog.Instance == null || ArenaCatalog.Instance.Find(id) == null) return;
            PlayerPrefs.SetInt("arena.owned." + id, 1); PlayerPrefs.Save(); Changed?.Invoke();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetEvents() => Changed = null;
    }
}

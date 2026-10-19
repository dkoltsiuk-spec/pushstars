using System;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>Free: everyone has it. Purchase: Price in gems. Aura: Price is the peak Aura that
    /// unlocks it. Island: free once the boss chapter named by Island is cleared.</summary>
    public enum ArenaAccess { Free, Purchase, Aura, Island }

    [Serializable]
    public sealed class ArenaDefinition
    {
        public string Id, Title;
        public Sprite Home;
        public Sprite Battle;
        public ArenaAccess Access;
        public int Price;
        public string Island;
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
        /// <summary>Bought arenas live in the gem ledger; "arena.owned.*" grants from before the
        /// ledger kept them are still honoured.</summary>
        public static bool Owns(string id)
        {
            var arena = ArenaCatalog.Instance != null ? ArenaCatalog.Instance.Find(id) : null;
            return arena != null && (arena.Access == ArenaAccess.Free || EarnedOwnership(arena) || CaseRewards.OwnsArena(arena.Id) ||
                PlayerPrefs.GetInt(LegacyOwnedPrefix + arena.Id, 0) == 1);
        }
        /// <summary>Aura and island arenas follow the peak Aura and the boss ladder themselves, so
        /// nothing is saved when they unlock and earlier progress counts too.</summary>
        private static bool EarnedOwnership(ArenaDefinition arena) =>
            arena.Access == ArenaAccess.Aura && CaseRewards.AuraPeak >= arena.Price ||
            arena.Access == ArenaAccess.Island && BossCatalog.IsChapterCleared(BossCatalog.FindChapter(arena.Island));
        /// <summary>What a locked arena's card says: the gem price, the Aura still missing, or the
        /// island that gives it.</summary>
        public static string LockedLabel(ArenaDefinition arena)
        {
            switch (arena.Access)
            {
                case ArenaAccess.Purchase: return $"{arena.Price:N0} <sprite name=\"gem\">";
                case ArenaAccess.Aura: return $"{AuraFormat.Short(System.Math.Max(0, arena.Price - CaseRewards.AuraPeak))} <sprite name=\"aura\"> TO GO";
                case ArenaAccess.Island: return BossCatalog.FindChapter(arena.Island)?.Title ?? "BOSS ISLAND";
                default: return "";
            }
        }
        private const string LegacyOwnedPrefix = "arena.owned.";
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
        /// <summary>Spends the catalog price in gems on a Purchase arena; ownership and the spend
        /// share one ledger save. False when it is not for sale, already owned or gems are short.</summary>
        public static bool TryBuy(string id)
        {
            var arena = ArenaCatalog.Instance != null ? ArenaCatalog.Instance.Find(id) : null;
            if (arena == null || arena.Access != ArenaAccess.Purchase || Owns(id) || !CaseRewards.TryBuyArena(arena.Id, arena.Price)) return false;
            Changed?.Invoke(); return true;
        }
        // Called only after a validated grant/purchase; selecting an item never spends currency.
        public static void Grant(string id)
        {
            if (ArenaCatalog.Instance == null || ArenaCatalog.Instance.Find(id) == null) return;
            if (CaseRewards.TryBuyArena(id, 0)) Changed?.Invoke();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetEvents() => Changed = null;
    }
}

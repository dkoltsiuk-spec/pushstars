using System;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>
    /// The 3D body of a hero by id, for screens outside the home scene (e.g. a case jackpot). Robot
    /// and Gladiator predate data-driven heroes and live only on the scenes' rosters, so this asset
    /// names them; every other hero falls back to its catalog <see cref="AvatarOffer.Prefab"/> path.
    /// </summary>
    [CreateAssetMenu(menuName = "Push Stars/Hero Prefabs")]
    public sealed class HeroPrefabs : ScriptableObject
    {
        public const string ResourcePath = "HeroPrefabs";

        [Serializable]
        public struct Entry
        {
            public string Id;
            public GameObject Prefab;
        }

        public Entry[] Entries = new Entry[0];

        public static GameObject For(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return null;
            var registry = Resources.Load<HeroPrefabs>(ResourcePath);
            if (registry != null)
                foreach (var entry in registry.Entries)
                    if (entry.Id == heroId && entry.Prefab != null) return entry.Prefab;
            var offer = AvatarCatalog.Find(heroId);
            return offer != null && !string.IsNullOrEmpty(offer.Prefab) ? Resources.Load<GameObject>(offer.Prefab) : null;
        }
    }
}

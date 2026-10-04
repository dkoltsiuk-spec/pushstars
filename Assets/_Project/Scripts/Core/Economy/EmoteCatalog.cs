using System;
using UnityEngine;

namespace PushStars.Core
{
    public enum EmoteRarity { Free, Rare, Epic, Legendary }

    /// <summary>
    /// One avatar emote: a humanoid clip played on whichever hero the player has selected, and a
    /// short stinger under it. Free emotes are owned by everyone; the rest are bought for gems.
    /// </summary>
    [Serializable]
    public sealed class EmoteDef
    {
        [Tooltip("Saved in the wallet. Never rename a shipped id.")]
        public string Id;
        public string Name;
        public EmoteRarity Rarity;
        [Tooltip("Gems. Ignored for free emotes.")]
        [Min(0)] public int Price;
        public AnimationClip Clip;
        public AudioClip Sound;
        [Range(0f, 1f)] public float SoundVolume = .6f;
        [Tooltip("Where in the clip the emote starts, seconds. Long dances open on their best bar.")]
        [Min(0f)] public float StartSeconds;
        [Tooltip("Seconds of the clip to play. 0 = the rest of the clip.")]
        [Min(0f)] public float MaxSeconds;
        [Tooltip("Seconds into the clip (absolute) where the shop silhouette is taken.")]
        [Min(0f)] public float PoseSeconds;
        public bool Free => Rarity == EmoteRarity.Free;
        public EmoteDef Clone() => (EmoteDef)MemberwiseClone();
        public float Duration
        {
            get
            {
                if (Clip == null) return 0f;
                float rest = Mathf.Max(0f, Clip.length - StartSeconds);
                return MaxSeconds > 0f ? Mathf.Min(MaxSeconds, rest) : rest;
            }
        }
    }

    /// <summary>Authored by Tools ▸ Push Stars ▸ Emotes ▸ Build Emotes into Resources/EmoteCatalog.</summary>
    [CreateAssetMenu(menuName = "Push Stars/Rewards/Emote Catalog")]
    public sealed class EmoteCatalog : ScriptableObject
    {
        public EmoteDef[] Emotes = new EmoteDef[0];

        private static EmoteDef[] _all;
        public static EmoteDef[] All => _all ?? (_all = EditorPreview(Resources.Load<EmoteCatalog>("EmoteCatalog")?.Emotes ?? new EmoteDef[0]));

        public const string PreviewPref = "PushStars.Emotes.PreviewAlternateClips";

        /// <summary>Editor-only: with Tools ▸ Push Stars ▸ Emotes ▸ Preview Test Clips on, each emote
        /// whose clip has a "_GVHMR" sibling plays that one instead, in Play Mode in the Editor. Test
        /// captures (non-commercial models) are compared in the game this way; the code does not exist
        /// in a player build, so a build always ships the catalog's own clips.</summary>
        private static EmoteDef[] EditorPreview(EmoteDef[] emotes)
        {
#if UNITY_EDITOR
            if (!UnityEditor.EditorPrefs.GetBool(PreviewPref, false)) return emotes;
            var copy = new EmoteDef[emotes.Length];
            for (int i = 0; i < emotes.Length; i++)
            {
                copy[i] = emotes[i];
                var path = emotes[i].Clip != null ? UnityEditor.AssetDatabase.GetAssetPath(emotes[i].Clip) : null;
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".anim")) continue;
                var alternate = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>(path.Replace(".anim", "_GVHMR.anim"));
                if (alternate == null) continue;
                copy[i] = emotes[i].Clone();
                copy[i].Clip = alternate;
                copy[i].StartSeconds = 0f;
                copy[i].MaxSeconds = 0f;
            }
            return copy;
#else
            return emotes;
#endif
        }
        public static EmoteDef Find(string id) => Array.Find(All, e => e.Id == id);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _all = null;

        public static bool Owns(EmoteDef emote) => emote != null && (emote.Free || CaseRewards.OwnsEmote(emote.Id));

        /// <summary>Emotes the player can use right now, in catalog order.</summary>
        public static EmoteDef[] Owned() => Array.FindAll(All, e => e.Clip != null && Owns(e));

        public static bool TryBuy(EmoteDef emote) => emote != null && !emote.Free && CaseRewards.TryBuyEmote(emote.Id, emote.Price);
    }
}

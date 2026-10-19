using System;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>
    /// Resources/LiveDuelConfig.json — which Photon application and region the live duel uses.
    /// The AppId is not a secret (every client carries it). <see cref="region"/> is fixed on
    /// purpose: a room is found by name only inside one region, and "best region" can put two
    /// friends on different continents' servers. <see cref="customAuth"/> turns on Photon's
    /// custom authentication against the photonAuth function — set it only after the Photon
    /// Dashboard points at that URL, or every connection is refused.
    /// </summary>
    [Serializable]
    public sealed class LiveDuelConfig
    {
        public string appId = "", region = "eu", appVersion = "duel-1";
        public bool customAuth;

        public bool IsValid => Guid.TryParse(appId, out _) && !string.IsNullOrWhiteSpace(region);

        public static LiveDuelConfig Load()
        {
            var asset = Resources.Load<TextAsset>("LiveDuelConfig");
            if (asset == null) return null;
            try { return JsonUtility.FromJson<LiveDuelConfig>(asset.text); }
            catch (ArgumentException) { return null; }
        }
    }
}

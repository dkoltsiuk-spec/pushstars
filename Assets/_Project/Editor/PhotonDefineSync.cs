using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>
    /// Turns PUSHSTARS_PHOTON on when the Photon SDK (PUN 2 from the Asset Store, which carries
    /// Photon Realtime) is in the project, and off when it is not — the live-duel transport
    /// (PushStars.Photon) compiles only with it, so importing the SDK is the whole switch and a
    /// checkout without it still builds.
    /// </summary>
    [InitializeOnLoad]
    public static class PhotonDefineSync
    {
        public const string Define = "PUSHSTARS_PHOTON";
        private const string RealtimeFolder = "Assets/Photon/PhotonRealtime";
        private static readonly NamedBuildTarget[] Targets = { NamedBuildTarget.iOS, NamedBuildTarget.Android, NamedBuildTarget.Standalone };

        static PhotonDefineSync() => EditorApplication.delayCall += Sync;

        [MenuItem("Tools/Push Stars/Live Duel/Sync Photon Define")]
        public static void Sync()
        {
            bool present = Directory.Exists(RealtimeFolder);
            foreach (var target in Targets)
            {
                var defines = PlayerSettings.GetScriptingDefineSymbols(target).Split(';')
                    .Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
                if (defines.Contains(Define) == present) continue;
                if (present) defines.Add(Define); else defines.Remove(Define);
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
                Debug.Log($"[LiveDuel] {(present ? "Enabled" : "Disabled")} {Define} for {target.TargetName}.");
            }
        }
    }
}

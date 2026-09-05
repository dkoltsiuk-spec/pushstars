using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>Creates the remote Main/Fight catalog while keeping embedded fallback scenes.</summary>
    public static class OtaSetup
    {
        const string GroupName = "Push Stars OTA Scenes";
        const string RemoteBuildVariable = "PushStarsOtaBuildPath";
        const string RemoteLoadVariable = "PushStarsOtaLoadPath";
        const string RemoteBuildPath = "ota_public/ota/[BuildTarget]";
        const string RemoteLoadPath = "https://push-stars-d620e.web.app/ota/[BuildTarget]";
        const string GeneratedDir = "Assets/_Project/OTA/Generated";
        const string StateDir = "Assets/_Project/OTA/State";
        const string PlayerVersion = "push-stars-ota-v1";
        const int RequestTimeoutSeconds = 10;

        static readonly (string source, string generated, string address)[] Scenes =
        {
            ("Assets/_Project/Scenes/Main.unity", GeneratedDir + "/MainRemote.unity", "ota/Main"),
            ("Assets/_Project/Scenes/Fight.unity", GeneratedDir + "/FightRemote.unity", "ota/Fight"),
        };

        [MenuItem("Tools/Push Stars/OTA/Configure", priority = 40)]
        public static void Configure()
        {
            SyncAuthoredScenes();
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            SetProfileValue(settings, RemoteBuildVariable, RemoteBuildPath);
            SetProfileValue(settings, RemoteLoadVariable, RemoteLoadPath);
            settings.OverridePlayerVersion = PlayerVersion;
            settings.CatalogRequestsTimeout = RequestTimeoutSeconds;

            var group = settings.FindGroup(GroupName) ?? settings.CreateGroup(
                GroupName, false, false, true, null,
                typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));

            var bundle = group.GetSchema<BundledAssetGroupSchema>();
            bundle.BuildPath.SetVariableByName(settings, RemoteBuildVariable);
            bundle.LoadPath.SetVariableByName(settings, RemoteLoadVariable);
            bundle.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
            bundle.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
            bundle.UseAssetBundleCache = true;
            bundle.Timeout = RequestTimeoutSeconds;

            var update = group.GetSchema<ContentUpdateGroupSchema>();
            update.StaticContent = false;

            // Generated scenes are intentionally not stored in Git, so Unity can assign fresh
            // GUIDs on a clean checkout. Remove the two previous generated entries before adding
            // the current copies; this prevents stale GUIDs from remaining in cloud builds.
            foreach (var previous in group.entries.ToArray())
                settings.RemoveAssetEntry(previous.guid, false);

            foreach (var scene in Scenes)
            {
                string guid = AssetDatabase.AssetPathToGUID(scene.generated);
                var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                entry.address = scene.address;
            }

            settings.BuildRemoteCatalog = true;
            settings.RemoteCatalogBuildPath.SetVariableByName(settings, RemoteBuildVariable);
            settings.RemoteCatalogLoadPath.SetVariableByName(settings, RemoteLoadVariable);
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification,
                              null, true, true);
            AssetDatabase.SaveAssets();
            Debug.Log($"[OTA] Configured {GroupName} → {RemoteLoadPath}");
        }

        /// <summary>Called before the transition IPA. Produces its catalog and deployable bundles.</summary>
        public static void BuildFullContent()
        {
            EnsureIOSBuildTarget();
            Configure();
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            if (!string.IsNullOrEmpty(result.Error))
                throw new Exception("Addressables full content build failed: " + result.Error);
            SaveContentStateSnapshot();
            Debug.Log("[OTA] Full content ready in ota_public. Deploy Hosting with firebase deploy --only hosting.");
        }

        [MenuItem("Tools/Push Stars/OTA/Build && Publish Full Content", priority = 42)]
        public static void BuildAndPublishFullContent()
        {
            BuildFullContent();
            PublishHosting();
        }

        [MenuItem("Tools/Push Stars/OTA/Build Content Update", priority = 41)]
        public static void BuildContentUpdate()
        {
            EnsureIOSBuildTarget();
            Configure();
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            string state = FindNewestContentState();
            if (state == null)
                throw new FileNotFoundException("No addressables_content_state.bin. Build the transition player first.");

            AddressablesPlayerBuildResult result = ContentUpdateScript.BuildContentUpdate(settings, state);
            if (!string.IsNullOrEmpty(result.Error))
                throw new Exception("Addressables content update failed: " + result.Error);
            Debug.Log("[OTA] Content update ready. Review it, then deploy: firebase deploy --only hosting");
        }

        [MenuItem("Tools/Push Stars/OTA/Build && Publish Content Update", priority = 43)]
        public static void BuildAndPublishContentUpdate()
        {
            BuildContentUpdate();
            PublishHosting();
        }

        static void SyncAuthoredScenes()
        {
            Directory.CreateDirectory(GeneratedDir);
            foreach (var scene in Scenes)
            {
                File.Copy(scene.source, scene.generated, true);
                AssetDatabase.ImportAsset(scene.generated, ImportAssetOptions.ForceSynchronousImport);
            }
        }

        static void EnsureIOSBuildTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS) return;
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS))
                throw new Exception("Could not switch Unity to the iOS build target.");
        }

        static void SetProfileValue(AddressableAssetSettings settings, string name, string value)
        {
            settings.profileSettings.CreateValue(name, value); // returns the existing id when present
            settings.profileSettings.SetValue(settings.activeProfileId, name, value);
        }

        static string FindNewestContentState()
        {
            if (Directory.Exists(StateDir))
            {
                string snapshot = Directory.GetFiles(StateDir, "addressables_content_state.bin",
                                                     SearchOption.AllDirectories)
                                           .OrderByDescending(File.GetLastWriteTimeUtc)
                                           .FirstOrDefault();
                if (snapshot != null) return snapshot;
            }

            const string root = "Assets/AddressableAssetsData";
            if (!Directory.Exists(root)) return null;
            return Directory.GetFiles(root, "addressables_content_state.bin", SearchOption.AllDirectories)
                            .OrderByDescending(File.GetLastWriteTimeUtc)
                            .FirstOrDefault();
        }

        static void SaveContentStateSnapshot()
        {
            const string generatedRoot = "Assets/AddressableAssetsData";
            if (!Directory.Exists(generatedRoot)) return;
            string source = Directory.GetFiles(generatedRoot, "addressables_content_state.bin",
                                               SearchOption.AllDirectories)
                                     .OrderByDescending(File.GetLastWriteTimeUtc)
                                     .FirstOrDefault();
            if (source == null) return;

            string platformDir = Path.Combine(StateDir, EditorUserBuildSettings.activeBuildTarget.ToString());
            Directory.CreateDirectory(platformDir);
            string destination = Path.Combine(platformDir, "addressables_content_state.bin");
            File.Copy(source, destination, true);
            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
        }

        static void PublishHosting()
        {
            string command = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm", "firebase.cmd");
            if (!File.Exists(command)) command = "firebase";

            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = command,
                Arguments = "deploy --only hosting --project push-stars-d620e",
                WorkingDirectory = Directory.GetParent(Application.dataPath).FullName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = System.Diagnostics.Process.Start(info);
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new Exception("Firebase Hosting publish failed:\n" + output + "\n" + error);
            Debug.Log("[OTA] Published to Firebase Hosting.\n" + output);
        }
    }
}

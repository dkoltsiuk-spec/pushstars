#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace PushStars.Editor
{
    /// <summary>
    /// Push notifications for Firebase Cloud Messaging ("notify me when an opponent is found"):
    /// the aps-environment entitlement and the remote-notification background mode. Firebase does
    /// not add them, and Mobile Notifications is left with remote notifications off so it backs off
    /// for pushes and Firebase handles them. Production APNs: every iOS build goes through
    /// TestFlight / the App Store. Shares the entitlements file <see cref="SignInBuildSetup"/> writes.
    /// The App ID needs the Push Notifications capability and a provisioning profile made after it.
    /// </summary>
    public static class PushBuildSetup
    {
        [PostProcessBuild(46)]
        public static void Configure(BuildTarget target, string output)
        {
            if (target != BuildTarget.iOS) return;
            string projectPath = PBXProject.GetPBXProjectPath(output);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string main = project.GetUnityMainTargetGuid();
            string entitlements = project.GetBuildPropertyForAnyConfig(main, "CODE_SIGN_ENTITLEMENTS");
            if (string.IsNullOrEmpty(entitlements)) entitlements = "PushStars.entitlements";
            var capabilities = new ProjectCapabilityManager(projectPath, entitlements, null, main);
            capabilities.AddPushNotifications(false);
            capabilities.AddBackgroundModes(BackgroundModesOptions.RemoteNotifications);
            capabilities.WriteToFile();
        }
    }
}
#endif

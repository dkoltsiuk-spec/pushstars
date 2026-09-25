#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace PushStars.Editor
{
    public static class SignInBuildSetup
    {
        [PostProcessBuild(45)]
        public static void Configure(BuildTarget target, string output)
        {
            if (target != BuildTarget.iOS) return;
            string projectPath = PBXProject.GetPBXProjectPath(output);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string main = project.GetUnityMainTargetGuid();
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "AuthenticationServices.framework", false);
            project.WriteToFile(projectPath);

            string entitlements = project.GetBuildPropertyForAnyConfig(main, "CODE_SIGN_ENTITLEMENTS");
            if (string.IsNullOrEmpty(entitlements)) entitlements = "PushStars.entitlements";
            var capabilities = new ProjectCapabilityManager(projectPath, entitlements, null, main);
            capabilities.AddSignInWithApple();
            capabilities.WriteToFile();

            var config = new PlistDocument();
            config.ReadFromFile("Assets/GoogleService-Info.plist");
            if (!config.root.values.TryGetValue("REVERSED_CLIENT_ID", out var reversed))
            {
                Debug.LogWarning("[Auth] Google OAuth client is missing. Enable Google in Firebase and download GoogleService-Info.plist again.");
                return;
            }
            string plistPath = Path.Combine(output, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            var types = plist.root.values.TryGetValue("CFBundleURLTypes", out var existing)
                ? existing.AsArray() : plist.root.CreateArray("CFBundleURLTypes");
            bool found = false;
            foreach (var type in types.values)
            {
                if (!type.AsDict().values.TryGetValue("CFBundleURLSchemes", out var schemes)) continue;
                foreach (var scheme in schemes.AsArray().values)
                    if (scheme.AsString() == reversed.AsString()) found = true;
            }
            if (!found) types.AddDict().CreateArray("CFBundleURLSchemes").AddString(reversed.AsString());
            plist.WriteToFile(plistPath);
        }
    }
}
#endif

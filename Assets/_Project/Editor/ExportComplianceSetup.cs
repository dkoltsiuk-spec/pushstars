#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace PushStars.Editor
{
    /// <summary>
    /// Declares in Info.plist that the app uses only exempt encryption (HTTPS to Firebase, Photon's
    /// standard transport encryption). Without the key App Store Connect holds every TestFlight
    /// build at "Missing Compliance" until someone answers the export question by hand.
    /// </summary>
    public static class ExportComplianceSetup
    {
        [PostProcessBuild(50)]
        public static void Configure(BuildTarget target, string output)
        {
            if (target != BuildTarget.iOS) return;
            string plistPath = Path.Combine(output, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            plist.WriteToFile(plistPath);
        }
    }
}
#endif

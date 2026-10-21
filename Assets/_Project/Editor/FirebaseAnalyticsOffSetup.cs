#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace PushStars.Editor
{
    /// <summary>
    /// Turns off Google Analytics for Firebase, which the Firebase/Core pod links in and starts by
    /// default. Our analytics is PostHog (opt-out in Settings); leaving Firebase's on would send
    /// device data and IP-derived location that the privacy policy and App Store labels don't declare.
    /// </summary>
    public static class FirebaseAnalyticsOffSetup
    {
        [PostProcessBuild(51)]
        public static void Configure(BuildTarget target, string output)
        {
            if (target != BuildTarget.iOS) return;
            string plistPath = Path.Combine(output, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetBoolean("FIREBASE_ANALYTICS_COLLECTION_DEACTIVATED", true);
            plist.WriteToFile(plistPath);
        }
    }
}
#endif

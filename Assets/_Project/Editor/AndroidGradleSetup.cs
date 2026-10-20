#if UNITY_ANDROID
using System.IO;
using System.Linq;
using UnityEditor.Android;

namespace PushStars.Editor
{
    /// <summary>
    /// The dependency resolver points Gradle at the local Firebase repository with a raw
    /// <c>file:///</c> URI built from the project path. This project lives in "Push Stars", and
    /// Gradle refuses a file URI with a space in it ("Cannot convert URI ... to a file"), so the
    /// space is escaped in the generated settings.gradle. The resolver rewrites its block of
    /// settingsTemplate.gradle on every resolve, which is why the fix lives here and not there.
    ///
    /// It also strips the advertising-ID permissions a Google/Firebase library merges in. The app
    /// shows no ads and never reads the ID, and the Play Console "Advertising ID" declaration says
    /// so — Play blocks a release whose manifest still carries AD_ID after a "no" there.
    /// </summary>
    public sealed class AndroidGradleSetup : IPostGenerateGradleAndroidProject
    {
        private const string PathExpression = @"$.replace(""\\"", ""/"")";
        private const string EscapeSpaces   = @".replace("" "", ""%20"")";

        public int callbackOrder => 100;

        private static readonly string[] AdIdPermissions =
        {
            "com.google.android.gms.permission.AD_ID",
            "android.permission.ACCESS_ADSERVICES_AD_ID",
            "android.permission.ACCESS_ADSERVICES_ATTRIBUTION",
        };

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            RemoveAdIdPermissions(unityLibraryPath);
            EscapeRepositoryPath(unityLibraryPath);
        }

        /// <summary>The launcher manifest is the app's own, so its remove markers win the merge.</summary>
        private static void RemoveAdIdPermissions(string unityLibraryPath)
        {
            string manifest = Path.Combine(unityLibraryPath, "..", "launcher", "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest)) throw new FileNotFoundException("[Build] Launcher manifest not found.", manifest);

            string text = File.ReadAllText(manifest);
            var missing = AdIdPermissions.Where(p => !text.Contains($"\"{p}\"")).ToList();
            if (missing.Count == 0) return;
            if (!text.Contains("xmlns:tools="))
                text = text.Replace("<manifest ", "<manifest xmlns:tools=\"http://schemas.android.com/tools\" ");
            int at = text.IndexOf('>', text.IndexOf("<manifest")) + 1;
            string lines = string.Concat(missing.Select(p => $"\n  <uses-permission android:name=\"{p}\" tools:node=\"remove\" />"));
            File.WriteAllText(manifest, text.Insert(at, lines));
        }

        private static void EscapeRepositoryPath(string unityLibraryPath)
        {
            string settings = Path.Combine(unityLibraryPath, "..", "settings.gradle");
            if (!File.Exists(settings)) return;

            string text = File.ReadAllText(settings);
            if (!text.Contains(PathExpression) || text.Contains(PathExpression + EscapeSpaces)) return;
            File.WriteAllText(settings, text.Replace(PathExpression, PathExpression + EscapeSpaces));
        }
    }
}
#endif

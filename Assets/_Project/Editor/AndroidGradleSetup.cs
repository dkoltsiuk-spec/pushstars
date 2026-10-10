#if UNITY_ANDROID
using System.IO;
using UnityEditor.Android;

namespace PushStars.Editor
{
    /// <summary>
    /// The dependency resolver points Gradle at the local Firebase repository with a raw
    /// <c>file:///</c> URI built from the project path. This project lives in "Push Stars", and
    /// Gradle refuses a file URI with a space in it ("Cannot convert URI ... to a file"), so the
    /// space is escaped in the generated settings.gradle. The resolver rewrites its block of
    /// settingsTemplate.gradle on every resolve, which is why the fix lives here and not there.
    /// </summary>
    public sealed class AndroidGradleSetup : IPostGenerateGradleAndroidProject
    {
        private const string PathExpression = @"$.replace(""\\"", ""/"")";
        private const string EscapeSpaces   = @".replace("" "", ""%20"")";

        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
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

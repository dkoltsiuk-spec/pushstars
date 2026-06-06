using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>
    /// Toggles the <c>PUSHSTARS_MEDIAPIPE</c> scripting define for the active build target. That define
    /// gates the <c>PushStars.CV.MediaPipe</c> assembly (the real Homuler Pose Landmarker adapter), so
    /// the project compiles without the plugin until you flip this on. Menu: Tools → Push Stars → MediaPipe.
    /// </summary>
    public static class MediaPipeDefineSetup
    {
        private const string Define = "PUSHSTARS_MEDIAPIPE";

        [MenuItem("Tools/Push Stars/MediaPipe/Enable (add PUSHSTARS_MEDIAPIPE)", priority = 300)]
        public static void Enable() => SetDefine(true);

        [MenuItem("Tools/Push Stars/MediaPipe/Disable (remove PUSHSTARS_MEDIAPIPE)", priority = 301)]
        public static void Disable() => SetDefine(false);

        private static void SetDefine(bool enable)
        {
            var target  = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            var current = PlayerSettings.GetScriptingDefineSymbols(target);
            var list    = new List<string>(current.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));

            bool changed = false;
            if (enable && !list.Contains(Define)) { list.Add(Define); changed = true; }
            if (!enable && list.Remove(Define))   { changed = true; }

            if (!changed)
            {
                Debug.Log($"[PushStars] {Define} already {(enable ? "present" : "absent")} for {target.TargetName}.");
                return;
            }

            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", list));
            Debug.Log($"[PushStars] {(enable ? "Enabled" : "Disabled")} {Define} for {target.TargetName}. Recompiling…");
        }
    }
}

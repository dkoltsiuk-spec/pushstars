using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PushStars.Editor
{
    public sealed class SadIdleSetup : AssetPostprocessor
    {
        public const string ClipPath = "Assets/Character/Animations/Sad Idle.fbx";

        private void OnPreprocessModel()
        {
            if (assetPath != ClipPath) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
        }

        private void OnPreprocessAnimation()
        {
            if (assetPath != ClipPath) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.name = "SadIdle";
                clip.loopTime = true;
                clip.loopPose = true;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }

        [MenuItem("Tools/Push Stars/Character/Install Sad Idle")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var clip = AssetDatabase.LoadAllAssetsAtPath(ClipPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (clip == null) return;
            bool changed = false;
            foreach (var path in new[] {
                "Assets/_Project/Art/Characters/Mixamo/AvatarOverlayTest.controller",
                "Assets/Character/Main_man/MainMan.controller",
                "Assets/Character/Main_woman/MainWoman.controller" })
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller == null) continue;
                var machine = controller.layers[0].stateMachine;
                var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "SadIdle");
                if (state != null && state.motion == clip) continue;
                if (state == null) state = machine.AddState("SadIdle");
                state.motion = clip;
                EditorUtility.SetDirty(controller);
                changed = true;
            }
            if (changed) { AssetDatabase.SaveAssets(); Debug.Log("[SadIdle] Installed looping humanoid clip in all three character controllers."); }
        }
    }
}

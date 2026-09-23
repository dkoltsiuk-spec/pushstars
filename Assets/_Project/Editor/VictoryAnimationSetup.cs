using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>Imports the shared Mixamo victory as a one-shot humanoid clip and keeps it in
    /// every controller that can present either side of a duel result.</summary>
    public sealed class VictoryAnimationSetup : AssetPostprocessor
    {
        public const string ClipPath = "Assets/Character/Animations/Victory.fbx";
        private static readonly string[] ControllerPaths =
        {
            "Assets/_Project/Art/Characters/Mixamo/AvatarOverlayTest.controller",
            "Assets/Character/Main_man/MainMan.controller",
            "Assets/Character/Main_woman/MainWoman.controller"
        };

        private void OnPreprocessModel()
        {
            if (assetPath != ClipPath) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        private void OnPreprocessAnimation()
        {
            if (assetPath != ClipPath) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.name = "Victory";
                clip.loopTime = false;
                clip.loopPose = false;
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }

        [InitializeOnLoadMethod]
        private static void InstallAfterReload() => EditorApplication.delayCall += Install;

        [MenuItem("Tools/Push Stars/Character/Install Victory Animation")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetImporter.GetAtPath(ClipPath) is not ModelImporter importer) return;

            if (importer.animationType != ModelImporterAnimationType.Human ||
                importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel ||
                importer.clipAnimations.Length == 0 ||
                importer.clipAnimations.Any(clip => clip.name != "Victory" || clip.loopTime))
            {
                importer.SaveAndReimport();
            }

            var victory = AssetDatabase.LoadAllAssetsAtPath(ClipPath).OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview"));
            if (victory == null) return;

            bool changed = false;
            foreach (string path in ControllerPaths)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller == null) continue;
                var machine = controller.layers[0].stateMachine;
                var state = machine.states.Select(child => child.state)
                    .FirstOrDefault(candidate => candidate.name == "Victory");
                if (state == null) state = machine.AddState("Victory");
                if (state.motion == victory) continue;
                state.motion = victory;
                EditorUtility.SetDirty(controller);
                changed = true;
            }

            if (!changed) return;
            AssetDatabase.SaveAssets();
            Debug.Log("[VictoryAnimation] Installed one-shot Victory in all result avatar controllers.");
        }
    }
}

using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>Between training sets the hero stands and gets their breath back (Mixamo "Breathing
    /// Idle"), head a little lowered — tired after the set, gathering for the next one. The head is
    /// lowered in a copy of the clip, so the imported FBX stays as delivered.</summary>
    public sealed class TrainingRestIdleSetup : AssetPostprocessor
    {
        public const string State = "RestBreathing";
        private const string SourcePath = "Assets/Character/Animations/Breathing Idle.fbx";
        private const string ClipPath = "Assets/Character/Animations/RestBreathing.anim";
        /// <summary>Muscle offset (−1…1 range) added to the neck and head nod: a slight bow.</summary>
        private const float HeadDown = -.08f;
        private static readonly string[] Controllers = {
            "Assets/_Project/Art/Characters/Mixamo/AvatarOverlayTest.controller",
            "Assets/Character/Main_man/MainMan.controller",
            "Assets/Character/Main_woman/MainWoman.controller" };

        private void OnPreprocessModel()
        {
            if (assetPath != SourcePath) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
        }

        private void OnPreprocessAnimation()
        {
            if (assetPath != SourcePath) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.name = "BreathingIdle";
                clip.loopTime = true;
                clip.loopPose = true;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }

        [MenuItem("Tools/Push Stars/Training/Install Rest Breathing")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            AssetDatabase.ImportAsset(SourcePath, ImportAssetOptions.ForceUpdate);
            var source = AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>()
                .First(c => !c.name.StartsWith("__preview"));

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, ClipPath); }
            clip.ClearCurves();
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (binding.propertyName == "Neck Nod Down-Up" || binding.propertyName == "Head Nod Down-Up")
                {
                    var keys = curve.keys;
                    for (int i = 0; i < keys.Length; i++) keys[i].value += HeadDown;
                    curve.keys = keys;
                }
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(source));
            EditorUtility.SetDirty(clip);

            foreach (var path in Controllers)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller == null) continue;
                var machine = controller.layers[0].stateMachine;
                // The earlier boxing-style warm-up is retired.
                foreach (var old in machine.states.Select(s => s.state).Where(s => s.name == "RestWarmUp").ToArray())
                    machine.RemoveState(old);
                var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == State)
                            ?? machine.AddState(State);
                state.motion = clip;
                EditorUtility.SetDirty(controller);
            }
            AssetDatabase.SaveAssets();
        }
    }
}

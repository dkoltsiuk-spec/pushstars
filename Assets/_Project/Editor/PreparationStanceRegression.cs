using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PushStars.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PushStars.Editor
{
    public static class PreparationStanceRegression
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static void Run()
        {
            int checkedBodies = 0;
            foreach (string sceneName in new[] { "FightPreparation", "FightResults" })
            foreach (bool female in new[] { false, true })
            {
                var scene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/" + sceneName + ".unity");
                try
                {
                    foreach (var stage in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FightAvatar>(true)))
                    {
                        var so = new SerializedObject(stage);
                        var prefab = so.FindProperty(female ? "_femalePrefab" : "_malePrefab").objectReferenceValue;
                        so.FindProperty("_femalePrefab").objectReferenceValue = prefab;
                        so.FindProperty("_malePrefab").objectReferenceValue = prefab;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        Invoke(stage, "Build");
                        stage.SetPreparationPresentation(true);
                        var animator = stage.Character.GetComponentInChildren<Animator>();
                        var reference = UnityEngine.Object.Instantiate((GameObject)prefab);
                        var referenceAnimator = reference.GetComponentInChildren<Animator>();
                        bool results = sceneName == "FightResults";
                        Component panel = results
                            ? scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FightResultScreen>(true)).First()
                            : scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<DuelReadyPanel>(true)).First();
                        // Exercise the live-render path, not the editor's static fallback portrait.
                        panel.GetType().GetField("_sceneAuthored", Private).SetValue(panel, false);
                        if (!results) typeof(DuelReadyPanel).GetField("_preparationAvatars", Private).SetValue(panel, new[] { stage });
                        var sourceObject = new GameObject("Regression source", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
                        var targetObject = new GameObject("Regression portrait", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
                        var source = sourceObject.GetComponent<UnityEngine.UI.RawImage>();
                        var target = targetObject.GetComponent<UnityEngine.UI.RawImage>();
                        target.rectTransform.sizeDelta = new Vector2(182, 368);
                        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(reference, scene);
                        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(sourceObject, scene);
                        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(targetObject, scene);
                        var render = new RenderTexture(512, 1024, 24);
                        stage.StageCamera.targetTexture = render;
                        source.texture = render;
                        var copy = panel.GetType().GetMethod(results ? "CropPortrait" : "CopyPortrait", Private);
                        object[] cropArguments = { target, source, results ? (object)stage : null };
                        try
                        {
                            Invoke(stage, "LateUpdate");
                            copy.Invoke(panel, cropArguments);
                            var cameraPosition = stage.StageCamera.transform.position;
                            var cameraRotation = stage.StageCamera.transform.rotation;
                            var crop = target.uvRect;
                            var spine = animator.GetBoneTransform(HumanBodyBones.Spine);
                            Quaternion startSpine = spine.localRotation;
                            float spineMotion = 0;
                            for (int frame = 0; frame < 360; frame++)
                            {
                                animator.Update(1f / 60);
                                Invoke(stage, "LateUpdate");
                                copy.Invoke(panel, cropArguments);
                                Require(stage.StageCamera.transform.position == cameraPosition
                                    && Quaternion.Angle(stage.StageCamera.transform.rotation, cameraRotation) < .01f,
                                    "Preparation camera follows the body.");
                                Require(target.uvRect == crop, "Preparation crop follows the body.");
                                referenceAnimator.Play("Idle", 0, animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
                                referenceAnimator.Update(0);
                                foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg,
                                    HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                                    Require(Quaternion.Angle(animator.GetBoneTransform(bone).localRotation,
                                        referenceAnimator.GetBoneTransform(bone).localRotation) < .1f,
                                        "Preparation changed the main-screen leg animation: " + bone);
                                spineMotion = Mathf.Max(spineMotion, Quaternion.Angle(startSpine, spine.localRotation));
                            }
                            Require(spineMotion > .05f, "Idle was frozen.");
                            if (results)
                            {
                                stage.SetResultPresentation(false, true);
                                animator.Update(0f);
                                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Victory"),
                                    "Winner did not start the Victory animation.");
                                var victory = animator.runtimeAnimatorController.animationClips
                                    .FirstOrDefault(clip => clip.name == "Victory");
                                Require(victory != null && !victory.isLooping,
                                    "Victory must be an installed one-shot clip.");
                                typeof(FightAvatar).GetField("_resultReturnAt", Private)
                                    .SetValue(stage, Time.unscaledTime - .01f);
                                Invoke(stage, "LateUpdate");
                                animator.Update(.2f);
                                var current = animator.GetCurrentAnimatorStateInfo(0);
                                var next = animator.IsInTransition(0)
                                    ? animator.GetNextAnimatorStateInfo(0) : current;
                                Require(current.IsName("StandIdle") || next.IsName("StandIdle"),
                                    "Winner did not return to StandIdle after Victory.");
                            }
                            target.rectTransform.sizeDelta = new Vector2(240, 368);
                            copy.Invoke(panel, cropArguments);
                            Require(target.uvRect != crop, "Portrait did not adapt to resized layout.");
                            stage.SetPreparationPresentation(false);
                            Require(!stage.IsPreparationFramed, "Preparation framing survived READY.");
                        }
                        finally
                        {
                            stage.StageCamera.targetTexture = null;
                            UnityEngine.Object.DestroyImmediate(render);
                            UnityEngine.Object.DestroyImmediate(reference);
                            UnityEngine.Object.DestroyImmediate(sourceObject);
                            UnityEngine.Object.DestroyImmediate(targetObject);
                        }
                        checkedBodies++;
                    }
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            Require(checkedBodies == 8, "Expected both fighters and both character rigs.");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/preparation-stance-regression.txt",
                "PASS: preparation and results, both fighters, both rigs; 360 idle frames each with fixed camera/crop, legs matching main-screen idle, one-shot winner Victory returning to StandIdle, responsive layout resize, and released preparation framing on READY.\n");
        }
        private static void Invoke(object owner, string name) => owner.GetType().GetMethod(name, Private).Invoke(owner, null);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}

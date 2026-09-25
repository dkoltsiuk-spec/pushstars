using System;
using System.Linq;
using PushStars.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PushStars.Editor
{
    public static class CaseOpeningMotionSetup
    {
        [MenuItem("Tools/Push Stars/Rewards/Configure Case Motion")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            const string path = "Assets/_Project/Scenes/CaseOpening.unity";
            var scene = SceneManager.GetSceneByPath(path);
            bool alreadyOpen = scene.IsValid() && scene.isLoaded;
            if (alreadyOpen && scene.isDirty) throw new InvalidOperationException("Save the case scene edits before configuring its rig.");
            if (!alreadyOpen) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RewardScreen>(true)).Single();
                Configure(screen);
                EditorSceneManager.SaveScene(scene, path);
            }
            finally { if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true); }
        }

        public static void Configure(RewardScreen screen)
        {
            var motion = screen.GetComponent<CaseOpeningMotion>();
            if (motion == null) motion = screen.gameObject.AddComponent<CaseOpeningMotion>();
            motion.Screen = screen;
            var content = screen.CaseUi.Content;
            motion.Behind = Layer(content, "CaseMotionBehind", false);
            motion.InFront = Layer(content, "CaseMotionFront", true);
            // Normalize first so configuring an existing rig cannot swap the rear layer
            // in front of the case through sibling-index shifts.
            motion.Behind.transform.SetAsLastSibling();
            motion.InFront.transform.SetAsLastSibling();
            motion.Behind.transform.SetSiblingIndex(content.GetSiblingIndex());
            motion.InFront.transform.SetSiblingIndex(content.GetSiblingIndex() + 1);
            const string materialPath = "Assets/_Project/Resources/Rewards/CaseMotion.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                var shader = Shader.Find("PushStars/UI Case Motion");
                if (shader == null) throw new InvalidOperationException("Case motion shader missing.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            screen.CaseUi.Artwork.material = material;
            var data = new SerializedObject(screen);
            data.FindProperty("_caseMotion").objectReferenceValue = motion;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(screen); EditorUtility.SetDirty(motion);
        }

        private static CaseMotionGraphic Layer(RectTransform content, string name, bool front)
        {
            var rt = content.parent.Find(name) as RectTransform;
            if (rt == null)
            {
                rt = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(CaseMotionGraphic)).GetComponent<RectTransform>();
                rt.SetParent(content.parent, false);
            }
            rt.anchorMin = content.anchorMin; rt.anchorMax = content.anchorMax;
            rt.pivot = content.pivot; rt.sizeDelta = content.sizeDelta; rt.anchoredPosition = content.anchoredPosition;
            rt.localScale = content.localScale;
            var graphic = rt.GetComponent<CaseMotionGraphic>();
            graphic.Foreground = front; graphic.raycastTarget = false;
            return graphic;
        }

        public static void ConfigureAndValidate()
        {
            Apply();
            CaseOpeningMotionValidation.Run();
            CaseRewardsRegression.Run();
        }
    }
}

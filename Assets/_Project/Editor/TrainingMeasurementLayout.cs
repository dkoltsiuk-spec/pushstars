using System;
using System.Linq;
using PushStars.Fight;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    /// <summary>Training's active set uses the authored measurement HUD itself.</summary>
    public static class TrainingMeasurementLayout
    {
        [MenuItem("Tools/Push Stars/Training/Use Measurement Layout")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Apply the measurement layout outside Play Mode.");
            var scene = SceneManager.GetSceneByPath(TrainingScreenSetup.ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(TrainingScreenSetup.ScenePath, OpenSceneMode.Additive);
            try { Apply(scene); EditorSceneManager.SaveScene(scene); }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        public static void Validate() { Run(); TrainingScreenValidation.Run(); }
        public static void Apply(Scene targetScene)
        {
            var sourceScene = SceneManager.GetSceneByPath("Assets/_Project/Scenes/Fight.unity");
            bool opened = !sourceScene.IsValid() || !sourceScene.isLoaded;
            if (opened) sourceScene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Fight.unity", OpenSceneMode.Additive);
            try
            {
                var sourceHud = Find<FightHud>(sourceScene); var targetHud = Find<FightHud>(targetScene);
                var sourceRects = sourceHud.GetComponentsInChildren<RectTransform>(true).ToDictionary(r => Path(r, sourceHud.transform));
                foreach (var rt in targetHud.GetComponentsInChildren<RectTransform>(true))
                {
                    if (!sourceRects.TryGetValue(Path(rt, targetHud.transform), out var origin)) continue;
                    rt.anchorMin = origin.anchorMin; rt.anchorMax = origin.anchorMax; rt.pivot = origin.pivot;
                    rt.sizeDelta = origin.sizeDelta; rt.anchoredPosition3D = origin.anchoredPosition3D;
                    rt.localScale = origin.localScale; rt.localRotation = origin.localRotation;
                }
                var fromHud = new SerializedObject(sourceHud); var toHud = new SerializedObject(targetHud);
                foreach (var key in new[] { "_soloBannerY", "_soloHalfAnchorY" }) toHud.CopyFromSerializedProperty(fromHud.FindProperty(key));
                toHud.ApplyModifiedPropertiesWithoutUndo();
                var playerHalf = toHud.FindProperty("_playerHalf").objectReferenceValue as RectTransform;
                var portrait = playerHalf.GetComponentInChildren<RawImage>(true).rectTransform;
                portrait.localScale = Vector3.one * 2.145f;
                portrait.anchoredPosition = new Vector2(0f, 165f);
                var aspect = portrait.GetComponent<AspectRatioFitter>();
                if (aspect == null) aspect = portrait.gameObject.AddComponent<AspectRatioFitter>();
                aspect.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
                var preview = new SerializedObject(portrait.GetComponent<ScenePortraitPreview>());
                preview.FindProperty("_aspectFitter").objectReferenceValue = aspect;
                preview.ApplyModifiedPropertiesWithoutUndo();
                var portraitTexture = portrait.GetComponent<RawImage>().texture;
                if (portraitTexture != null) aspect.aspectRatio = (float)portraitTexture.width / portraitTexture.height;
                var reps = toHud.FindProperty("_soloReps").objectReferenceValue as TextMeshProUGUI;
                reps.rectTransform.anchoredPosition = new Vector2(0f, -88f);
                var fromScale = sourceHud.GetComponent<CanvasScaler>(); var toScale = targetHud.GetComponent<CanvasScaler>();
                toScale.uiScaleMode = fromScale.uiScaleMode; toScale.referenceResolution = fromScale.referenceResolution;
                toScale.screenMatchMode = fromScale.screenMatchMode; toScale.matchWidthOrHeight = fromScale.matchWidthOrHeight;
                // Undo the training-specific square render target and high camera angle.
                var sourceAvatars = sourceScene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FightAvatar>(true)).ToDictionary(a => a.name);
                foreach (var avatar in targetScene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FightAvatar>(true)))
                {
                    if (!sourceAvatars.TryGetValue(avatar.name, out var origin)) continue;
                    CopyProperties(origin, avatar, "_viewDirection", "_faceBias", "_padding", "_minDistance", "_maxDistance");
                    var fromStage = origin.StageCamera.GetComponentInParent<CharacterStage>(); var toStage = avatar.StageCamera.GetComponentInParent<CharacterStage>();
                    if (fromStage != null && toStage != null) CopyProperties(fromStage, toStage, "_width", "_height");
                }
                var controller = Find<FightController>(targetScene); var control = new SerializedObject(controller);
                var screen = targetHud.GetComponent<TrainingScreen>(); var so = new SerializedObject(screen);
                var oldExercise = so.FindProperty("_exercise").objectReferenceValue as GameObject;
                var solo = toHud.FindProperty("_soloPanel").objectReferenceValue as GameObject;
                if (oldExercise != solo) oldExercise.SetActive(false);
                var presentation = targetHud.transform.Find("TrainingScreen").gameObject;
                Set(so, "_presentationRoot", presentation); Set(so, "_exercise", solo);
                foreach (var pair in new[] { ("_reps", "_soloReps"), ("_technique", "_soloForm"), ("_set", "_soloTempo"), ("_timer", "_soloTimer"), ("_hint", "_bannerText"), ("_exerciseMode", "_soloCaption") })
                    Set(so, pair.Item1, toHud.FindProperty(pair.Item2).objectReferenceValue);
                foreach (var pair in new[] { ("_next", "_soloFinishButton"), ("_exercisePause", "_soloPauseButton"), ("_exerciseExit", "_soloExitButton"), ("_exerciseResume", "_soloResumeButton") })
                    Set(so, pair.Item1, control.FindProperty(pair.Item2).objectReferenceValue);
                var finish = control.FindProperty("_soloFinishLabel").objectReferenceValue as TextMeshProUGUI;
                finish.text = "NEXT";
                var tempo = toHud.FindProperty("_soloTempo").objectReferenceValue as TextMeshProUGUI;
                foreach (var label in tempo.transform.parent.GetComponentsInChildren<TextMeshProUGUI>(true))
                    if (label != tempo && (label.text == "TEMPO" || label.text == "SET:")) label.text = "SET:";
                var groups = targetHud.transform.Cast<Transform>().Where(t => t.gameObject != presentation)
                    .Select(t => t.GetComponent<CanvasGroup>()).Where(g => g != null).ToArray();
                var array = so.FindProperty("_measurementGroups"); array.arraySize = groups.Length;
                for (int i = 0; i < groups.Length; i++) { array.GetArrayElementAtIndex(i).objectReferenceValue = groups[i]; groups[i].alpha = 1; groups[i].blocksRaycasts = groups[i].interactable = true; }
                so.FindProperty("_useMeasurementLayout").boolValue = true; so.ApplyModifiedPropertiesWithoutUndo();
                presentation.SetActive(false); solo.SetActive(true);
                EditorSceneManager.MarkSceneDirty(targetScene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(sourceScene, true); }
        }
        private static T Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).Single();
        private static string Path(Transform node, Transform root)
        {
            if (node == root) return "";
            int ordinal = 0;
            for (int i = 0; i < node.GetSiblingIndex(); i++) if (node.parent.GetChild(i).name == node.name) ordinal++;
            return Path(node.parent, root) + "/" + node.name + "[" + ordinal + "]";
        }
        private static void Set(SerializedObject so, string key, UnityEngine.Object value) => so.FindProperty(key).objectReferenceValue = value;
        private static void CopyProperties(UnityEngine.Object source, UnityEngine.Object target, params string[] keys)
        { var from = new SerializedObject(source); var to = new SerializedObject(target); foreach (var key in keys) to.CopyFromSerializedProperty(from.FindProperty(key)); to.ApplyModifiedPropertiesWithoutUndo(); }
    }
}

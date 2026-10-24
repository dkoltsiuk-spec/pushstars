using System;
using System.Linq;
using PushStars.Fight;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    /// <summary>Training's rest view: the hero standing and getting their breath back, a big centred countdown, a rail of
    /// set nodes with a rounded (sliced, never stretched) fill, and header buttons whose sprites
    /// already carry their X / pause glyphs. Idempotent; reapply after a rebuild.</summary>
    public static class TrainingRestLayout
    {
        private const string Sprites = "Assets/_Project/UI/Sprites/";
        private const string Art = Sprites + "TrainingScreen/";

        [MenuItem("Tools/Push Stars/Training/Apply Rest Layout")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Apply the rest layout outside Play Mode.");
            var scene = SceneManager.GetSceneByPath(TrainingScreenSetup.ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(TrainingScreenSetup.ScenePath, OpenSceneMode.Additive);
            try { Apply(scene, true); EditorSceneManager.SaveScene(scene); }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        public static void Apply(Scene scene, bool rebakePortraits)
        {
            var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TrainingScreen>(true)).Single();
            var so = new SerializedObject(screen);
            var rest = ((GameObject)so.FindProperty("_rest").objectReferenceValue).transform;
            var header = ((GameObject)so.FindProperty("_header").objectReferenceValue).transform;

            // The close and pause sprites are drawn with their glyphs; a label on top doubled them.
            foreach (var key in new[] { "_exit", "_pause" })
            {
                var button = (Button)so.FindProperty(key).objectReferenceValue;
                foreach (var label in button.GetComponentsInChildren<TextMeshProUGUI>(true)) label.gameObject.SetActive(false);
            }

            var hud = screen.GetComponent<FightHud>();
            var playerHalf = new SerializedObject(hud).FindProperty("_playerHalf").objectReferenceValue as RectTransform;
            var avatar = playerHalf.GetComponentInChildren<FightAvatar>(true);
            if (avatar == null) avatar = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FightAvatar>(true)).First();
            var male = FightPresentationSceneBuilder.EnsureTrainingRestPortrait(avatar, false, rebakePortraits);
            var female = FightPresentationSceneBuilder.EnsureTrainingRestPortrait(avatar, true, rebakePortraits);
            so.FindProperty("_femaleRest").objectReferenceValue = female;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Standing hero, centred above the timer; TrainingScreen swaps in the live stage and
            // fits the width to it, so the anchor is top-centre and only the height is authored.
            var portrait = rest.Find("RestingCharacter").GetComponent<RawImage>();
            portrait.texture = male;
            var frame = portrait.rectTransform;
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f, 1);
            frame.anchoredPosition = new Vector2(0, -128);
            frame.sizeDelta = new Vector2(370f * male.width / male.height, 370);
            frame.localScale = Vector3.one;
            Place((RectTransform)rest.Find("RestWatch"), 60, 190, 270, 278);
            // Getting the breath back is not a nap: the sleeping Zs are retired.
            for (int i = 0; i < 3; i++) rest.Find("Sleep" + i).gameObject.SetActive(false);

            var timer = rest.Find("RestTimer").GetComponent<TextMeshProUGUI>();
            Place(timer.rectTransform, 0, 500, 390, 96);
            timer.fontSize = 78;
            timer.alignment = TextAlignmentOptions.Center;
            var counter = AssetDatabase.LoadAssetAtPath<Material>(Art + "TrainingCounter.mat");
            if (counter != null) timer.fontSharedMaterial = counter;

            BuildRail(rest, so);
            EditorUtility.SetDirty(screen);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>Thin rounded track; each set is a node (dark ring, coloured disc, icon) sitting on it.</summary>
        private static void BuildRail(Transform rest, SerializedObject so)
        {
            var pill = Sprite(Sprites + "pill_16.png");
            var circle = Sprite(Sprites + "circle_128.png");
            var track = rest.Find("ProgressTrack").GetComponent<Image>();
            track.sprite = pill; track.type = Image.Type.Sliced; track.color = new Color32(16, 19, 38, 255);
            Place(track.rectTransform, 52, 630, 286, 14);
            var fill = (Image)so.FindProperty("_progressFill").objectReferenceValue;
            fill.sprite = pill; fill.type = Image.Type.Sliced; fill.color = new Color32(255, 207, 0, 255);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(.5f, 1);
            fill.rectTransform.offsetMin = new Vector2(0, 2); fill.rectTransform.offsetMax = new Vector2(0, -2);
            fill.rectTransform.pivot = new Vector2(0, .5f);

            var steps = so.FindProperty("_steps");
            for (int i = 0; i < steps.arraySize; i++)
            {
                var icon = (Image)steps.GetArrayElementAtIndex(i).objectReferenceValue;
                var node = (RectTransform)icon.transform;
                while (node.parent != track.transform) node = (RectTransform)node.parent;
                var ring = node.GetComponent<Image>();
                ring.sprite = circle; ring.color = new Color32(11, 13, 27, 255);
                node.sizeDelta = new Vector2(46, 46);
                var disc = node.Find("Disc") as RectTransform;
                if (disc == null)
                {
                    disc = new GameObject("Disc", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
                    disc.gameObject.layer = node.gameObject.layer;
                    disc.SetParent(node, false);
                    disc.SetAsFirstSibling();
                    var image = disc.GetComponent<Image>(); image.sprite = circle; image.raycastTarget = false;
                }
                disc.anchorMin = disc.anchorMax = disc.pivot = new Vector2(.5f, .5f);
                disc.anchoredPosition = Vector2.zero; disc.sizeDelta = new Vector2(38, 38);
                disc.GetComponent<Image>().color = new Color32(255, 207, 0, 255);
                icon.transform.SetParent(disc, false);
                var iconRect = icon.rectTransform;
                iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(.5f, .5f);
                iconRect.anchoredPosition = Vector2.zero; iconRect.sizeDelta = new Vector2(26, 26);
                icon.preserveAspect = true;
                var number = node.Find("Number") as RectTransform;
                if (number != null)
                {
                    number.anchorMin = number.anchorMax = new Vector2(.5f, 0); number.pivot = new Vector2(.5f, 1);
                    number.anchoredPosition = new Vector2(0, -2); number.sizeDelta = new Vector2(40, 18);
                }
            }
        }

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        { rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h); }
        private static Sprite Sprite(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().First();
    }
}

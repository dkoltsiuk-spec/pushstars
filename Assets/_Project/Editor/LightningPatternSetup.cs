using System;
using System.IO;
using System.Linq;
using System.Text;
using PushStars.Fight;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>Updates only the background pattern in the authored presentation scenes.</summary>
    public static class LightningPatternSetup
    {
        [MenuItem("Tools/Push Stars/UI/Resize Reward Lightning Checkerboard")]
        public static void ResizeRewardPatterns()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scene edits first.");
            try
            {
                foreach (string name in new[] { "RewardSummary", "CaseAward", "CaseOpening", "CaseReward" })
                {
                    var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/" + name + ".unity");
                    foreach (var field in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LightningField>(true)))
                    {
                        field.ArrangeCheckerboard();
                        Validate(field);
                    }
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        [MenuItem("Tools/Push Stars/UI/Update Presentation Lightning")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var sprite = Resources.Load<PushStarsTheme>("PushStarsTheme").IconLightningBG;
            if (sprite == null) throw new InvalidOperationException("Missing lightning artwork.");
            var report = new StringBuilder();
            string backup = "output/lightning-pattern/backups/" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            Directory.CreateDirectory(backup);
            foreach (string name in new[] { "FightPreparation", "FightResults", "Training", "RewardSummary", "CaseAward", "CaseOpening", "CaseReward" })
            {
                string path = "Assets/_Project/Scenes/" + name + ".unity";
                var scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (!opened && scene.isDirty) throw new InvalidOperationException("Unsaved scene: " + path);
                File.Copy(path, backup + "/" + name + ".unity");
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    var roots = scene.GetRootGameObjects();
                    LightningField field;
                    if (name == "FightPreparation")
                    {
                        var backdrop = roots.SelectMany(r => r.GetComponentsInChildren<ReadyScreenGraphic>(true)).Single();
                        // This scene already uses the half-cell lattice. Preserve authored objects.
                        field = backdrop.GetComponentInChildren<LightningField>(true);
                        if (field == null) field = ReplacePattern(backdrop.rectTransform, sprite);
                    }
                    else if (name == "FightResults")
                    {
                        var screen = roots.SelectMany(r => r.GetComponentsInChildren<FightResultScreen>(true)).Single();
                        var backdrop = screen.Root.transform.Find("DuelResultBackdrop") as RectTransform;
                        if (backdrop == null)
                        {
                            backdrop = new GameObject("DuelResultBackdrop", typeof(RectTransform), typeof(ReadyScreenGraphic)).GetComponent<RectTransform>();
                            backdrop.SetParent(screen.Root.transform, false); backdrop.SetAsFirstSibling();
                            backdrop.anchorMin = Vector2.zero; backdrop.anchorMax = Vector2.one;
                            backdrop.offsetMin = backdrop.offsetMax = Vector2.zero;
                            backdrop.GetComponent<ReadyScreenGraphic>().raycastTarget = false;
                        }
                        field = ReplacePattern(backdrop, sprite);
                    }
                    else if (name == "Training")
                    {
                        var screen = roots.SelectMany(r => r.GetComponentsInChildren<TrainingScreen>(true)).Single();
                        var data = new SerializedObject(screen);
                        // The shared field owns scrolling; the old per-bolt driver must not move it again.
                        data.FindProperty("_bolts").arraySize = 0;
                        data.ApplyModifiedPropertiesWithoutUndo();
                        var parent = screen.transform.Find("TrainingScreen") as RectTransform;
                        field = ReplacePattern(parent, sprite);
                    }
                    else
                    {
                        var canvas = roots.SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).Single();
                        field = ReplacePattern((RectTransform)canvas.transform, sprite);
                    }
                    Validate(field);
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + path);
                    report.AppendLine("PASS " + name + ": visible lightning, alternate rows offset by 45 of 90 points, even wrap cycle, behind screen content.");
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
            File.WriteAllText("output/lightning-pattern/validation.txt", report.ToString());
            Debug.Log(report.ToString());
        }

        private static LightningField ReplacePattern(RectTransform parent, Sprite sprite)
        {
            if (parent == null) throw new InvalidOperationException("Missing background pattern parent.");
            var previous = parent.Find("LightningPattern");
            int index = previous != null ? previous.GetSiblingIndex() : parent.GetComponent<ReadyScreenGraphic>() != null ? 0 : 1;
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            var field = LightningField.Build(parent, sprite);
            field.transform.SetSiblingIndex(index);
            return field;
        }

        private static void Validate(LightningField field)
        {
            if (field == null || !field.enabled || !field.gameObject.activeSelf) throw new InvalidOperationException("Lightning is disabled.");
            var first = (RectTransform)field.transform.Find("L0_0");
            var next = (RectTransform)field.transform.Find("L0_1");
            var second = (RectTransform)field.transform.Find("L1_0");
            var third = (RectTransform)field.transform.Find("L2_0");
            float step = next.anchoredPosition.x - first.anchoredPosition.x;
            if (Mathf.Abs(second.anchoredPosition.x - first.anchoredPosition.x - step * .5f) > .01f
                || Mathf.Abs(third.anchoredPosition.x - first.anchoredPosition.x) > .01f)
                throw new InvalidOperationException("Lightning rows are not staggered by half a cell.");
            var data = new SerializedObject(field);
            float rowHeight = first.anchoredPosition.y - second.anchoredPosition.y;
            float rows = data.FindProperty("_spanY").floatValue / rowHeight;
            if (Mathf.Abs(rows / 2f - Mathf.Round(rows / 2f)) > .001f)
                throw new InvalidOperationException("Scrolling changes row parity at the seam.");
            var bolts = field.GetComponentsInChildren<Image>(true);
            if (bolts.Any(i => i.raycastTarget || i.sprite == null) || bolts.Max(i => i.color.a) < .8f)
                throw new InvalidOperationException("Missing, imperceptible or interactive background bolts.");
        }
    }
}

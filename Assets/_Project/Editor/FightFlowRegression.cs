using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using PushStars.CV;
using PushStars.Fight;
using PushStars.UI;
using PushStars.UI.Layout;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>Checks the real serialized scenes in disposable copies; never runs a UI builder.</summary>
    public static class FightFlowRegression
    {
        public static readonly FightScreen[] Screens =
        {
            FightScreen.Preparation, FightScreen.Battle, FightScreen.Results, FightScreen.RewardSummary,
            FightScreen.CaseAward, FightScreen.CaseOpening, FightScreen.CaseReward
        };

        [MenuItem("Tools/Push Stars/Rewards/Validate Fight Screen Bindings", priority = 341)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run the authored scene check outside Play mode.");
            var report = new StringBuilder("Authored fight screen regression — " + DateTime.UtcNow.ToString("u") + "\n");
            Scene previous = SceneManager.GetActiveScene();
            try
            {
                foreach (FightScreen screen in Screens) ValidateScene(screen, report);
                foreach (string sprite in new[] { "CaseCommon", "CaseRare", "CaseEpic", "CaseLegendary", "Gems" })
                    Require(Resources.Load<Sprite>("Rewards/" + sprite) != null, "Missing reward sprite: " + sprite);
                report.AppendLine("PASS: four rarity sprites and gems are imported Resources assets.");
                report.AppendLine("RESULT: PASS. Seven authored scenes checked; no source scenes, gameplay or reward saves changed.");
                Debug.Log("[FightFlowRegression] PASS. Logs/fight-flow-regression.txt");
            }
            catch (Exception exception)
            {
                report.AppendLine("RESULT: FAIL " + exception);
                throw;
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/fight-flow-regression.txt", report.ToString());
            }
        }

        private static void ValidateScene(FightScreen screen, StringBuilder report)
        {
            string source = FightScreenNavigation.ScenePath(screen);
            Require(File.Exists(source), "Authored scene is missing: " + source);
            Require(EditorBuildSettings.scenes.Any(item => item.enabled && item.path == source), "Scene is absent/disabled in Build Settings: " + source);
            byte[] original = File.ReadAllBytes(source);
            string copy = "Assets/__FightSceneRegression_" + Guid.NewGuid().ToString("N") + ".unity";
            Scene scene = default;
            try
            {
                File.Copy(source, copy, false);
                AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceSynchronousImport);
                scene = EditorSceneManager.OpenScene(copy, OpenSceneMode.Additive);
                var roots = FindAll<ScreenLayoutRoot>(scene).ToArray();
                Require(roots.Length > 0, "Scene has no serialized ScreenLayoutRoot: " + source);
                var layouts = new Dictionary<string, ScreenLayoutRoot>(StringComparer.Ordinal);
                foreach (var layout in roots)
                {
                    Require(layout.IsSceneAuthored, "Layout still applies legacy runtime geometry: " + source + "/" + layout.ScreenId);
                    Require(!layouts.ContainsKey(layout.ScreenId), "Duplicate layout ID in " + source + ": " + layout.ScreenId);
                    layouts.Add(layout.ScreenId, layout);
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    var rects = new HashSet<RectTransform>();
                    Require(layout.Targets.Count >= 5, "Too few serialized targets: " + source + "/" + layout.ScreenId);
                    foreach (var target in layout.Targets)
                    {
                        Require(target != null && !string.IsNullOrEmpty(target.id) && target.rect != null && ids.Add(target.id) && rects.Add(target.rect),
                            "Missing/duplicate target in " + source + "/" + layout.ScreenId);
                        Require(target.rect.gameObject.scene == scene, "Target refers to another scene: " + target.id);
                        Require(target.rect.GetComponent<SafeAreaFitter>() == null, "Safe area was registered instead of an editable element: " + target.id);
                    }
                }
                foreach (string id in ExpectedLayouts(screen))
                    Require(layouts.ContainsKey(id), "Missing serialized layout " + id + " in " + source);
                var canvases = FindAll<Canvas>(scene).Where(item => item.enabled && item.gameObject.activeInHierarchy).ToArray();
                Require(canvases.Length > 0, "No visible authored Canvas in " + source);
                int visibleLabels = FindAll<TextMeshProUGUI>(scene).Count(label => label.enabled && label.gameObject.activeInHierarchy &&
                    !string.IsNullOrWhiteSpace(label.text) && label.color.a > 0f);
                int visibleGraphics = FindAll<Graphic>(scene).Count(item => item.enabled && item.gameObject.activeInHierarchy);
                Require(visibleLabels >= 3 && visibleGraphics >= 5, "Scene is blank until Play/build code runs: " + source);
                Require(!FindAll<FightRewardFlow>(scene).Any(), "Legacy runtime reward builder is still attached: " + source);
                if (screen == FightScreen.Battle)
                {
                    Require(FindAll<FightController>(scene).Count() == 1 && FindAll<FightHud>(scene).Count() == 1,
                        "Battle must contain one controller and HUD.");
                    var portraits = FindAll<ScenePortraitPreview>(scene).ToArray();
                    Require(portraits.Length == 2 && portraits.All(item => item.GetComponent<RawImage>().texture is Texture2D),
                        "Battle must display both baked character portraits outside Play Mode.");
                }
                else
                {
                    Require(!FindAll<FightController>(scene).Any(), "Presentation scene contains a FightController: " + source);
                    Require(!FindAll<MonoBehaviour>(scene).Any(item => item is IPoseSource), "Presentation scene contains camera/CV tracking: " + source);
                }
                if (screen == FightScreen.Preparation)
                    Require(FindAll<DuelReadyPanel>(scene).SingleOrDefault()?.IsSceneAuthored == true, "Preparation panel is not marked as authored.");
                if (screen == FightScreen.Results)
                    Require(FindAll<FightResultScreen>(scene).SingleOrDefault()?.IsSceneAuthored == true, "Results panel is not marked as authored.");
                if (screen >= FightScreen.RewardSummary)
                    Require(FindAll<RewardScreen>(scene).SingleOrDefault()?.Screen == screen, "RewardScreen kind/binding does not match " + source);
                ValidateReferences(scene, source);
                report.AppendLine($"PASS {screen}: visible in Edit Mode, {visibleLabels} labels / {roots.Sum(root => root.Targets.Count)} serialized editable targets, valid references.");

                // The normal Rect-tool + Ctrl+S operation, performed only in our scene copy.
                var selectedLayout = roots.First(root => root.Targets.Any(target => target.rect.gameObject.activeInHierarchy));
                var selected = selectedLayout.Targets.First(target => target.rect.gameObject.activeInHierarchy);
                string layoutId = selectedLayout.ScreenId, targetId = selected.id;
                Vector2 position = selected.rect.anchoredPosition + new Vector2(17f, -11f);
                Vector2 size = selected.rect.sizeDelta + new Vector2(7f, 5f);
                selected.rect.anchoredPosition = position;
                selected.rect.sizeDelta = size;
                EditorUtility.SetDirty(selected.rect);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, copy), "Could not save temporary edited scene.");
                EditorSceneManager.CloseScene(scene, true);
                scene = EditorSceneManager.OpenScene(copy, OpenSceneMode.Additive);
                var reopened = FindAll<ScreenLayoutRoot>(scene).Single(root => root.ScreenId == layoutId)
                    .Targets.Single(target => target.id == targetId).rect;
                Require((reopened.anchoredPosition - position).sqrMagnitude < .0001f && (reopened.sizeDelta - size).sqrMagnitude < .0001f,
                    "Saving/reopening lost the edited geometry: " + screen + "/" + targetId);
                Require(original.SequenceEqual(File.ReadAllBytes(source)), "Validation modified the original scene: " + source);
                report.AppendLine("PASS " + screen + ": moved/resized target persisted through scene save + reopen.");
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (File.Exists(copy)) AssetDatabase.DeleteAsset(copy);
            }
        }

        public static IEnumerable<string> ExpectedLayouts(FightScreen screen)
        {
            switch (screen)
            {
                case FightScreen.Preparation: yield return "preparation"; break;
                case FightScreen.Battle: yield return "battle"; yield return "battle-solo"; break;
                case FightScreen.Results: yield return "results"; break;
                case FightScreen.RewardSummary: yield return "reward-summary"; break;
                case FightScreen.CaseAward: yield return "case-award"; break;
                case FightScreen.CaseOpening: yield return "case-opening"; break;
                case FightScreen.CaseReward: yield return "case-reward"; break;
            }
        }

        private static void ValidateReferences(Scene scene, string context)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    Require(component != null, "Missing MonoBehaviour script in " + context);
                    var serialized = new SerializedObject(component);
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        Object reference = property.objectReferenceValue;
                        Require(reference != null || property.objectReferenceInstanceIDValue == 0,
                            "Broken object reference: " + context + "/" + component.name + "." + property.propertyPath);
                        if (reference == null || EditorUtility.IsPersistent(reference)) continue;
                        var target = reference as Component;
                        var targetObject = reference as GameObject;
                        if (target != null) targetObject = target.gameObject;
                        Require(targetObject != null && targetObject.scene == scene,
                            "Unserialized transient/foreign reference: " + context + "/" + component.name + "." + property.propertyPath);
                    }
                }
        }

        public static IEnumerable<T> FindAll<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (T component in root.GetComponentsInChildren<T>(true))
                    if (component != null) yield return component;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}

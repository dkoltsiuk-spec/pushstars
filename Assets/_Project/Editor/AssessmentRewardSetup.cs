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
    public static class AssessmentRewardSetup
    {
        [MenuItem("Tools/Push Stars/Rewards/Configure Assessment Aura")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            Edit("RewardSummary", ConfigureSummary);
            Edit("CaseOpening", ConfigureAura);
            Edit("CaseReward", ConfigureAura);
            AssetDatabase.SaveAssets();
        }
        private static void Edit(string name, Action<RewardScreen> configure)
        {
            string path = "Assets/_Project/Scenes/" + name + ".unity";
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (!opened && scene.isDirty) throw new InvalidOperationException("Save scene edits before configuring " + name);
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RewardScreen>(true)).Single();
                configure(screen);
                FightPresentationSceneBuilder.PersistTextMaterials(screen.gameObject);
                EditorUtility.SetDirty(screen);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + name);
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
        private static Transform Find(RewardScreen screen, string name) => screen.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
        private static void ConfigureSummary(RewardScreen screen)
        {
            var ui = screen.SummaryUi;
            ui.Title = Find(screen, "Title").GetComponent<TextMeshProUGUI>();
            ui.Subtitle = Find(screen, "Subtitle").GetComponent<TextMeshProUGUI>();
            ui.TrophyGroup = Find(screen, "trophies").gameObject;
            var button = Find(screen, "continue-button");
            ui.ContinueLabel = button.GetComponentInChildren<TextMeshProUGUI>(true);
            ui.ContinueIcon = button.Find("HomeIcon")?.GetComponent<Image>();
            // Widen the existing authored button, including its face and shadow.
            var rect = (RectTransform)button;
            rect.sizeDelta = new Vector2(174, 52);
            foreach (string name in new[] { "MockupFace", "MockupShadow" })
            {
                var child = button.Find(name) as RectTransform;
                if (child == null) continue;
                child.sizeDelta = rect.sizeDelta;
                child.anchoredPosition = new Vector2(87, name == "MockupShadow" ? -31 : -26);
            }
            var composition = Find(screen, "Composition");
            var bonus = Child(composition, "AssessmentBonus", new Vector2(112, -298), new Vector2(184, 66));
            var icon = Child(bonus, "AuraIcon", new Vector2(20, -28), new Vector2(36, 44));
            var image = icon.GetComponent<Image>() ?? icon.gameObject.AddComponent<Image>();
            image.sprite = Resources.Load<PushStarsTheme>("PushStarsTheme").IconAura;
            image.preserveAspect = true; image.raycastTarget = false;
            Label(bonus, "BonusValue", new Vector2(105, -18), new Vector2(126, 29), "200 AURA", 22, new Color(.80f, .58f, 1));
            Label(bonus, "BonusCaption", new Vector2(106, -44), new Vector2(128, 22), "INSIDE YOUR CASE", 10, new Color(.8f, .77f, .9f));
            ui.AssessmentBonus = bonus.gameObject;
            bonus.gameObject.SetActive(false);
        }
        private static void ConfigureAura(RewardScreen screen)
        {
            var rig = screen.GetComponent<AuraRewardPresentation>() ?? screen.gameObject.AddComponent<AuraRewardPresentation>();
            rig.Screen = screen;
            var canvas = screen.GetComponentInChildren<Canvas>().rootCanvas;
            var layer = Child(canvas.transform, "AuraEnergy", Vector2.zero, Vector2.zero);
            layer.gameObject.layer = canvas.gameObject.layer;
            if (layer.GetComponent<CanvasRenderer>() == null) layer.gameObject.AddComponent<CanvasRenderer>();
            layer.anchorMin = Vector2.zero; layer.anchorMax = Vector2.one;
            layer.offsetMin = layer.offsetMax = Vector2.zero;
            layer.SetSiblingIndex(2);
            rig.Energy = layer.GetComponent<AuraEnergyGraphic>() ?? layer.gameObject.AddComponent<AuraEnergyGraphic>();
            rig.Energy.raycastTarget = false;
            const string materialPath = "Assets/_Project/Resources/Rewards/AuraVortex.mat";
            var fireMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            var fireShader = Shader.Find("PushStars/UI Aura Vortex");
            if (fireShader == null) throw new InvalidOperationException("Aura vortex shader did not import.");
            if (fireMaterial == null)
            {
                fireMaterial = new Material(fireShader) { name = "Aura Vortex" };
                AssetDatabase.CreateAsset(fireMaterial, materialPath);
            }
            else { fireMaterial.shader = fireShader; EditorUtility.SetDirty(fireMaterial); }
            rig.Energy.material = fireMaterial;
            layer.gameObject.SetActive(false);
            var data = new SerializedObject(screen);
            data.FindProperty("_auraPresentation").objectReferenceValue = rig;
            data.ApplyModifiedPropertiesWithoutUndo();
            if (screen.Screen == FightScreen.CaseReward)
            {
                var ui = screen.PrizeUi;
                rig.Title = Find(screen, "header").GetComponentInChildren<TextMeshProUGUI>(true);
                rig.PrizeIcon = Find(screen, "GemsArt").GetComponent<Image>();
                var flame = Child(ui.Content, "AuraFlame", Vector2.zero, Vector2.zero);
                flame.anchorMin = Vector2.zero; flame.anchorMax = Vector2.one;
                flame.offsetMin = flame.offsetMax = Vector2.zero;
                rig.Flame = flame.GetComponent<AuraFlameGraphic>();
                if (rig.Flame == null) rig.Flame = flame.gameObject.AddComponent<AuraFlameGraphic>();
                rig.Flame.raycastTarget = false;
                flame.gameObject.SetActive(false);
                rig.AmountGroup = ui.Amount.GetComponent<CanvasGroup>();
                if (rig.AmountGroup == null) rig.AmountGroup = ui.Amount.gameObject.AddComponent<CanvasGroup>();
                rig.TitleGroup = rig.Title.GetComponent<CanvasGroup>();
                if (rig.TitleGroup == null) rig.TitleGroup = rig.Title.gameObject.AddComponent<CanvasGroup>();
                rig.ClaimHint = Label(Find(screen, "Composition"), "AuraClaimHint", new Vector2(195, -768), new Vector2(330, 30), "TAP TO COLLECT", 15, new Color(.83f, .72f, 1));
                rig.ClaimHint.alignment = TextAlignmentOptions.Center;
                rig.ClaimHint.gameObject.SetActive(false);
            }
            EditorUtility.SetDirty(rig);
        }
        private static RectTransform Child(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rt = parent.Find(name) as RectTransform;
            if (rt == null) { rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent, false); }
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = position; rt.sizeDelta = size;
            return rt;
        }
        private static TextMeshProUGUI Label(Transform parent, string name, Vector2 position, Vector2 size, string value, float fontSize, Color tint)
        {
            var rt = Child(parent, name, position, size);
            var label = rt.GetComponent<TextMeshProUGUI>() ?? rt.gameObject.AddComponent<TextMeshProUGUI>();
            FightTypography.Apply(label, FightTypography.Role.Label);
            label.text = value; label.fontSize = label.fontSizeMax = fontSize; label.fontSizeMin = fontSize * .8f;
            label.enableAutoSizing = true; label.alignment = TextAlignmentOptions.Left;
            label.color = tint; label.raycastTarget = false;
            return label;
        }
    }
}

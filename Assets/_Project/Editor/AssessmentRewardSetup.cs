using System;
using System.IO;
using System.Linq;
using PushStars.Fight;
using PushStars.UI;
using PushStars.UI.Layout;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    /// <summary>Authors the post-fight reward scenes: the summary (no Aura row), the case screens
    /// (ordinary rarity backgrounds, no Aura rig) and the standalone AuraReward stamp scene.</summary>
    public static class AssessmentRewardSetup
    {
        private const string SceneFolder = "Assets/_Project/Scenes/";
        public const string AuraScenePath = SceneFolder + "AuraReward.unity";

        [MenuItem("Tools/Push Stars/Rewards/Configure Assessment Aura")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            BuildAuraScene();
            Edit("RewardSummary", ConfigureSummary);
            Edit("CaseOpening", RemoveAuraRigs);
            Edit("CaseReward", RemoveAuraRigs);
            AssetDatabase.SaveAssets();
        }
        private static void Edit(string name, Action<RewardScreen> configure)
        {
            string path = SceneFolder + name + ".unity";
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
        private static Transform Find(Component root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
        private static void DestroyNamed(Component root, params string[] names)
        {
            foreach (string name in names)
            {
                var found = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
                if (found != null) UnityEngine.Object.DestroyImmediate(found.gameObject);
            }
        }
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
            // Aura is no longer announced here ("200 AURA inside your case"); it has its own screen.
            DestroyNamed(screen, "AssessmentBonus");
        }
        /// <summary>Case screens show an ordinary case on its rarity background: no vortex charge,
        /// no stamp. Aura is presented by the AuraReward scene instead.</summary>
        private static void RemoveAuraRigs(RewardScreen screen)
        {
            foreach (var behaviour in screen.GetComponents<AuraStampPresentation>())
                UnityEngine.Object.DestroyImmediate(behaviour, true);
            // The retired vortex rig's scripts are gone; drop its objects and dangling components.
            DestroyNamed(screen, "AuraEnergy", "AuraFlame", "AuraClaimHint", "AuraStamp", "AuraStampFlash", "AuraBackground", "AuraMonoShade");
            foreach (var transform in screen.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
        }

        /// <summary>Regenerates AuraReward.unity from CaseReward's canvas, safe area and camera, with
        /// the case content removed and the stamp rig built fresh.</summary>
        private static void BuildAuraScene()
        {
            var previous = SceneManager.GetActiveScene();
            var loaded = SceneManager.GetSceneByPath(AuraScenePath);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                if (loaded.isDirty) throw new InvalidOperationException("Save AuraReward edits before regenerating it.");
                EditorSceneManager.CloseScene(loaded, true);
            }
            // Overwrite the existing file in place: its .meta, and so the GUID that Build Settings
            // and scene references key on, survives regeneration. CopyAsset would mint a new GUID.
            const string source = SceneFolder + "CaseReward.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(AuraScenePath) != null)
            {
                File.Copy(source, AuraScenePath, true);
                AssetDatabase.ImportAsset(AuraScenePath, ImportAssetOptions.ForceUpdate);
            }
            else if (!AssetDatabase.CopyAsset(source, AuraScenePath))
                throw new InvalidOperationException("Could not copy CaseReward into AuraReward");
            var scene = EditorSceneManager.OpenScene(AuraScenePath, OpenSceneMode.Additive);
            try
            {
                var root = scene.GetRootGameObjects().Single(r => r.GetComponentInChildren<RewardScreen>(true) != null);
                root.name = "AuraReward";
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
                foreach (var behaviour in root.GetComponents<MonoBehaviour>())
                    UnityEngine.Object.DestroyImmediate(behaviour, true);
                var screen = root.AddComponent<AuraRewardScreen>();
                DestroyNamed(screen, "header", "rarity", "gems", "amount", "receipt", "claim-button", "home-button", "LightningPattern");

                var layout = root.GetComponentInChildren<ScreenLayoutRoot>(true);
                if (layout != null)
                {
                    var layoutData = new SerializedObject(layout);
                    layoutData.FindProperty("_screenId").stringValue = "aura-reward";
                    layoutData.FindProperty("_targets").arraySize = 0;
                    layoutData.ApplyModifiedPropertiesWithoutUndo();
                }

                var rig = ConfigureStamp(screen);
                if (rig.Backdrop != null) rig.Backdrop.Appearance = FightRewardBackdrop.Style.Aura;
                var data = new SerializedObject(screen);
                data.FindProperty("_stamp").objectReferenceValue = rig;
                data.ApplyModifiedPropertiesWithoutUndo();

                var canvas = screen.GetComponentInChildren<Canvas>().rootCanvas;
                var tap = canvas.transform.Find("PrizeScreenTap");
                tap.name = "CollectTap";
                tap.SetAsLastSibling();
                var button = tap.GetComponent<Button>();
                for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                    UnityEventTools.RemovePersistentListener(button.onClick, i);
                UnityEventTools.AddVoidPersistentListener(button.onClick, screen.Collect);

                FightPresentationSceneBuilder.PersistTextMaterials(root);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save AuraReward");
                RegisterAuraScene();
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
        /// <summary>Adds AuraReward to Build Settings, or repairs an entry whose path matches but
        /// whose GUID is stale (left behind when the scene used to be recreated with a new GUID).</summary>
        private static void RegisterAuraScene()
        {
            var guid = new GUID(AssetDatabase.AssetPathToGUID(AuraScenePath));
            var scenes = EditorBuildSettings.scenes.ToList();
            int index = scenes.FindIndex(s => s.path == AuraScenePath || s.guid == guid);
            if (index >= 0 && scenes[index].guid == guid && scenes[index].path == AuraScenePath) return;
            bool enabled = index < 0 || scenes[index].enabled;
            if (index >= 0)
            {
                // The setter keeps an entry's old GUID while its path is unchanged, so an in-place
                // replacement is ignored: drop the stale entry first, then add the right one.
                scenes.RemoveAt(index);
                EditorBuildSettings.scenes = scenes.ToArray();
            }
            else index = scenes.Count;
            scenes.Insert(index, new EditorBuildSettingsScene(guid, enabled));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private const string StampMaterialPath = "Assets/_Project/Resources/Rewards/AuraStampText.mat";
        private const string GhostMaterialPath = "Assets/_Project/Resources/Rewards/AuraStampGhost.mat";
        private const string SkullPath = "Assets/_Project/Resources/Rewards/AuraSkull.png";
        private const int SkullSliceCount = 14;
        private static readonly Vector2 StampCenter = new Vector2(195, -470);
        /// <summary>The "+N AURA" meme stamp and skull.</summary>
        private static AuraStampPresentation ConfigureStamp(Component owner)
        {
            var canvas = owner.GetComponentInChildren<Canvas>().rootCanvas;
            var composition = Find(owner, "Composition");
            var rig = Ensure<AuraStampPresentation>(owner);

            var flash = Stretch(canvas.transform, "AuraStampFlash");
            rig.Flash = Ensure<Image>(flash);
            rig.Flash.color = new Color(.93f, .82f, 1, 0); rig.Flash.raycastTarget = false;
            flash.gameObject.SetActive(false);

            var shade = Stretch(canvas.transform, "AuraMonoShade");
            var backdrop = canvas.GetComponentInChildren<FightRewardBackdrop>(true);
            shade.SetSiblingIndex(backdrop != null ? backdrop.transform.GetSiblingIndex() + 1 : 0);
            rig.MonoShade = Ensure<Image>(shade);
            rig.MonoShade.color = new Color(.025f, .025f, .03f, 0); rig.MonoShade.raycastTarget = false;
            shade.gameObject.SetActive(false);

            rig.Root = Stretch(composition, "AuraStamp");
            rig.Root.SetAsLastSibling();
            var rootGroup = Ensure<CanvasGroup>(rig.Root);
            rootGroup.blocksRaycasts = false; rootGroup.interactable = false;
            rig.Shaker = Stretch(rig.Root, "Shaker");
            var body = Stretch(rig.Shaker, "Body");
            rig.Body = Ensure<CanvasGroup>(body);
            rig.Body.blocksRaycasts = false;
            var fx = Child(body, "Fx", StampCenter, new Vector2(1000, 1000));
            Ensure<CanvasRenderer>(fx);
            rig.Fx = Ensure<AuraStampFxGraphic>(fx);
            rig.Fx.raycastTarget = false;
            rig.Stamp = Child(body, "Stamp", StampCenter, new Vector2(420, 260));
            rig.Stamp.localRotation = Quaternion.identity;

            var stampMaterial = TextMaterial(StampMaterialPath, true);
            var ghostMaterial = TextMaterial(GhostMaterialPath, false);
            rig.GhostA = Stretch(rig.Stamp, "GhostA");
            rig.GhostB = Stretch(rig.Stamp, "GhostB");
            rig.GhostNumberA = StampText(rig.GhostA, "Number", true, new Color(.2f, .9f, 1f), ghostMaterial);
            rig.GhostWordA = StampText(rig.GhostA, "Word", false, new Color(.2f, .9f, 1f), ghostMaterial);
            rig.GhostNumberB = StampText(rig.GhostB, "Number", true, new Color(1f, .24f, .86f), ghostMaterial);
            rig.GhostWordB = StampText(rig.GhostB, "Word", false, new Color(1f, .24f, .86f), ghostMaterial);
            rig.Number = StampText(rig.Stamp, "Number", true, Color.white, stampMaterial);
            rig.Word = StampText(rig.Stamp, "Word", false, Color.white, stampMaterial);

            // The skull is cut into horizontal strips so the glitch can tear it sideways.
            rig.Skull = Child(rig.Shaker, "Skull", new Vector2(195, -262), new Vector2(190, 190));
            rig.SkullGroup = Ensure<CanvasGroup>(rig.Skull);
            rig.SkullGroup.alpha = 0; rig.SkullGroup.blocksRaycasts = false;
            var importer = (TextureImporter)AssetImporter.GetAtPath(SkullPath);
            if (importer.mipmapEnabled || !importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            var skullTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(SkullPath);
            rig.SkullSlices = new RawImage[SkullSliceCount];
            for (int i = 0; i < SkullSliceCount; i++)
            {
                var slice = Stretch(rig.Skull, "Slice" + i);
                slice.anchorMin = new Vector2(0, i / (float)SkullSliceCount);
                slice.anchorMax = new Vector2(1, (i + 1) / (float)SkullSliceCount);
                slice.offsetMin = slice.offsetMax = Vector2.zero;
                var image = Ensure<RawImage>(slice);
                image.texture = skullTexture;
                image.uvRect = new Rect(0, i / (float)SkullSliceCount, 1, 1f / SkullSliceCount);
                image.raycastTarget = false;
                rig.SkullSlices[i] = image;
            }

            rig.ClaimHint = Label(rig.Root, "ClaimHint", new Vector2(195, -768), new Vector2(330, 30), "TAP TO COLLECT", 15, new Color(.83f, .72f, 1));
            rig.ClaimHint.alignment = TextAlignmentOptions.Center;
            // The fight's Aura moments, one per line between the stamp and the tap hint.
            rig.Moments = Label(rig.Body.transform, "Moments", new Vector2(195, -680), new Vector2(340, 150),
                "VICTORY  +1000\nPERFECT FORM  +500", 20, Color.white);
            rig.Moments.alignment = TextAlignmentOptions.Top;
            rig.Moments.fontSizeMin = 13;
            rig.Moments.lineSpacing = 4;
            rig.Moments.richText = true;
            rig.Root.gameObject.SetActive(false);
            rig.Backdrop = backdrop;
            EditorUtility.SetDirty(rig);
            return rig;
        }
        /// <summary>Upright bold lettering; only the number leans a touch (the "+N" of the meme).</summary>
        private static TextMeshProUGUI StampText(Transform parent, string name, bool number, Color tint, Material material)
        {
            var rt = Child(parent, name, number ? new Vector2(210, -100) : new Vector2(210, -176), number ? new Vector2(440, 150) : new Vector2(440, 80));
            rt.localRotation = Quaternion.Euler(0, 0, number ? NumberLean : 0);
            var label = Ensure<TextMeshProUGUI>(rt);
            FightTypography.Apply(label, FightTypography.Role.Label);
            label.text = number ? "+200" : "AURA";
            label.enableAutoSizing = false;
            label.fontSize = number ? 116 : 56;
            label.characterSpacing = number ? -3 : 0;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            // Spread the outline texture across the whole word: a left-to-right keyline gradient.
            label.horizontalMapping = TextureMappingOptions.Paragraph;
            label.verticalMapping = TextureMappingOptions.Character;
            label.color = tint; label.raycastTarget = false;
            label.fontSharedMaterial = material;
            return label;
        }
        private const float NumberLean = 4f;
        private const string OutlineGradientPath = "Assets/_Project/Resources/Rewards/AuraStampOutline.png";
        private static readonly Color OutlineLeft = new Color32(46, 12, 214, 255), OutlineRight = new Color32(120, 52, 236, 255);
        /// <summary>A crisp keyline whose colour runs indigo → violet across the text. No glow or
        /// soft shadow: the edge stays hard at every size.</summary>
        private static Material TextMaterial(string path, bool stamp)
        {
            var probe = new GameObject("probe").AddComponent<TextMeshProUGUI>();
            FightTypography.Apply(probe, FightTypography.Role.Label);
            var font = probe.font;
            UnityEngine.Object.DestroyImmediate(probe.gameObject);
            // Only the desktop SDF shader samples an outline texture; the ghosts need no outline.
            var shader = Shader.Find(stamp ? "TextMeshPro/Distance Field" : "TextMeshPro/Mobile/Distance Field");
            if (shader == null) throw new InvalidOperationException("TextMeshPro shader missing");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(font.material);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.shaderKeywords = new string[0];
            material.SetTexture("_MainTex", font.atlasTexture);
            material.SetFloat("_TextureWidth", font.atlasWidth);
            material.SetFloat("_TextureHeight", font.atlasHeight);
            material.SetFloat("_GradientScale", font.atlasPadding + 1);
            material.SetFloat("_WeightNormal", 0); material.SetFloat("_WeightBold", 0);
            material.SetColor("_FaceColor", Color.white);
            material.SetFloat("_OutlineSoftness", 0);
            material.SetFloat("_Sharpness", .6f);
            if (stamp)
            {
                material.SetColor("_OutlineColor", Color.white);
                material.SetTexture("_OutlineTex", OutlineGradient());
                material.SetFloat("_OutlineWidth", .3f);
                material.SetFloat("_FaceDilate", .3f);
            }
            else
            {
                material.SetFloat("_OutlineWidth", 0);
                material.SetFloat("_FaceDilate", .3f);
            }
            ShaderUtilities.UpdateShaderRatios(material);
            EditorUtility.SetDirty(material);
            return material;
        }
        private static Texture2D OutlineGradient()
        {
            var texture = new Texture2D(256, 4, TextureFormat.RGBA32, false);
            for (int x = 0; x < texture.width; x++)
            {
                var color = Color.Lerp(OutlineLeft, OutlineRight, x / (texture.width - 1f));
                for (int y = 0; y < texture.height; y++) texture.SetPixel(x, y, color);
            }
            System.IO.File.WriteAllBytes(OutlineGradientPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(OutlineGradientPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(OutlineGradientPath);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(OutlineGradientPath);
        }
        private static T Ensure<T>(Component owner) where T : Component
        {
            // Unity's fake-null for a missing component defeats ??, so test explicitly.
            var existing = owner.GetComponent<T>();
            return existing != null ? existing : owner.gameObject.AddComponent<T>();
        }
        private static RectTransform Stretch(Transform parent, string name)
        {
            var rt = parent.Find(name) as RectTransform;
            if (rt == null) { rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent, false); }
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(.5f, .5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one; rt.localRotation = Quaternion.identity;
            return rt;
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

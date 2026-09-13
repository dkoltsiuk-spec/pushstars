using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PushStars.Fight;
using PushStars.UI;
using PushStars.UI.Layout;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>One-time migration from the existing Fight art into independently authored scenes.</summary>
    public static class FightPresentationSceneBuilder
    {
        public const string PreparationPath = "Assets/_Project/Scenes/FightPreparation.unity";
        public const string ResultsPath = "Assets/_Project/Scenes/FightResults.unity";
        public const string StandingPortraitPath = "Assets/_Project/UI/Portraits/fight_standing.png";
        public const string StandingFemalePortraitPath = "Assets/_Project/UI/Portraits/fight_standing_female.png";
        public const string PronePortraitPath = "Assets/_Project/UI/Portraits/fight_pushup.png";
        private const string MaterialFolder = "Assets/_Project/UI/Fonts/FightScreenMaterials";

        public static void BuildAll()
        {
            BuildPreparation();
            BuildResults();
        }

        public static void BuildPreparation() => Build(PreparationPath, true);
        public static void BuildResults() => Build(ResultsPath, false);

        private static void Build(string destination, bool preparation)
        {
            if (File.Exists(destination)) return; // Once authored, scene geometry belongs to the user.
            string temporary = "Assets/_Project/Scenes/__PresentationCopy_" + Guid.NewGuid().ToString("N") + ".unity";
            if (!AssetDatabase.CopyAsset(FightSceneSetup.ScenePath, temporary))
                throw new InvalidOperationException("Cannot copy the Fight art source for " + destination);
            var previous = SceneManager.GetActiveScene();
            Scene copied = default;
            try
            {
                copied = EditorSceneManager.OpenScene(temporary, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(copied);
                var panel = InScene<DuelReadyPanel>(copied).Single();
                var results = InScene<FightResultScreen>(copied).Single();
                var canvas = panel.GetComponent<Canvas>();
                if (canvas == null) throw new InvalidOperationException("Fight source has no canvas on its panel host.");
                var stages = PrepareIndependentStages(copied);
                Texture2D portrait = EnsureStandingPortrait(stages.First());
                EnsurePortrait(stages.First(), StandingFemalePortraitPath, "WarriorIdle", 0.13f, 512, 1024, true);
                EnsurePortrait(stages.First(), PronePortraitPath, "PushUp", 0.5f, 960, 640);
                Canvas.ForceUpdateCanvases();

                GameObject presentation;
                RawImage playerSource;
                RawImage opponentSource;
                if (preparation)
                {
                    var theme = Resources.Load<PushStarsTheme>("PushStarsTheme");
                    panel.Show(new DuelReadyPanel.Side("BEASTCORE_DEV", 120, 32, 52),
                        new DuelReadyPanel.Side("OSKAT009", 98, 32, 52),
                        theme != null ? theme.FlagMoldova : null, theme != null ? theme.FlagGermany : null, false);
                    panel.MarkSceneAuthored(portrait);
                    presentation = panel.Root;
                    playerSource = panel.PlayerAvatarSource;
                    opponentSource = panel.OpponentAvatarSource;
                    var presenter = canvas.gameObject.AddComponent<PreparationScreen>();
                    var serialized = new SerializedObject(presenter);
                    Set(serialized, "_panel", panel);
                    Set(serialized, "_homeButton", AddHomeButton(presentation));
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                else
                {
                    results.ShowDuel(true, false, 21, 18, 90f, 85f, 33.3f, 1.6f, 570, 21, "OSKAT009", "BEASTCORE_DEV", false);
                    results.MarkSceneAuthored(portrait);
                    presentation = results.Root;
                    playerSource = results.PlayerAvatarSource;
                    opponentSource = results.OpponentAvatarSource;
                    var presenter = canvas.gameObject.AddComponent<ResultsScenePresenter>();
                    var serialized = new SerializedObject(presenter);
                    Set(serialized, "_screen", results);
                    Set(serialized, "_homeButton", AddHomeButton(presentation));
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                var sources = new GameObject("PortraitSources", typeof(RectTransform));
                sources.transform.SetParent(canvas.transform, false);
                HideSource(playerSource, sources.transform, "PlayerPortraitSource");
                HideSource(opponentSource, sources.transform, "OpponentPortraitSource");
                StripUnrelated(copied, canvas, presentation, sources, preparation, stages);
                canvas.name = preparation ? "PreparationCanvas" : "ResultsCanvas";
                presentation.name = preparation ? "PreparationScreen" : "ResultsScreen";
                foreach (var layout in canvas.GetComponentsInChildren<ScreenLayoutRoot>(true)) layout.MarkSceneAuthored();
                PersistTextMaterials(canvas.gameObject);
                Canvas.ForceUpdateCanvases();
                if (!EditorSceneManager.SaveScene(copied, destination))
                    throw new InvalidOperationException("Could not save " + destination);
                AddToBuildSettings(destination);
                Debug.Log("Created editable standalone scene: " + destination);
            }
            finally
            {
                if (copied.IsValid() && copied.isLoaded) EditorSceneManager.CloseScene(copied, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                AssetDatabase.DeleteAsset(temporary);
            }
        }

        private static FightAvatar[] PrepareIndependentStages(Scene scene)
        {
            var output = new List<FightAvatar>();
            foreach (var old in InScene<FightAvatar>(scene).ToArray())
            {
                var camera = old.StageCamera;
                var stage = camera != null ? camera.GetComponentInParent<CharacterStage>() : null;
                if (stage == null) continue;
                var avatar = stage.gameObject.AddComponent<FightAvatar>();
                EditorUtility.CopySerialized(old, avatar);
                RepairAvatarPrefabs(avatar);
                var serialized = new SerializedObject(avatar);
                Set(serialized, "_driverBehaviour", null);
                Set(serialized, "_holdFramingWhileMirroring", null);
                serialized.FindProperty("_alsoBound").arraySize = 0;
                serialized.FindProperty("_shadow").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                // The independent CharacterStage creates a private RenderTexture in Awake.
                camera.targetTexture = null;
                camera.enabled = false;
                output.Add(avatar);
            }
            if (output.Count != 2) throw new InvalidOperationException("Expected two independent character stages.");
            return output.ToArray();
        }

        public static void RepairAvatarPrefabs(FightAvatar avatar)
        {
            var serialized = new SerializedObject(avatar);
            foreach (var entry in new[] { ("_malePrefab", "Assets/Character/Main_man/MainMan.prefab"),
                ("_femalePrefab", "Assets/Character/Main_woman/MainWoman.prefab") })
            {
                var property = serialized.FindProperty(entry.Item1);
                if (property.objectReferenceValue == null)
                    property.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(entry.Item2);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void HideSource(RawImage source, Transform parent, string name)
        {
            if (source == null) return;
            source.name = name;
            source.transform.SetParent(parent, false);
            source.rectTransform.anchoredPosition = new Vector2(-10000f, -10000f);
            source.rectTransform.sizeDelta = Vector2.one;
            source.texture = null;
            source.enabled = false;
            source.gameObject.SetActive(true);
        }

        private static void StripUnrelated(Scene scene, Canvas canvas, GameObject presentation, GameObject sources,
            bool preparation, IReadOnlyCollection<FightAvatar> stages)
        {
            var keepRoots = new HashSet<GameObject> { canvas.transform.root.gameObject };
            foreach (var stage in stages) keepRoots.Add(stage.transform.root.gameObject);
            foreach (var system in InScene<EventSystem>(scene)) keepRoots.Add(system.transform.root.gameObject);
            foreach (var camera in InScene<Camera>(scene))
                if (camera.cullingMask == 0) keepRoots.Add(camera.transform.root.gameObject);
            foreach (var root in scene.GetRootGameObjects())
                if (!keepRoots.Contains(root)) Object.DestroyImmediate(root);

            foreach (Transform child in canvas.transform.Cast<Transform>().ToArray())
            {
                if (child.gameObject == presentation || child.gameObject == sources || child.name == "BaseBackground") continue;
                Object.DestroyImmediate(child.gameObject);
            }
            foreach (var behaviour in canvas.GetComponents<MonoBehaviour>())
            {
                if (behaviour is CanvasScaler || behaviour is GraphicRaycaster || behaviour is ThemeInitializer) continue;
                if (preparation && (behaviour is DuelReadyPanel || behaviour is PreparationScreen)) continue;
                if (!preparation && (behaviour is FightResultScreen || behaviour is ResultsScenePresenter)) continue;
                Object.DestroyImmediate(behaviour);
            }
        }

        private static Button AddHomeButton(GameObject root)
        {
            var button = UiBuilder.Button((RectTransform)root.transform, "Home", "HOME",
                new Color(0.07f, 0.08f, 0.15f, 0.9f), Color.white, 14f, out var label);
            UiBuilder.Place((RectTransform)button.transform, Vector2.one, new Vector2(-14f, -48f), new Vector2(82f, 32f));
            FightTypography.Apply(label, FightTypography.Role.Button);
            return button;
        }

        public static void PersistTextMaterials(GameObject root)
        {
            EnsureFolder(MaterialFolder);
            var resolved = new Dictionary<Material, Material>();
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                var source = text.fontSharedMaterial;
                if (source == null) continue;
                if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(source)))
                {
                    ClearMaterialInstance(text);
                    continue;
                }
                if (!resolved.TryGetValue(source, out var asset))
                {
                    string filename = string.Concat(source.name.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
                    filename += "_" + Hash128.Compute(EditorJsonUtility.ToJson(source)).ToString().Substring(0, 8);
                    string path = MaterialFolder + "/" + filename + ".mat";
                    asset = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (asset == null)
                    {
                        asset = new Material(source) { name = source.name, hideFlags = HideFlags.None };
                        AssetDatabase.CreateAsset(asset, path);
                    }
                    resolved[source] = asset;
                }
                text.fontSharedMaterial = asset;
                ClearMaterialInstance(text);
                EditorUtility.SetDirty(text);
            }
            AssetDatabase.SaveAssets();
        }

        private static void ClearMaterialInstance(TMP_Text text)
        {
            // TMP retains a private instance after assigning a shared preset. It is only a
            // cache; persisting it embeds stale duplicate materials inside the scene.
            var serialized = new SerializedObject(text);
            serialized.FindProperty("m_fontMaterial").objectReferenceValue = null;
            serialized.FindProperty("m_fontMaterials").arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Texture2D EnsureStandingPortrait(FightAvatar avatar) =>
            EnsurePortrait(avatar, StandingPortraitPath, "WarriorIdle", 0.13f, 512, 1024);

        public static Texture2D EnsureTrainingRestPortrait(FightAvatar avatar, bool female = false) =>
            EnsurePortrait(avatar, "Assets/_Project/UI/Portraits/training_rest" + (female ? "_female" : "") + ".png", "WarriorIdle", .13f, 960, 720, female, true);
        public static Texture2D EnsureTrainingPushupPortrait(FightAvatar avatar, bool female = false) =>
            EnsurePortrait(avatar, "Assets/_Project/UI/Portraits/training_pushup" + (female ? "_female" : "") + ".png", "PushUp", .5f, 720, 720, female);

        private static Texture2D EnsurePortrait(FightAvatar avatar, string assetPath, string pose, float poseTime, int width, int height, bool female = false, bool resting = false)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (existing != null) return existing;
            var serialized = new SerializedObject(avatar);
            var prefab = serialized.FindProperty(female ? "_femalePrefab" : "_malePrefab").objectReferenceValue as GameObject;
            if (prefab == null)
                prefab = serialized.FindProperty(female ? "_malePrefab" : "_femalePrefab").objectReferenceValue as GameObject;
            var avatarRoot = serialized.FindProperty("_avatarRoot").objectReferenceValue as Transform;
            if (prefab == null || avatarRoot == null) throw new InvalidOperationException("Missing portrait prefab or stage root.");
            GameObject character = null;
            GameObject cameraObject = null;
            RenderTexture render = null;
            Texture2D texture = null;
            var previousRender = RenderTexture.active;
            try
            {
                character = (GameObject)PrefabUtility.InstantiatePrefab(prefab, avatarRoot);
                character.transform.localPosition = Vector3.zero;
                character.transform.localRotation = Quaternion.identity;
                SetLayer(character.transform, avatarRoot.gameObject.layer);
                var animator = character.GetComponentInChildren<Animator>();
                if (animator != null)
                {
                    var controller = serialized.FindProperty("_fightController").objectReferenceValue as RuntimeAnimatorController;
                    if (controller != null) animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.Rebind();
                    animator.Play(pose, 0, poseTime);
                    animator.Update(0f);
                }
                if (resting && animator != null && animator.isHuman)
                {
                    using (var handler = new HumanPoseHandler(animator.avatar, animator.transform))
                    {
                        var human = new HumanPose(); handler.GetHumanPose(ref human);
                        for (int i = 0; i < human.muscles.Length; i++) human.muscles[i] = 0;
                        for (int i = 0; i < HumanTrait.MuscleName.Length; i++)
                        {
                            string muscle = HumanTrait.MuscleName[i];
                            if (muscle.Contains("Arm Down-Up")) human.muscles[i] = -.45f;
                            if (muscle.Contains("Leg In-Out")) human.muscles[i] = .15f;
                            if (muscle.Contains("Leg Stretch") || muscle.Contains("Forearm Stretch")) human.muscles[i] = 1f;
                        }
                        handler.SetHumanPose(ref human);
                    }
                    animator.enabled = false;
                    character.transform.localRotation = Quaternion.Euler(-20, 0, -62);
                }
                Bounds bounds = Measure(character);
                cameraObject = new GameObject("__PortraitBakeCamera", typeof(Camera));
                cameraObject.transform.SetParent(avatar.transform, false);
                var camera = cameraObject.GetComponent<Camera>();
                camera.CopyFrom(avatar.StageCamera);
                camera.enabled = false;
                camera.orthographic = true;
                camera.aspect = (float)width / height;
                camera.orthographicSize = Mathf.Max(bounds.extents.y * 1.06f, bounds.extents.x / camera.aspect * 1.08f);
                camera.transform.position = bounds.center + Vector3.forward * 5f;
                camera.transform.LookAt(bounds.center);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                render = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                render.Create();
                camera.targetTexture = render;
                camera.Render();
                RenderTexture.active = render;
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                // Imported skin bounds can include invisible geometry. Frame the rendered
                // silhouette, then render again; the saved portrait uses the whole editable box.
                var pixels = texture.GetPixels32();
                int left = width, right = -1, bottom = height, top = -1;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        if (pixels[y * width + x].a > 24)
                        {
                            left = Mathf.Min(left, x); right = Mathf.Max(right, x);
                            bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y);
                        }
                if (right > left && top > bottom)
                {
                    float fullHeight = camera.orthographicSize * 2f;
                    float centerX = (left + right + 1f) / (2f * width) - .5f;
                    float centerY = (bottom + top + 1f) / (2f * height) - .5f;
                    camera.transform.position += camera.transform.right * (centerX * fullHeight * camera.aspect)
                        + camera.transform.up * (centerY * fullHeight);
                    camera.orthographicSize *= Mathf.Max((top - bottom + 1f) / height, (right - left + 1f) / width) * 1.06f;
                    camera.Render();
                    texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    texture.Apply();
                }
                EnsureFolder("Assets/_Project/UI/Portraits");
                File.WriteAllBytes(assetPath, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }
            finally
            {
                RenderTexture.active = previousRender;
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                if (character != null) Object.DestroyImmediate(character);
                if (render != null) { render.Release(); Object.DestroyImmediate(render); }
                if (texture != null) Object.DestroyImmediate(texture);
            }
        }

        private static Bounds Measure(GameObject character)
        {
            Bounds result = default;
            bool hasBounds = false;
            foreach (var renderer in character.GetComponentsInChildren<Renderer>())
            {
                Bounds world = renderer.bounds;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    var mesh = new Mesh();
                    skin.BakeMesh(mesh);
                    var bounds = mesh.bounds;
                    world = new Bounds(skin.transform.TransformPoint(bounds.center), Vector3.zero);
                    for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        world.Encapsulate(skin.transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z))));
                    Object.DestroyImmediate(mesh);
                }
                if (!hasBounds) { result = world; hasBounds = true; }
                else result.Encapsulate(world);
            }
            if (!hasBounds) throw new InvalidOperationException("Portrait prefab has no renderers.");
            return result;
        }

        private static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root) SetLayer(child, layer);
        }

        private static IEnumerable<T> InScene<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));

        private static void Set(SerializedObject serialized, string field, Object value) =>
            serialized.FindProperty(field).objectReferenceValue = value;

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int separator = path.LastIndexOf('/');
            string parent = path.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
        }

        private static void AddToBuildSettings(string path)
        {
            if (EditorBuildSettings.scenes.Any(scene => scene.path == path)) return;
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(path, true) }).ToArray();
        }
    }
}

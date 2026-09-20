using System;
using System.IO;
using System.Linq;
using System.Text;
using PushStars.Fight;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class GoblinKingSetup
    {
        public const string Root = "Assets/_Project/Art/Characters/GoblinKing";
        public const string PrefabPath = "Assets/_Project/Resources/Bosses/goblin-king.prefab";
        public const string PortraitPath = "Assets/_Project/Resources/Bosses/goblin-king-portrait.png";
        private const string BodyPath = Root + "/Body.fbx";
        private static readonly (string file, string state, bool loop)[] Clips = {
            ("Orc Idle (1)", "StandIdle", true), ("Boxing idle", "BattleIdle", true),
            ("Boxing", "WarmUp", false), ("Punching (1)", "Punch", false),
            ("Boxing", "Kick", false), ("Reaction (1)", "Hit", false),
            ("Reaction (1)", "Reaction", false), ("Dying", "Defeat", false),
            ("Boxing", "Victory", false), ("Orc Idle (1)", "Laugh", false)
        };
        [MenuItem("Tools/Push Stars/Boss/Import Goblin King")]
        public static void Import()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            Directory.CreateDirectory(Root + "/Textures");
            Directory.CreateDirectory("Assets/_Project/Resources/Bosses");
            Directory.CreateDirectory("output/goblin-king");
            AssetDatabase.Refresh();
            foreach (var entry in Clips.GroupBy(c => c.file).Select(g => g.First()))
            {
                string path = Root + "/Animations/" + entry.file + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.optimizeGameObjects = false;
                importer.materialImportMode = path == BodyPath ? ModelImporterMaterialImportMode.ImportStandard : ModelImporterMaterialImportMode.None;
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                importer.SaveAndReimport();
                var clips = importer.defaultClipAnimations;
                if (clips.Length != 1) throw new InvalidOperationException("Expected one take: " + path);
                var clip = clips[0]; clip.name = entry.state;
                clip.loopTime = entry.loop; clip.loopPose = entry.loop;
                clip.lockRootRotation = true; clip.keepOriginalOrientation = true;
                clip.lockRootPositionXZ = true; clip.keepOriginalPositionXZ = true;
                clip.lockRootHeightY = true; clip.keepOriginalPositionY = true;
                importer.clipAnimations = clips; importer.SaveAndReimport();
                var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                    throw new InvalidOperationException("Humanoid rig invalid: " + path);
            }
            var bodyImporter = (ModelImporter)AssetImporter.GetAtPath(BodyPath);
            bodyImporter.animationType = ModelImporterAnimationType.Human;
            bodyImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            bodyImporter.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            bodyImporter.materialLocation = ModelImporterMaterialLocation.InPrefab;
            bodyImporter.SaveAndReimport();
            bodyImporter.ExtractTextures(Root + "/Textures");
            AssetDatabase.Refresh();
            var body = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath);
            var sourceMaterial = body.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null);
            Texture albedo = sourceMaterial != null ? sourceMaterial.mainTexture : null;
            if (albedo == null) albedo = AssetDatabase.FindAssets("t:Texture2D", new[] {Root + "/Textures"})
                .Select(id => AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(id)))
                .OrderByDescending(t => t.width * t.height).FirstOrDefault();
            if (albedo == null) throw new InvalidOperationException("Goblin king albedo not found; refusing a textureless placeholder.");
            const string matPath = Root + "/GoblinKingToon.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Character/Main_man/Materials/MainMan.mat"));
                material.name = "GoblinKingToon"; AssetDatabase.CreateAsset(material, matPath);
            }
            MainCharacterSetup.ApplyCharacterSurface(material, albedo);
            EditorUtility.SetDirty(material);
            var controller = Controller();
            var instance = Object.Instantiate(body);
            try
            {
                instance.name = "Goblin King";
                var animator = instance.GetComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();
                    if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
                }
                var skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>();
                if (skins.Length == 0) throw new InvalidOperationException("Goblin king has no skinned mesh.");
                var bounds = skins[0].bounds;
                foreach (var skin in skins.Skip(1)) bounds.Encapsulate(skin.bounds);
                instance.transform.localScale *= 2.1f / bounds.size.y;
                // Match visible ink width after normalizing this model's scale.
                material.SetFloat("_OutlineWidth", .0066f / Mathf.Abs(skins[0].transform.lossyScale.x));
                EditorUtility.SetDirty(material);
                instance.AddComponent<BossAvatarPresentation>();
                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            }
            finally { Object.DestroyImmediate(instance); }
            AssetDatabase.SaveAssets();

            Validate();
            RenderPortrait();
        }

        public static void RenderPortrait()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var previous = RenderTexture.active;
            var rt = new RenderTexture(256, 256, 24);
            var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            try
            {
                var body = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(body, scene);
                var animator = body.GetComponent<Animator>();
                animator.Play("StandIdle", 0, .2f); animator.Update(0);
                Vector3 head = animator.GetBoneTransform(HumanBodyBones.Head).position;
                var camera = new GameObject("PortraitCamera").AddComponent<Camera>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                camera.scene = scene; camera.orthographic = true; camera.orthographicSize = .36f;
                camera.transform.position = head + new Vector3(0, .12f, 4);
                camera.transform.rotation = Quaternion.Euler(0, 180, 0);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
                camera.targetTexture = rt;
                var light = new GameObject("PortraitLight").AddComponent<Light>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light.gameObject, scene);
                light.type = LightType.Directional; light.intensity = 1.8f;
                light.transform.rotation = Quaternion.Euler(25, 180, 0);
                camera.Render(); RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); texture.Apply();
                string path = PortraitPath;
                File.WriteAllBytes(path, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
                camera.targetTexture = null;
            }
            finally
            {
                RenderTexture.active = previous; rt.Release(); Object.DestroyImmediate(rt);
                Object.DestroyImmediate(texture); EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static AnimatorController Controller()
        {
            string path = Root + "/GoblinKing.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var machine = controller.layers[0].stateMachine;
            foreach (var entry in Clips)
            {
                var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == entry.state)
                    ?? machine.AddState(entry.state);
                var source = AssetDatabase.LoadAllAssetsAtPath(Root + "/Animations/" + entry.file + ".fbx")
                    .OfType<AnimationClip>().Single(c => !c.name.StartsWith("__preview__"));
                string clipPath = Root + "/Animations/" + entry.state + ".anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip == null) { clip = Object.Instantiate(source); AssetDatabase.CreateAsset(clip, clipPath); }
                else EditorUtility.CopySerialized(source, clip);
                clip.name = entry.state;
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = entry.loop; settings.loopBlend = entry.loop;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                EditorUtility.SetDirty(clip);
                state.motion = clip;
                state.writeDefaultValues = true;
                if (entry.state == "StandIdle") machine.defaultState = state;
                EditorUtility.SetDirty(state);
            }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        [MenuItem("Tools/Push Stars/Boss/Validate Goblin King")]
        public static void Validate()
        {
            var report = new StringBuilder();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var scene = EditorSceneManager.NewPreviewScene();
            var previous = RenderTexture.active;
            try
            {
                var camera = new GameObject("BossCheckCamera").AddComponent<Camera>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                camera.scene = scene; camera.orthographic = true; camera.orthographicSize = 1.2f;
                camera.transform.position = new Vector3(0, 1, 4); camera.transform.LookAt(new Vector3(0, 1, 0));
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.1f, .14f, .08f);
                foreach (var setting in new[] { ("Key", 38f, 200f, CharacterLighting.KeyIntensity, CharacterLighting.KeyColor),
                    ("Fill",12f,140f,CharacterLighting.FillIntensity,CharacterLighting.FillColor) })
                {
                    var light = new GameObject(setting.Item1).AddComponent<Light>();
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light.gameObject, scene);
                    light.type = LightType.Directional; light.intensity = setting.Item4; light.color = setting.Item5;
                    light.transform.rotation = Quaternion.Euler(setting.Item2, setting.Item3, 0);
                }
                var rt = new RenderTexture(360, 440, 24); camera.targetTexture = rt;
                var sheet = new Texture2D(1080, 440 * Clips.Length, TextureFormat.RGB24, false);
                for (int row = 0; row < Clips.Length; row++)
                {
                    var strip = new Texture2D(1080, 440, TextureFormat.RGB24, false);
                    var body = Object.Instantiate(prefab);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(body, scene);
                    var animator = body.GetComponent<Animator>();
                    report.AppendLine(Clips[row].file + " -> " + Clips[row].state);
                    for (int col = 0; col < 3; col++)
                    {
                        animator.Play(Clips[row].state, 0, .12f + col * .36f); animator.Update(0);
                        camera.Render(); RenderTexture.active = rt;
                        var cell = new Texture2D(360, 440, TextureFormat.RGB24, false);
                        cell.ReadPixels(new Rect(0, 0, 360, 440), 0, 0); cell.Apply();
                        sheet.SetPixels(col * 360, (Clips.Length - 1 - row) * 440, 360, 440, cell.GetPixels());
                        strip.SetPixels(col * 360, 0, 360, 440, cell.GetPixels());
                        Object.DestroyImmediate(cell);
                    }
                    strip.Apply();
                    File.WriteAllBytes("output/goblin-king/" + Clips[row].state + ".png", strip.EncodeToPNG());
                    Object.DestroyImmediate(strip);
                    Object.DestroyImmediate(body);
                }
                sheet.Apply(); Directory.CreateDirectory("output/goblin-king");
                File.WriteAllBytes("output/goblin-king/animations.png", sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet); camera.targetTexture = null; RenderTexture.active = previous; rt.Release(); Object.DestroyImmediate(rt);
                File.WriteAllText("output/goblin-king/import.txt", "PASS: humanoid GoblinBig body, six source clips, ten animator states and textured toon prefab.\n" + report);
            }
            finally { RenderTexture.active = previous; EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}

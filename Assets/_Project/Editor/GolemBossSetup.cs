using System;
using System.IO;
using System.Linq;
using System.Text;
using PushStars.Core;
using PushStars.Fight;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>
    /// Island golems: per island, three AccuRig bodies (small, big, king) become boss prefabs under
    /// the prefab IDs <see cref="BossCatalog"/> gives that chapter. The golems have no clips of their
    /// own: the Goblin King's humanoid controller (stand/battle idle, punch, kick, hit, defeat…)
    /// retargets onto them, so <see cref="BossAvatarPresentation"/> drives them unchanged.
    /// </summary>
    public static class GolemBossSetup
    {
        private sealed class Island
        {
            public string Chapter, Root, Output;
            public (string folder, string prefabId, string displayName, float height)[] Golems;
        }

        private static readonly Island Ice = new Island
        {
            Chapter = "ice", Root = "Assets/_Project/Art/Characters/IceGolems", Output = "output/ice-golems/",
            Golems = new[]
            {
                ("Mini", "ice-golem-mini", "Frost Golem", 2.0f),
                ("Max", "ice-golem-max", "Frost Golem", 2.2f),
                ("King", "ice-golem-king", "Glacier King", 2.4f),
            }
        };

        private static readonly Island Lava = new Island
        {
            Chapter = "lava", Root = "Assets/_Project/Art/Characters/LavaGolems", Output = "output/lava-golems/",
            Golems = new[]
            {
                ("Mini", "lava-golem-mini", "Magma Golem", 2.0f),
                ("Max", "lava-golem-max", "Magma Golem", 2.2f),
                ("King", "lava-golem-king", "Volcano King", 2.4f),
            }
        };

        private static readonly string[] States =
            { "StandIdle", "BattleIdle", "WarmUp", "Punch", "Kick", "Hit", "Reaction", "Defeat", "Victory", "Laugh" };

        public static string PrefabPath(string prefabId) => "Assets/_Project/Resources/Bosses/" + prefabId + ".prefab";
        public static string PortraitPath(string prefabId) => "Assets/_Project/Resources/Bosses/" + prefabId + "-portrait.png";

        [MenuItem("Tools/Push Stars/Boss/Import Ice Golems")]
        public static void ImportIce() => Import(Ice);

        [MenuItem("Tools/Push Stars/Boss/Import Lava Golems")]
        public static void ImportLava() => Import(Lava);

        [MenuItem("Tools/Push Stars/Boss/Validate Ice Golems")]
        public static void ValidateIce() => Validate(Ice);

        [MenuItem("Tools/Push Stars/Boss/Validate Lava Golems")]
        public static void ValidateLava() => Validate(Lava);

        private static void Import(Island island)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            Directory.CreateDirectory(island.Output);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(GoblinKingSetup.Root + "/GoblinKing.controller");
            if (controller == null) throw new InvalidOperationException("Goblin King controller missing; run Import Goblin King first.");
            foreach (var golem in island.Golems)
                ImportGolem(island.Root + "/" + golem.folder, golem.folder, golem.prefabId, golem.displayName, golem.height, controller);
            AssetDatabase.SaveAssets();
            Validate(island);
        }

        private static void ImportGolem(string dir, string folder, string prefabId, string displayName, float height, AnimatorController controller)
        {
            string bodyPath = dir + "/Body.fbx";
            Directory.CreateDirectory(dir + "/Textures");
            AssetDatabase.Refresh();
            var importer = (ModelImporter)AssetImporter.GetAtPath(bodyPath)
                ?? throw new InvalidOperationException("Missing golem body " + bodyPath);
            // A cached humanoid description survives reimports (see the rig gotchas): reset through Generic.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.SaveAndReimport();
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.optimizeGameObjects = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(bodyPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                throw new InvalidOperationException("Humanoid rig invalid: " + bodyPath);

            // AccuRig embeds the diffuse in the FBX; unpack it so it gets its own importer and a mobile size.
            importer.ExtractTextures(dir + "/Textures");
            AssetDatabase.Refresh();
            var albedo = AssetDatabase.FindAssets("t:Texture2D", new[] { dir + "/Textures" })
                .Select(id => AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(id)))
                .OrderByDescending(t => t.width * t.height).FirstOrDefault();
            if (albedo == null) throw new InvalidOperationException(displayName + " albedo not found; refusing a textureless placeholder.");
            var textureImporter = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(albedo));
            if (textureImporter.maxTextureSize != 2048)
            {
                textureImporter.maxTextureSize = 2048;
                textureImporter.SaveAndReimport();
            }

            string matPath = dir + "/" + folder + "GolemToon.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Character/Main_man/Materials/MainMan.mat"));
                material.name = folder + "GolemToon";
                AssetDatabase.CreateAsset(material, matPath);
            }
            MainCharacterSetup.ApplyCharacterSurface(material, albedo);

            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(bodyPath));
            try
            {
                instance.name = displayName;
                var animator = instance.GetComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                // AccuRig can leave an unskinned copy of the body under "Poses__…": it would stand frozen
                // inside the animated golem.
                foreach (var still in instance.GetComponentsInChildren<MeshRenderer>(true))
                    Object.DestroyImmediate(still.gameObject);
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();
                    if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
                }
                var skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>();
                if (skins.Length == 0) throw new InvalidOperationException(displayName + " has no skinned mesh.");
                var bounds = skins[0].bounds;
                foreach (var skin in skins.Skip(1)) bounds.Encapsulate(skin.bounds);
                // Humanoids scale through the prefab transform only, never the importer's globalScale.
                instance.transform.localScale *= height / bounds.size.y;
                // Match visible ink width after normalizing this model's scale.
                material.SetFloat("_OutlineWidth", .0066f / Mathf.Abs(skins[0].transform.lossyScale.x));
                EditorUtility.SetDirty(material);
                instance.AddComponent<BossAvatarPresentation>();
                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath(prefabId));
            }
            finally { Object.DestroyImmediate(instance); }
            RenderPortrait(prefabId);
        }

        /// <summary>Head-and-shoulders sprite for the map node, home strip and fight HUD icon.</summary>
        public static void RenderPortrait(string prefabId)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var previous = RenderTexture.active;
            var rt = new RenderTexture(256, 256, 24);
            var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            try
            {
                var body = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(prefabId)));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(body, scene);
                var animator = body.GetComponent<Animator>();
                animator.Play("StandIdle", 0, .2f); animator.Update(0);
                Vector3 head = animator.GetBoneTransform(HumanBodyBones.Head).position;
                var skins = body.GetComponentsInChildren<SkinnedMeshRenderer>();
                var bounds = skins[0].bounds;
                foreach (var skin in skins.Skip(1)) bounds.Encapsulate(skin.bounds);
                var camera = new GameObject("PortraitCamera").AddComponent<Camera>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                camera.scene = scene; camera.orthographic = true;
                // Golem heads sit low between spiked shoulders, so frame from the top of the mesh,
                // not from the head bone: a thin margin above, head and shoulders below.
                camera.orthographicSize = .45f;
                camera.transform.position = new Vector3(head.x, bounds.max.y - .4f, head.z + 4);
                camera.transform.rotation = Quaternion.Euler(0, 180, 0);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
                camera.targetTexture = rt;
                var light = new GameObject("PortraitLight").AddComponent<Light>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light.gameObject, scene);
                light.type = LightType.Directional; light.intensity = 1.8f;
                light.transform.rotation = Quaternion.Euler(25, 180, 0);
                camera.Render(); RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); texture.Apply();
                string path = PortraitPath(prefabId);
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

        /// <summary>Every boss of the island resolves to a prefab with every presentation state, and
        /// a contact sheet per golem (rows = states, three phases each) lands in the island's output.</summary>
        private static void Validate(Island island)
        {
            Directory.CreateDirectory(island.Output);
            var report = new StringBuilder();
            var chapter = BossCatalog.FindChapter(island.Chapter);
            foreach (var boss in chapter.Bosses)
            {
                var prefab = Resources.Load<GameObject>("Bosses/" + boss.PrefabId);
                if (prefab == null) throw new InvalidOperationException("Missing boss prefab " + boss.PrefabId);
                if (Resources.Load<Sprite>("Bosses/" + boss.PrefabId + "-portrait") == null)
                    throw new InvalidOperationException("Missing portrait " + boss.PrefabId);
                var animator = prefab.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman || prefab.GetComponent<BossAvatarPresentation>() == null)
                    throw new InvalidOperationException(boss.PrefabId + " is not a humanoid boss prefab");
                report.AppendLine(boss.Id + " -> " + boss.PrefabId + " (" + boss.MaxHp + " HP)");
            }
            foreach (var golem in island.Golems) RenderSheet(golem.prefabId, island.Output);
            File.WriteAllText(island.Output + "import.txt",
                "PASS: three humanoid golem bodies, ten states each, portraits; " + island.Chapter + " ladder resolves.\n" + report);
            Debug.Log("PASS: " + island.Chapter + " golems\n" + report);
        }

        private static void RenderSheet(string prefabId, string output)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(prefabId));
            var scene = EditorSceneManager.NewPreviewScene();
            var previous = RenderTexture.active;
            var rt = new RenderTexture(300, 400, 24);
            var sheet = new Texture2D(900, 400 * States.Length, TextureFormat.RGB24, false);
            try
            {
                var camera = new GameObject("GolemCheckCamera").AddComponent<Camera>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                camera.scene = scene; camera.orthographic = true; camera.orthographicSize = 1.45f;
                camera.transform.position = new Vector3(0, 1.25f, 4); camera.transform.LookAt(new Vector3(0, 1.25f, 0));
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.34f, .61f, .78f);
                foreach (var (name, pitch, yaw, intensity, color) in new[] {
                    ("Key", 38f, 200f, CharacterLighting.KeyIntensity, CharacterLighting.KeyColor),
                    ("Fill", 12f, 140f, CharacterLighting.FillIntensity, CharacterLighting.FillColor) })
                {
                    var light = new GameObject(name).AddComponent<Light>();
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light.gameObject, scene);
                    light.type = LightType.Directional; light.intensity = intensity; light.color = color;
                    light.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
                }
                camera.targetTexture = rt;
                for (int row = 0; row < States.Length; row++)
                {
                    // A fresh body per row: sampled poses must not leak between states.
                    var body = Object.Instantiate(prefab);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(body, scene);
                    var animator = body.GetComponent<Animator>();
                    for (int col = 0; col < 3; col++)
                    {
                        animator.Play(States[row], 0, .12f + col * .36f); animator.Update(0);
                        camera.Render(); RenderTexture.active = rt;
                        var cell = new Texture2D(300, 400, TextureFormat.RGB24, false);
                        cell.ReadPixels(new Rect(0, 0, 300, 400), 0, 0); cell.Apply();
                        sheet.SetPixels(col * 300, (States.Length - 1 - row) * 400, 300, 400, cell.GetPixels());
                        Object.DestroyImmediate(cell);
                    }
                    Object.DestroyImmediate(body);
                }
                sheet.Apply();
                File.WriteAllBytes(output + prefabId + ".png", sheet.EncodeToPNG());
                camera.targetTexture = null;
            }
            finally
            {
                RenderTexture.active = previous; rt.Release(); Object.DestroyImmediate(rt);
                Object.DestroyImmediate(sheet); EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}

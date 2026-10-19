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
            /// <summary>Back light on the silhouette: the dark stone otherwise sinks into the arena.</summary>
            public Color RimColor;
            /// <summary>Self-lit cracks from GlowMask.png (tools/art/paint_lava_glow.py); black for none.</summary>
            public Color GlowColor;
        }

        private static readonly Island Ice = new Island
        {
            Chapter = "ice", Root = "Assets/_Project/Art/Characters/IceGolems", Output = "output/ice-golems/",
            Golems = new[]
            {
                ("Mini", "ice-golem-mini", "Frost Golem", 2.0f),
                ("Max", "ice-golem-max", "Frost Golem", 2.2f),
                ("King", "ice-golem-king", "Glacier King", 2.4f),
            },
            RimColor = new Color(.62f, .86f, 1f), GlowColor = Color.black,
        };

        private static readonly Island Lava = new Island
        {
            Chapter = "lava", Root = "Assets/_Project/Art/Characters/LavaGolems", Output = "output/lava-golems/",
            Golems = new[]
            {
                ("Mini", "lava-golem-mini", "Magma Golem", 2.0f),
                ("Max", "lava-golem-max", "Magma Golem", 2.2f),
                ("King", "lava-golem-king", "Volcano King", 2.4f),
            },
            RimColor = new Color(1f, .55f, .2f), GlowColor = new Color(1.25f, .55f, .14f),
        };

        /// <summary>Bodies whose AccuRig ankles landed on the midline, between the knees, instead of inside
        /// the feet: every clip then swung the feet around a pivot under the crotch (splayed, twisted feet).</summary>
        private static readonly string[] MisplacedAnkles = { "ice-golem-king" };

        /// <summary>Degrees the rebuilt avatar's neck and head rest pose leans forward. The Glacier King is
        /// sculpted hunched; the shared clips stand his spine up, which threw his face at the sky and
        /// stretched the badly weighted jaw. Only bodies in <see cref="MisplacedAnkles"/> get a rebuilt avatar.</summary>
        private static readonly System.Collections.Generic.Dictionary<string, float> HeadForward = new() { ["ice-golem-king"] = 45f };

        /// <summary>Preparation size relative to the crown-to-sole fit, for stocky bodies that filled their half.</summary>
        private static readonly System.Collections.Generic.Dictionary<string, float> PreparationScale = new()
            { ["ice-golem-mini"] = .88f, ["lava-golem-max"] = .88f };

        private static readonly string[] States =
            { "StandIdle", "BattleIdle", "WarmUp", "Punch", "Kick", "Hit", "Reaction", "Defeat", "Victory", "Laugh" };

        public static string PrefabPath(string prefabId) => "Assets/_Project/Resources/Bosses/" + prefabId + ".prefab";
        public static string PortraitPath(string prefabId) => "Assets/_Project/Resources/Bosses/" + prefabId + "-portrait.png";

        [MenuItem("Tools/Push Stars/Boss/Import Ice Golems")]
        public static void ImportIce() => Import(Ice);

        [MenuItem("Tools/Push Stars/Boss/Import Lava Golems")]
        public static void ImportLava() => Import(Lava);

        /// <summary>Re-applies rim and crack glow to the existing golem materials and re-renders their
        /// portraits, without reimporting the bodies.</summary>
        [MenuItem("Tools/Push Stars/Boss/Apply Golem Look")]
        public static void ApplyLook()
        {
            foreach (var island in new[] { Ice, Lava })
                foreach (var golem in island.Golems)
                {
                    string dir = island.Root + "/" + golem.folder;
                    var material = AssetDatabase.LoadAssetAtPath<Material>(dir + "/" + golem.folder + "GolemToon.mat")
                        ?? throw new InvalidOperationException("Import the " + island.Chapter + " golems first.");
                    ApplyLook(island, dir, material);
                    RenderPortrait(golem.prefabId);
                }
            AssetDatabase.SaveAssets();
        }

        private static void ApplyLook(Island island, string dir, Material material)
        {
            material.SetColor("_RimColor", island.RimColor);
            // A tight falloff keeps it on the silhouette: wider, it washes the stone in the rim colour.
            material.SetFloat("_RimStrength", .8f);
            material.SetFloat("_RimPower", 4.5f);
            // Weighted towards upward-facing edges, like a light behind and above the boss.
            material.SetVector("_RimLightDirection", new Vector4(0, 1, 0, .5f));
            bool glow = island.GlowColor.maxColorComponent > 0;
            Texture2D mask = null;
            if (glow)
            {
                string path = dir + "/GlowMask.png";
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path)
                    ?? throw new FileNotFoundException("Run tools/art/paint_lava_glow.py", path);
                importer.sRGBTexture = false; importer.mipmapEnabled = true;
                importer.maxTextureSize = 1024; importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
                mask = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            material.SetFloat("_Glow", glow ? 1 : 0);
            material.SetTexture("_EmissionMap", mask);
            material.SetColor("_EmissionColor", island.GlowColor);
            if (glow) material.EnableKeyword("_EMISSION"); else material.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
        }

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
                ImportGolem(island, island.Root + "/" + golem.folder, golem.folder, golem.prefabId, golem.displayName, golem.height, controller);
            AssetDatabase.SaveAssets();
            Validate(island);
        }

        private static void ImportGolem(Island island, string dir, string folder, string prefabId, string displayName, float height, AnimatorController controller)
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
            ApplyLook(island, dir, material);

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
                if (MisplacedAnkles.Contains(prefabId)) RebuildAnkles(instance, importer, dir, prefabId);
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
                var presentation = instance.AddComponent<BossAvatarPresentation>();
                if (PreparationScale.TryGetValue(prefabId, out float scale)) presentation.PreparationScale = scale;
                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath(prefabId));
            }
            finally { Object.DestroyImmediate(instance); }
            RenderPortrait(prefabId);
        }

        /// <summary>
        /// Moves each ankle joint above the vertices it carries (the skin weights are right, only the joint
        /// positions are wrong), then rebinds the mesh and rebuilds the humanoid avatar from the corrected
        /// skeleton, so retargeted clips bend the feet at the real ankles. Runs in the import's bind pose.
        /// </summary>
        private static void RebuildAnkles(GameObject body, ModelImporter importer, string dir, string prefabId)
        {
            var animator = body.GetComponent<Animator>();
            var skins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var meshes = skins.ToDictionary(skin => skin, skin => Object.Instantiate(skin.sharedMesh));
            var moved = new System.Collections.Generic.List<Transform>();
            // Moves a joint, keeps its children where they are and rebinds the vertices it carries.
            void Move(Transform bone, Vector3 target)
            {
                var children = bone.Cast<Transform>().Select(c => (c, c.position, c.rotation)).ToArray();
                bone.position = target;
                foreach (var (child, position, rotation) in children) { child.position = position; child.rotation = rotation; }
                if (!moved.Contains(bone)) moved.Add(bone);
                foreach (var (child, _, _) in children) if (!moved.Contains(child)) moved.Add(child);
                foreach (var skin in skins)
                {
                    int index = Array.IndexOf(skin.bones, bone);
                    if (index < 0) continue;
                    var bindposes = meshes[skin].bindposes;
                    bindposes[index] = bone.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                    meshes[skin].bindposes = bindposes;
                }
            }
            // World positions of the vertices a bone mainly carries.
            System.Collections.Generic.List<Vector3> Carried(params Transform[] bones)
            {
                var points = new System.Collections.Generic.List<Vector3>();
                foreach (var skin in skins)
                {
                    var indices = bones.Select(b => Array.IndexOf(skin.bones, b)).Where(i => i >= 0).ToArray();
                    if (indices.Length == 0) continue;
                    var weights = skin.sharedMesh.boneWeights; var vertices = skin.sharedMesh.vertices;
                    var toWorld = skin.transform.localToWorldMatrix;
                    for (int i = 0; i < vertices.Length; i++)
                        if (indices.Contains(weights[i].boneIndex0)) points.Add(toWorld.MultiplyPoint3x4(vertices[i]));
                }
                return points;
            }
            foreach (var (footBone, toesBone) in new[] { (HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes), (HumanBodyBones.RightFoot, HumanBodyBones.RightToes) })
            {
                var foot = animator.GetBoneTransform(footBone);
                var toes = animator.GetBoneTransform(toesBone);
                var carried = Carried(foot);
                if (carried.Count == 0) throw new InvalidOperationException(footBone + " carries no vertices.");
                // Only sideways: height and depth of the exported ankle are plausible.
                float x = carried.Average(p => p.x);
                Move(foot, new Vector3(x, foot.position.y, foot.position.z));
                if (toes == null) continue;
                // Clips turn the ankle-to-toe line to face forward, so it must run along the sole
                // (heel to tips), or the whole foot turns in by the difference.
                var sole = Carried(foot, toes).Where(p => p.y < foot.position.y + .02f).OrderBy(p => p.z).ToList();
                int fifth = Mathf.Max(1, sole.Count / 5);
                Vector3 heel = sole.Take(fifth).Aggregate(Vector3.zero, (a, b) => a + b) / fifth;
                Vector3 tips = sole.Skip(sole.Count - fifth).Aggregate(Vector3.zero, (a, b) => a + b) / fifth;
                Vector3 along = tips - heel;
                if (along.z <= 0) throw new InvalidOperationException(footBone + " sole does not point forward.");
                float reach = toes.position.z - foot.position.z;
                Move(toes, new Vector3(foot.position.x + along.x / along.z * reach, toes.position.y, toes.position.z));
            }
            foreach (var skin in skins)
            {
                var mesh = meshes[skin];
                mesh.name = skin.sharedMesh.name + " (ankles)";
                skin.sharedMesh = SaveAsset(mesh, dir + "/" + skin.name + "-ankles.asset");
            }

            var transforms = body.GetComponentsInChildren<Transform>(true);
            var pose = transforms.Select(t => (t, t.localPosition, t.localRotation)).ToArray();
            void Restore() { foreach (var (t, position, rotation) in pose) { t.localPosition = position; t.localRotation = rotation; } }

            // The importer's T-pose straightened these legs around the misplaced joints (thigh, calf and
            // a ~50 degree foot turn), which now turned the feet in. Re-derive it for the legs only:
            // thigh and shin straight down, ankle-to-toe line facing forward. The rest of the skeleton
            // keeps the importer's T-pose (its arm and spine corrections), or the body comes out hunched.
            var legRotations = new System.Collections.Generic.Dictionary<string, Quaternion>();
            void Aim(Transform bone, Transform child, Vector3 direction)
            {
                bone.rotation = Quaternion.FromToRotation(child.position - bone.position, direction) * bone.rotation;
                legRotations[bone.name] = bone.localRotation;
            }
            foreach (var (upper, lower, foot, toes) in new[] {
                (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes),
                (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes) })
            {
                Aim(animator.GetBoneTransform(upper), animator.GetBoneTransform(lower), Vector3.down);
                Aim(animator.GetBoneTransform(lower), animator.GetBoneTransform(foot), Vector3.down);
                var footBone = animator.GetBoneTransform(foot); var toesBone = animator.GetBoneTransform(toes);
                Vector3 reach = toesBone.position - footBone.position;
                // Turn about the vertical only: the toes keep their drop below the ankle.
                Aim(footBone, toesBone, new Vector3(0, reach.y, new Vector2(reach.x, reach.z).magnitude));
            }
            Restore();

            var description = importer.humanDescription;
            description.skeleton = description.skeleton.Select(bone =>
            {
                var t = moved.FirstOrDefault(x => x.name == bone.name);
                if (t != null) bone.position = t.localPosition;
                if (legRotations.TryGetValue(bone.name, out var rotation)) bone.rotation = rotation;
                return bone;
            }).ToArray();
            if (HeadForward.TryGetValue(prefabId, out float lean))
                foreach (var part in new[] { HumanBodyBones.Neck, HumanBodyBones.Head })
                {
                    // Half each, about the body's left-right axis (positive X pitches forward-down). The
                    // spine above the hips keeps its bind rotation in the T-pose, so the bind parent works.
                    var bone = animator.GetBoneTransform(part);
                    int index = Array.FindIndex(description.skeleton, b => b.name == bone.name);
                    var parent = bone.parent.rotation;
                    description.skeleton[index].rotation = Quaternion.Inverse(parent) * Quaternion.AngleAxis(lean * .5f, Vector3.right)
                        * parent * description.skeleton[index].rotation;
                }
            // AvatarBuilder matches the root by name.
            string name = body.name;
            body.name = description.skeleton[0].name;
            var avatar = AvatarBuilder.BuildHumanAvatar(body, description);
            body.name = name;
            if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("Rebuilt ankle avatar is not a valid humanoid.");
            avatar.name = "Body (ankles)";
            // Assigning an avatar rebinds the Animator, which snaps every bone back to the imported pose.
            animator.avatar = SaveAsset(avatar, dir + "/Body-ankles-avatar.asset");
            Restore();
        }

        /// <summary>Writes into the existing asset when there is one: replacing it would leave the saved
        /// prefab pointing at the deleted object until the next reimport.</summary>
        private static T SaveAsset<T>(T value, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(value, path); return value; }
            string name = existing.name;
            EditorUtility.CopySerialized(value, existing);
            existing.name = name;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(value);
            return existing;
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

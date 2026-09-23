using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class AvatarCollectionSetup
    {
        private const string Art = "Assets/_Project/Resources/AvatarCollection";

        [MenuItem("Tools/Push Stars/Character/Prepare Sonic and Avatar Collection")]
        public static void Run()
        {
            PrepareCharacter("Sonic");
            RenderMainPortraits();
            ImportCardArt();
            Debug.Log("[AvatarCollection] Setup complete. Sonic is prepared but is not equipped on the home stage.");
        }

        public static void PrepareCharacter(string characterName)
        {
            string Dir = "Assets/Character/" + characterName;
            AssetDatabase.Refresh();
            var happy = Configure(Dir + "/Happy Idle.fbx", "Idle", true, true);
            var accent = Configure(Dir + "/Offensive Idle.fbx", "OffensiveIntro", false, false);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Dir + "/Happy Idle.fbx");
            Directory.CreateDirectory(Dir + "/Textures");
            importer.ExtractTextures(Dir + "/Textures");
            AssetDatabase.Refresh();
            importer.SaveAndReimport();
            happy = LoadClip(Dir + "/Happy Idle.fbx");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Happy Idle.fbx");
            var instance = Object.Instantiate(model);
            try
            {
                instance.name = characterName;
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                    throw new InvalidOperationException(characterName + " needs a valid humanoid avatar.");
                if (instance.GetComponentsInChildren<SkinnedMeshRenderer>().Length == 0)
                    throw new InvalidOperationException("Happy Idle does not contain a character mesh.");
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var source = materials[i];
                        string path = Dir + "/" + characterName + "-" + renderer.name.Replace(':', '_') + "-" + i + ".mat";
                        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (material == null)
                        {
                            material = new Material(MainCharacterSetup.CharacterShader());
                            AssetDatabase.CreateAsset(material, path);
                        }
                        var texture = source != null ? source.mainTexture : null;
                        if (texture == null)
                            texture = AssetDatabase.FindAssets("t:Texture2D", new[] { Dir + "/Textures" })
                                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Texture2D>).FirstOrDefault();
                        MainCharacterSetup.ApplyCharacterSurface(material, texture);
                        materials[i] = material;
                    }
                    renderer.sharedMaterials = materials;
                }
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Dir + "/" + characterName + ".controller")
                    ?? AnimatorController.CreateAnimatorControllerAtPath(Dir + "/" + characterName + ".controller");
                var machine = controller.layers[0].stateMachine;
                var idle = State(machine, "Idle", happy);
                State(machine, "OffensiveIntro", accent);
                machine.defaultState = idle;
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                happy.SampleAnimation(instance, 0);
                var bounds = BoundsOf(instance);
                instance.transform.localScale *= 1.8f / Mathf.Max(bounds.size.y, .01f);
                if (instance.GetComponent<SonicIdleBehaviour>() == null) instance.AddComponent<SonicIdleBehaviour>();
                PrefabUtility.SaveAsPrefabAsset(instance, Dir + "/" + characterName + ".prefab");
                RenderPortrait(instance, happy, Art + "/" + characterName + ".png");
                Debug.Log($"[AvatarCollection] {characterName} humanoid valid; Happy Idle={happy.length:F3}s; first-third accent={accent.length:F3}s.");
            }
            finally { Object.DestroyImmediate(instance); }
        }

        private static void RenderMainPortraits()
        {
            foreach (var def in MainCharacterSetup.Characters)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(def.PrefabPath);
                if (prefab == null) throw new InvalidOperationException("Missing " + def.PrefabPath);
                var body = Object.Instantiate(prefab);
                try
                {
                    var animator = body.GetComponentInChildren<Animator>();
                    var controller = animator.runtimeAnimatorController as AnimatorController;
                    var idle = controller.layers[0].stateMachine.states.First(s => s.state.name == "Idle").state.motion as AnimationClip;
                    RenderPortrait(body, idle, Art + "/" + def.Name + ".png");
                }
                finally { Object.DestroyImmediate(body); }
            }
        }

        public static void ImportCardArt()
        {
            AssetDatabase.Refresh();
            foreach (string path in Directory.GetFiles(Art, "*.png"))
            {
                var texture = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                texture.textureType = TextureImporterType.Sprite;
                texture.spriteImportMode = SpriteImportMode.Single;
                texture.alphaIsTransparency = true;
                texture.mipmapEnabled = false;
                texture.maxTextureSize = 1024;
                texture.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
        }

        private static AnimationClip Configure(string path, string name, bool loop, bool mesh)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importCameras = importer.importLights = false;
            importer.materialImportMode = mesh ? ModelImporterMaterialImportMode.ImportStandard : ModelImporterMaterialImportMode.None;
            importer.clipAnimations = Array.Empty<ModelImporterClipAnimation>();
            importer.SaveAndReimport();
            var clip = importer.defaultClipAnimations.First();
            clip.name = name;
            if (!loop) clip.lastFrame = clip.firstFrame + (clip.lastFrame - clip.firstFrame) / 3f;
            clip.loopTime = clip.loopPose = loop;
            clip.lockRootRotation = clip.keepOriginalOrientation = true;
            clip.lockRootHeightY = clip.keepOriginalPositionY = true;
            clip.lockRootPositionXZ = clip.keepOriginalPositionXZ = true;
            importer.clipAnimations = new[] { clip };
            importer.SaveAndReimport();
            return LoadClip(path) ?? throw new InvalidOperationException("Missing animation: " + path);
        }

        private static AnimationClip LoadClip(string path) => AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));

        private static AnimatorState State(AnimatorStateMachine machine, string name, AnimationClip clip)
        {
            var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name) ?? machine.AddState(name);
            state.motion = clip;
            return state;
        }

        private static Bounds BoundsOf(GameObject body)
        {
            var renderers = body.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static void RenderPortrait(GameObject body, AnimationClip clip, string path)
        {
            var origin = new Vector3(1000, 0, 0);
            body.transform.position = origin;
            body.transform.rotation = Quaternion.Euler(0, 180, 0);
            clip.SampleAnimation(body, clip.length * .1f);
            // Portraits must remain legible on small cards; do not alter shared stage materials.
            foreach (var renderer in body.GetComponentsInChildren<Renderer>())
            {
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                properties.SetFloat("_ShadeStrength", .22f);
                properties.SetFloat("_LightInfluence", 0f);
                renderer.SetPropertyBlock(properties);
            }
            var bounds = BoundsOf(body);
            var cameraObject = new GameObject("AvatarPortraitCamera");
            var lightObject = new GameObject("AvatarPortraitLight");
            var rt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var shot = new Texture2D(512, 512, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.25f;
                light.transform.rotation = Quaternion.Euler(25, -25, 0);
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.orthographic = true;
                camera.orthographicSize = bounds.size.y * .28f;
                camera.nearClipPlane = .01f;
                camera.farClipPlane = 20;
                camera.transform.position = new Vector3(bounds.center.x, bounds.max.y - bounds.size.y * .26f, origin.z - 5);
                camera.transform.rotation = Quaternion.identity;
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                shot.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                shot.Apply();
                File.WriteAllBytes(path, shot.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(lightObject);
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(shot);
            }
        }

        public static void FinishAndValidate()
        {
            Run();
            AvatarCollectionSceneSetup.RepairLightning();
            AvatarCollectionValidation.Run();
        }
    }
}

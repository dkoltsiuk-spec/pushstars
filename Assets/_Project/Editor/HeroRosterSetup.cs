using System;
using System.IO;
using System.Linq;
using PushStars.Core;
using PushStars.UI;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>
    /// Imports the catalog heroes that bring their own prefab (<see cref="AvatarOffer.Prefab"/>).
    /// Each one is a rigged FBX at <c>Assets/Character/&lt;Name&gt;/&lt;name&gt;.fbx</c> with its diffuse
    /// under <c>Textures/</c>; the same import as the main bodies turns it into a humanoid prefab
    /// under Resources, which is where the collection, the shop and the fight stage load it from.
    /// Nothing in a scene references these heroes, so adding one is a catalog row plus this import.
    /// </summary>
    public static class HeroRosterSetup
    {
        private const string HeadArt = "Assets/_Project/Resources/AvatarCollection";

        /// <summary>Bodies whose hanging arms touch or sink into them on the shared clips, and how far theirs open
        /// (<see cref="StandingArmSpread"/>). A value tuned on the prefab afterwards wins over this.</summary>
        private static readonly (string hero, float degrees)[] ArmSpread =
            { ("Chubby", 12f), ("Viking", 11f), ("Skinny", 8f), ("Tigress", 8f) };

        [MenuItem("Tools/Push Stars/Character/Import Catalog Heroes")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before importing heroes.");
            AssetDatabase.Refresh();
            var report = new System.Text.StringBuilder();
            foreach (var offer in AvatarCatalog.Defaults().Where(o => !string.IsNullOrEmpty(o.Prefab)))
                report.AppendLine(Import(offer));
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/hero-roster-import.txt", report.ToString());
            Debug.Log("[HeroRoster] Import finished.\n" + report);
        }

        private static MainCharacterSetup.CharacterDef Definition(AvatarOffer offer)
        {
            string name = Path.GetFileName(offer.Prefab);
            string dir = "Assets/Character/" + name;
            return new MainCharacterSetup.CharacterDef
            {
                Name = name, Gender = CharacterGender.Male, Dir = dir,
                BodyFbx = $"{dir}/{name.ToLowerInvariant()}.fbx",
                PrefabPath = $"Assets/_Project/Resources/{offer.Prefab}.prefab",
                ControllerPath = $"{dir}/{name}.controller",
            };
        }

        private static string Import(AvatarOffer offer)
        {
            var def = Definition(offer);
            if (AssetImporter.GetAtPath(def.BodyFbx) as ModelImporter == null) return $"{def.Name}: FBX missing at {def.BodyFbx}";
            Directory.CreateDirectory(Path.GetDirectoryName(def.PrefabPath));
            AssetDatabase.Refresh();
            // The import rebuilds the prefab from the FBX, so what was tuned on it is carried over.
            var tuned = AssetDatabase.LoadAssetAtPath<GameObject>(def.PrefabPath)?.GetComponent<StandingArmSpread>();
            float? degrees = tuned != null ? tuned.Degrees : null, forearm = tuned != null ? tuned.ForearmReturn : null;
            if (!MainCharacterSetup.Import(def)) return $"{def.Name}: import failed";
            ApplyArmSpread(def.Name, degrees, forearm);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(def.PrefabPath);
            var animator = prefab != null ? prefab.GetComponentInChildren<Animator>() : null;
            bool valid = animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
            return $"{def.Name}: {(valid ? "humanoid ok" : "INVALID HUMANOID")}, scale ×{(prefab != null ? prefab.transform.localScale.y : 0):F3}";
        }

        /// <summary>Adds the arm spread to a hero's prefab without re-importing the model.</summary>
        public static void ApplyArmSpread(string hero, float? degrees = null, float? forearmReturn = null)
        {
            int row = Array.FindIndex(ArmSpread, a => a.hero == hero);
            if (row < 0 && degrees == null) return;
            string path = $"Assets/_Project/Resources/Heroes/{hero}.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var spread = root.GetComponent<StandingArmSpread>();
                if (spread == null) spread = root.AddComponent<StandingArmSpread>();
                spread.Degrees = degrees ?? ArmSpread[row].degrees;
                if (forearmReturn != null) spread.ForearmReturn = forearmReturn.Value;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>Renders a transparent close-up of each Aura hero's head. The sticker outline
        /// the unlock bar's icons carry is added outside Unity, and the result lands in
        /// Resources/AvatarCollection/&lt;Name&gt;Head.png.</summary>
        public static void RenderHeads(string outputDir)
        {
            foreach (var offer in AvatarCatalog.Defaults().Where(o => !string.IsNullOrEmpty(o.Prefab) && !string.IsNullOrEmpty(o.HeadIcon)))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Definition(offer).PrefabPath);
                if (prefab == null) continue;
                var body = Object.Instantiate(prefab, new Vector3(3000, 0, 0), Quaternion.Euler(0, 180, 0));
                var cameraObject = new GameObject("HeroHeadCamera");
                var lightObject = new GameObject("HeroHeadLight");
                var target = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                var shot = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
                var previous = RenderTexture.active;
                try
                {
                    var animator = body.GetComponentInChildren<Animator>();
                    var idle = animator.runtimeAnimatorController.animationClips.First(c => c.name == MainCharacterSetup.IdleState);
                    idle.SampleAnimation(body, 0f);
                    foreach (var renderer in body.GetComponentsInChildren<Renderer>())
                    {
                        var properties = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(properties);
                        properties.SetFloat("_ShadeStrength", .22f);
                        properties.SetFloat("_LightInfluence", 0f);
                        renderer.SetPropertyBlock(properties);
                    }
                    var renderers = body.GetComponentsInChildren<Renderer>();
                    var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    float neck = animator.GetBoneTransform(HumanBodyBones.Head).position.y;
                    float top = bounds.max.y;
                    var light = lightObject.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.intensity = 1.25f;
                    light.transform.rotation = Quaternion.Euler(25, -25, 0);
                    var camera = cameraObject.AddComponent<Camera>();
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.clear;
                    camera.orthographic = true;
                    camera.orthographicSize = (top - neck) * .78f;
                    camera.nearClipPlane = .01f;
                    camera.farClipPlane = 20;
                    camera.transform.position = new Vector3(animator.GetBoneTransform(HumanBodyBones.Head).position.x, (top + neck) * .5f, body.transform.position.z - 5);
                    camera.transform.rotation = Quaternion.identity;
                    camera.targetTexture = target;
                    camera.Render();
                    RenderTexture.active = target;
                    shot.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
                    shot.Apply();
                    Directory.CreateDirectory(outputDir);
                    File.WriteAllBytes(Path.Combine(outputDir, Path.GetFileName(offer.HeadIcon) + ".png"), shot.EncodeToPNG());
                }
                finally
                {
                    RenderTexture.active = previous;
                    Object.DestroyImmediate(cameraObject);
                    Object.DestroyImmediate(lightObject);
                    Object.DestroyImmediate(body);
                    target.Release();
                    Object.DestroyImmediate(target);
                    Object.DestroyImmediate(shot);
                }
            }
        }

        /// <summary>Sprite import for the head icons, matching the existing ones.</summary>
        public static void ImportHeads()
        {
            AssetDatabase.Refresh();
            foreach (var offer in AvatarCatalog.Defaults().Where(o => !string.IsNullOrEmpty(o.Prefab) && !string.IsNullOrEmpty(o.HeadIcon)))
            {
                if (AssetImporter.GetAtPath($"{HeadArt}/{Path.GetFileName(offer.HeadIcon)}.png") is not TextureImporter texture) continue;
                texture.textureType = TextureImporterType.Sprite;
                texture.spriteImportMode = SpriteImportMode.Single;
                texture.alphaIsTransparency = true;
                texture.mipmapEnabled = false;
                texture.maxTextureSize = 512;
                texture.SaveAndReimport();
            }
        }
    }
}

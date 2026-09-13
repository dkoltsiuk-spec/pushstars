using System;
using System.Linq;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    public static class ProfileIdentitySetup
    {
        [MenuItem("Push Stars/UI/Add Profile Identity Editor")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            var scene = SceneManager.GetActiveScene();
            var dash = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ProfileDashboard>(true)).First();
            if (dash.GetComponent<ProfileIdentityEditor>() != null) return;
            AssetDatabase.Refresh();
            string[] files = { "hood", "fighter", "athlete", "robot" };
            var textures = new Texture2D[4];
            for (int i = 0; i < files.Length; i++)
            {
                string path = "Assets/_Project/UI/Portraits/ProfileAvatars/" + files[i] + ".png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 512;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                textures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            var art = dash.transform.Find("ProfileScroll/Viewport/Content/Art");
            var editor = Undo.AddComponent<ProfileIdentityEditor>(dash.gameObject);
            editor.AvatarTextures = textures;
            editor.AvatarCrops = Enumerable.Repeat(new Rect(0, 0, 1, 1), 4).ToArray();
            editor.AvatarNames = new[] { "SHADOW", "STRIKER", "BLAZE", "BOLT" };
            editor.NameLabel = dash.PlayerName;
            editor.Font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/Rubik Bold TMP.asset");
            editor.Circle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/circle_128.png");
            editor.Rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/pill_12.png");
            editor.YellowPlate = Sprite("Group 556");
            editor.DarkPlate = Sprite("Group 557");
            editor.ModalParent = (RectTransform)dash.transform.parent.parent;
            var old = art.Find("Avatar").GetComponent<Image>();
            Undo.RecordObject(old, "Replace avatar with circular portrait"); old.enabled = false;
            var ring = Rect(art, "AvatarRing", 17, 19, 80, 80);
            var ringImage = ring.gameObject.AddComponent<Image>(); ringImage.sprite = editor.Circle;
            ringImage.color = new Color32(24, 34, 55, 255); ringImage.raycastTarget = false;
            var face = Rect(ring, "AvatarMask", 3, 3, 74, 74);
            var disk = face.gameObject.AddComponent<Image>(); disk.sprite = editor.Circle; disk.raycastTarget = false;
            face.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var portrait = Rect(face, "Portrait", 0, 0, 74, 74).gameObject.AddComponent<RawImage>();
            portrait.texture = textures[0]; portrait.raycastTarget = false; editor.Avatar = portrait;
            editor.EditAvatar = Gear(art, "EditAvatar", 80, 19);
            editor.EditName = Gear(art, "EditName", 309, 24);
            var name = dash.PlayerName;
            Undo.RecordObject(name, "Bold profile name");
            Undo.RecordObject(name.rectTransform, "Name layout");
            name.rectTransform.anchoredPosition = new Vector2(108, -29);
            name.rectTransform.sizeDelta = new Vector2(196, 32);
            var black = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/Rubik Black TMP.asset");
            name.font = black; name.fontSize = 24; name.fontSizeMax = 24; name.fontSizeMin = 12;
            name.enableAutoSizing = true; name.enableWordWrapping = false; name.richText = false;
            name.color = new Color32(216, 227, 255, 255);
            const string materialPath = "Assets/_Project/UI/Sprites/ProfileSettings/ProfileNameOutline.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(black.material) { name = "ProfileNameOutline" };
                material.SetColor("_OutlineColor", new Color32(7, 10, 19, 255));
                material.SetFloat("_OutlineWidth", .20f);
                material.SetFloat("_FaceDilate", .06f);
                material.DisableKeyword("UNDERLAY_ON"); material.DisableKeyword("UNDERLAY_INNER");
                ShaderUtilities.UpdateShaderRatios(material);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            name.fontSharedMaterial = material;
            var shadow = UnityEngine.Object.Instantiate(name.gameObject, art);
            Undo.RegisterCreatedObjectUndo(shadow, "Create dark name layer");
            shadow.name = "NameDarkLayer";
            shadow.transform.SetSiblingIndex(name.transform.GetSiblingIndex());
            editor.NameShadow = shadow.GetComponent<TextMeshProUGUI>();
            editor.NameShadow.color = new Color32(7, 10, 19, 255);
            editor.NameShadow.rectTransform.anchoredPosition += new Vector2(0, -2.5f);
            editor.NameShadow.raycastTarget = false;
            EditorUtility.SetDirty(editor);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = dash.gameObject;
        }

        private static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/ProfileSettings/" + name + ".png");
        private static Button Gear(Transform parent, string name, float x, float y)
        {
            // A larger transparent hit area keeps the small edit gear usable on a phone.
            var hit = Rect(parent, name, x - 8, y - 8, 38, 38);
            var image = hit.gameObject.AddComponent<Image>(); image.color = Color.clear;
            var button = hit.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var icon = Rect(hit, "Icon", 8, 8, 22, 22).gameObject.AddComponent<Image>();
            icon.sprite = Sprite("Group 553"); icon.preserveAspect = true; icon.raycastTarget = false;
            return button;
        }
        private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform)); Undo.RegisterCreatedObjectUndo(go, "Create profile editing UI");
            go.layer = parent.gameObject.layer; go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h); return rt;
        }
    }
}

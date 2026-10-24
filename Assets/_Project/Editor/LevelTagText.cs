using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Editor
{
    /// <summary>"LVL n" on the tag badge (profile, fight result): white with a black keyline, so it
    /// reads on the light tag and against any background, like the game's other numbers.</summary>
    public static class LevelTagText
    {
        private const string Material = "Assets/_Project/UI/Sprites/ProfileBrawl/LevelValue.mat";
        private const string TagSprite = "lvl-icon";
        private static readonly string[] Scenes = { "Assets/_Project/Scenes/Main.unity", "Assets/_Project/Scenes/RewardSummary.unity" };

        public static void Style(TMP_Text text)
        {
            text.color = Color.white;
            var material = AssetDatabase.LoadAssetAtPath<Material>(Material);
            if (material != null) text.fontSharedMaterial = material;
            EditorUtility.SetDirty(text);
        }

        [MenuItem("Tools/Push Stars/UI/Restyle Level Tags")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (var path in Scenes)
            {
                var scene = EditorSceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    foreach (var tag in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Image>(true))
                                 .Where(i => i.sprite != null && i.sprite.name == TagSprite))
                        foreach (var text in tag.GetComponentsInChildren<TMP_Text>(true))
                            if (text.gameObject.activeSelf) Style(text);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
        }
    }
}

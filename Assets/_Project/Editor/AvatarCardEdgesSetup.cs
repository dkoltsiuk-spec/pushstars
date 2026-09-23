using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PushStars.Editor
{
    public static class AvatarCardEdgesSetup
    {
        [MenuItem("Tools/Push Stars/UI/Fix Avatar Card Edges")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scenes before fixing card edges.");
            const string path = "Assets/_Project/Scenes/Main.unity";
            var scene = EditorSceneManager.OpenScene(path);
            Directory.CreateDirectory("Library/AvatarCollectionBackup");
            File.Copy(path, "Library/AvatarCollectionBackup/Main-before-card-edges-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
            var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AvatarCollectionScreen>(true)).First();
            for (int i = 0; i < screen.Cards.Length; i++)
            {
                var card = (RectTransform)screen.Cards[i].transform;
                // Retain the original Image for button hit testing and existing serialized references.
                screen.Plates[i].color = Color.clear;
                screen.Footers[i].enabled = false;
                var surface = Surface(card, "CleanCard", false);
                surface.transform.SetAsFirstSibling();
                var footer = Surface(card, "CleanFooter", true);
                footer.transform.SetSiblingIndex(screen.Actions[i].transform.GetSiblingIndex());
                var label = screen.Actions[i].rectTransform;
                label.anchorMin = label.anchorMax = label.pivot = new Vector2(0, 1);
                label.anchoredPosition = new Vector2(2, -163);
                label.sizeDelta = new Vector2(162, 39);
                surface.Selected = footer.Selected = screen.Actions[i].text == "SELECTED";
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[AvatarCollection] Removed baked white edge and aligned footer to the shared inner card bounds.");
        }

        private static AvatarCardSurface Surface(RectTransform parent, string name, bool footer)
        {
            var child = parent.Find(name);
            var rect = child != null ? (RectTransform)child : UiBuilder.Rect(parent, name);
            UiBuilder.Stretch(rect);
            var surface = rect.GetComponent<AvatarCardSurface>() ?? rect.gameObject.AddComponent<AvatarCardSurface>();
            surface.Footer = footer;
            surface.raycastTarget = false;
            return surface;
        }

        public static void RunAndValidate() { Run(); AvatarCollectionValidation.Run(); }
    }
}

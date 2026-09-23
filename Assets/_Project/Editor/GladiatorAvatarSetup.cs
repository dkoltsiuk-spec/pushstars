using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Editor
{
    public static class GladiatorAvatarSetup
    {
        public const string PrefabPath = "Assets/Character/Gladiator/Gladiator.prefab";

        [MenuItem("Tools/Push Stars/Character/Prepare and Equip Gladiator")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode first.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/_Project/Scenes/Main.unity" || scene.isDirty)
                throw new InvalidOperationException("Open the saved Main scene first.");
            Directory.CreateDirectory("Library/AvatarCollectionBackup");
            File.Copy(scene.path, "Library/AvatarCollectionBackup/Main-before-gladiator-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
            var def = new MainCharacterSetup.CharacterDef {
                Name = "Gladiator", Gender = CharacterGender.Male,
                Dir = "Assets/Character/Gladiator",
                BodyFbx = "Assets/Character/Gladiator/gladiator.fbx",
                PrefabPath = PrefabPath,
                ControllerPath = "Assets/Character/Gladiator/Gladiator.controller"
            };
            if (!MainCharacterSetup.Import(def)) throw new InvalidOperationException("Gladiator import failed.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var animator = prefab.GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("Gladiator humanoid rig is invalid.");
            var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AvatarCollectionScreen>(true)).First();
            var roster = screen.Roster;
            var settings = new SerializedObject(roster);
            settings.FindProperty("_gladiatorPrefab").objectReferenceValue = prefab;
            settings.FindProperty("_defaultHomeAvatar").intValue = 2;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var stage = roster.GetComponent<CharacterStage>();
            for (int i = stage.AvatarRoot.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(stage.AvatarRoot.GetChild(i).gameObject);
            stage.SetAvatar((GameObject)PrefabUtility.InstantiatePrefab(prefab));
            PlayerPrefs.SetInt(CharacterRoster.HomeAvatarKey, 2);
            PlayerPrefs.Save();
            if (screen.Cards.Length == 4)
            {
                var source = screen.Cards[2];
                var card = UnityEngine.Object.Instantiate(source, source.transform.parent);
                card.name = "Card4";
                card.transform.Find("Name").GetComponent<TextMeshProUGUI>().text = "GLADIATOR";
                var preview = card.GetComponentInChildren<AvatarCardPreview>(true);
                preview.Prefab = prefab;
                preview.Slot = 4;
                preview.Image.texture = null;
                screen.Cards = screen.Cards.Concat(new[] { card }).ToArray();
                screen.InfoButtons = screen.InfoButtons.Concat(new[] { card.transform.Find("Info").GetComponent<Button>() }).ToArray();
                screen.Plates = screen.Plates.Concat(new[] { card.image }).ToArray();
                screen.Footers = screen.Footers.Concat(new[] { card.transform.Find("Footer").GetComponent<Image>() }).ToArray();
                screen.Actions = screen.Actions.Concat(new[] { card.transform.Find("Action").GetComponent<TextMeshProUGUI>() }).ToArray();
                screen.PreviewPage.Prefabs = screen.PreviewPage.Prefabs.Concat(new[] { prefab }).ToArray();
            }
            screen.Filter(0);
            screen.Fit();
            EditorUtility.SetDirty(roster);
            EditorUtility.SetDirty(screen);
            EditorUtility.SetDirty(screen.PreviewPage);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Gladiator] Imported, animated, added to collection and equipped on Home.");
        }
    }
}

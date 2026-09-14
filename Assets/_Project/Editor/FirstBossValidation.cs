using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PushStars.Core;
using PushStars.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class FirstBossValidation
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [MenuItem("Tools/Push Stars/Boss/Validate Scene Integration")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run outside Play mode.");
            var fields = typeof(FightRequest).GetFields(BindingFlags.Static | BindingFlags.NonPublic);
            var saved = fields.Select(f => f.GetValue(null)).ToArray();
            bool hadProgress = PlayerPrefs.HasKey("boss_progress");
            int progress = PlayerPrefs.GetInt("boss_progress");
            Directory.CreateDirectory("output/first-boss");
            try
            {
                PlayerPrefs.SetInt("boss_progress", 0);
                FightRequest.Boss();
                PlayerPrefs.SetInt("boss_progress", 1);
                Require(FightRequest.BossId == "novice", "Selected boss survives ladder advancement");
                foreach (var name in new[] { "FightPreparation", "Fight", "FightResults" }) CheckScene(name, true);
                FightRequest.Ghost();
                CheckScene("FightPreparation", false);
                File.WriteAllText("output/first-boss/integration.txt", "PASS: first orc on opponent stage in preparation, battle and results; original player preserved; hero lighting and toon shader; ten animator states; victory then laugh then idle; ghost driver not bound to boss; selected boss survives ladder advancement; PVP unchanged.\n");
            }
            catch (Exception e)
            { File.WriteAllText("output/first-boss/integration.txt", "FAIL: " + e); throw; }
            finally
            {
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, saved[i]);
                if (hadProgress) PlayerPrefs.SetInt("boss_progress", progress); else PlayerPrefs.DeleteKey("boss_progress");
            }
        }

        private static void CheckScene(string name, bool boss)
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/" + name + ".unity");
            var previous = RenderTexture.active;
            try
            {
                var avatars = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FightAvatar>(true)).ToArray();
                Require(avatars.Length == 2, "Two stages in " + name);
                foreach (var avatar in avatars)
                {
                    typeof(FightAvatar).GetMethod("Build", Private).Invoke(avatar, null);
                    bool opponent = new SerializedObject(avatar).FindProperty("_opponentStage").boolValue;
                    Require(avatar.Character != null, "Character spawned in " + name);
                    var presentation = avatar.Character.GetComponent<BossAvatarPresentation>();
                    Require((presentation != null) == (opponent && boss), "Boss only replaces opponent in boss mode");
                    if (presentation == null) continue;
                    var animator = avatar.Character.GetComponent<Animator>();
                    Require(animator.avatar.isValid && animator.isHuman, "Valid boss humanoid");
                    foreach (string state in new[] { "StandIdle", "BattleIdle", "WarmUp", "Punch", "Kick", "Hit", "Reaction", "Defeat", "Victory", "Laugh" })
                        Require(animator.HasState(0, Animator.StringToHash(state)), "Bound animation " + state);
                    ValidateVictorySequence(presentation, animator);
                    var ghost = new SerializedObject(avatar).FindProperty("_driverBehaviour").objectReferenceValue as Behaviour;
                    Require(ghost == null || !ghost.enabled, "Ghost cannot overwrite boss animation");
                    foreach (var renderer in avatar.Character.GetComponentsInChildren<Renderer>())
                        foreach (var material in renderer.sharedMaterials)
                            Require(material.shader.name == MainCharacterSetup.ToonShaderName && material.GetTexture("_BaseMap") != null && material.GetColor("_BaseColor") == Color.white,
                                "Textured toon boss is not a shadow");
                    var lights = avatar.StageCamera.transform.parent.GetComponentsInChildren<Light>();
                    var player = avatars.Single(a => a != avatar);
                    var playerLights = player.StageCamera.transform.parent.GetComponentsInChildren<Light>();
                    Require(lights.Length == playerLights.Length && lights.All(l => playerLights.Any(p => p.name == l.name && p.color == l.color && Mathf.Approximately(p.intensity, l.intensity))), "Same lights as player");
                    var camera = avatar.StageCamera; camera.scene = scene;
                    var rt = new RenderTexture(720, 1024, 24); camera.targetTexture = rt;
                    string pose = name == "Fight" ? "Kick" : name == "FightResults" ? "Defeat" : "StandIdle";
                    animator.Play(pose, 0, name == "FightResults" ? .98f : .45f); animator.Update(0);
                    typeof(FightAvatar).GetMethod("FrameCharacter", Private).Invoke(avatar, null);
                    camera.Render(); RenderTexture.active = rt;
                    var image = new Texture2D(720, 1024, TextureFormat.RGBA32, false);
                    image.ReadPixels(new Rect(0, 0, 720, 1024), 0, 0); image.Apply();
                    File.WriteAllBytes("output/first-boss/" + name + ".png", image.EncodeToPNG());
                    Object.DestroyImmediate(image); camera.targetTexture = null; RenderTexture.active = previous; rt.Release(); Object.DestroyImmediate(rt);
                }
            }
            finally { RenderTexture.active = previous; EditorSceneManager.ClosePreviewScene(scene); }
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private static void ValidateVictorySequence(BossAvatarPresentation presentation, Animator animator)
        {
            var type = typeof(BossAvatarPresentation);
            type.GetField("_animator", Private).SetValue(presentation, animator);
            type.GetField("_nextResultState", Private).SetValue(presentation, "Laugh");
            type.GetMethod("Play", Private).Invoke(presentation, new object[] { "Victory", true });
            animator.Update(.15f);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Victory"), "Victory starts first");
            var advance = type.GetMethod("UpdateResult", Private);
            advance.Invoke(presentation, new object[] { 0f });
            animator.Update(.15f);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Victory"), "Victory is not interrupted before clip end");
            advance.Invoke(presentation, new object[] { float.MaxValue }); animator.Update(.15f);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Laugh"), "Laugh follows victory");
            advance.Invoke(presentation, new object[] { float.MaxValue }); animator.Update(.15f);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("StandIdle"), "Laugh ends in idle");
        }
    }
}

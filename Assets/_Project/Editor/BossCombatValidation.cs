using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PushStars.Core;
using PushStars.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class BossCombatValidation
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Output = "output/boss-combat/";
        private static bool _standingPreview;
        public static void RunStandingPreview()
        {
            _standingPreview = true;
            try { Run(); }
            finally { _standingPreview = false; }
        }
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            Directory.CreateDirectory(Output);
            var fields = typeof(FightRequest).GetFields(BindingFlags.Static | BindingFlags.NonPublic);
            var saved = fields.Select(f => f.GetValue(null)).ToArray();
            bool hadProgress = PlayerPrefs.HasKey("boss_progress"); int progress = PlayerPrefs.GetInt("boss_progress");
            try
            {
                CheckHealth();
                PlayerPrefs.SetInt("boss_progress", 0); FightRequest.Boss();
                CheckScene("FightPreparation"); CheckScene("Fight");
                File.WriteAllText(Output + "validation.txt", "PASS: 5 perfect / 7 minimum-form reps; deterministic damage and clamping; 14 boss hits defeat player; no damage after KO; win/loss/draw; both scene bindings, real avatars, HP animation and damage labels; 390x844 and 320x568 renders. No CV camera or rewards started.\n");
            }
            catch (Exception e) { File.WriteAllText(Output + "validation.txt", "FAIL: " + e); throw; }
            finally
            {
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, saved[i]);
                if (hadProgress) PlayerPrefs.SetInt("boss_progress", progress); else PlayerPrefs.DeleteKey("boss_progress");
            }
        }

        private static void CheckHealth()
        {
            foreach (int form in new[] { 0, 50, 100 })
            {
                var state = new BossCombatState("novice"); int reps = 0, events = 0;
                state.Damaged += (_, __) => events++;
                while (!state.Knockout) { state.PlayerRep(form); reps++; }
                Require(reps >= 5 && reps <= 7 && state.PlayerWins && !state.Draw, "First boss needs 5–7 reps");
                Require(reps == (form == 0 ? 7 : form == 100 ? 5 : 6), "Form damage thresholds");
                state.PlayerRep(100); state.BossAttack();
                Require(events == reps && state.BossHp == 0 && state.PlayerHp == 1000, "KO stops damage and clamps HP");
            }
            Require(BossCombatState.RepDamage(-1) == 70 && BossCombatState.RepDamage(101) == 100 &&
                BossCombatState.RepDamage(float.NaN) == 70 && BossCombatState.RepDamage(float.PositiveInfinity) == 70, "Form bounds");
            var loss = new BossCombatState("novice");
            for (int i = 0; i < 13; i++) loss.BossAttack();
            Require(loss.PlayerHp == 25 && !loss.Knockout, "Boss attack cadence damage");
            loss.BossAttack(); Require(loss.PlayerHp == 0 && !loss.PlayerWins && !loss.Draw, "Boss KO victory");
            var timeout = new BossCombatState("athlete"); Require(timeout.Draw, "Equal HP draws");
            timeout.PlayerRep(100); Require(timeout.PlayerWins, "Higher HP wins on timeout");
            timeout.BossAttack(); timeout.BossAttack(); Require(!timeout.PlayerWins && !timeout.Draw, "Lower HP loses on timeout");
        }

        private static void CheckScene(string name)
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/" + name + ".unity");
            var previous = RenderTexture.active;
            var textures = new System.Collections.Generic.List<RenderTexture>();
            try
            {
                var c = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BossCombatScreen>(true)).Single();
                Require(c.Root && c.PlayerStage && c.BossStage && c.Home && c.Action && c.DamageLabels.Length == 6, "Scene bindings");
                var canvas = c.GetComponent<Canvas>();
                var scaler = canvas.GetComponent<CanvasScaler>(); if (scaler) scaler.enabled = false;
                canvas.renderMode = RenderMode.WorldSpace; canvas.scaleFactor = 1;
                var rect = (RectTransform)canvas.transform; rect.position = Vector3.zero; rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity; rect.sizeDelta = new Vector2(390, 844);
                foreach (var avatar in new[] { c.PlayerStage, c.BossStage })
                {
                    typeof(FightAvatar).GetMethod("Build", Private).Invoke(avatar, null);
                    Require(avatar.Character, "Real fighter instantiated");
                    var animator = avatar.Character.GetComponentInChildren<Animator>();
                    string pose = avatar == c.BossStage ? "StandIdle" : "WarriorIdle";
                    if (!c.Preparation && avatar == c.PlayerStage) pose = _standingPreview ? "StandIdle" : "PushUp";
                    if (animator.HasState(0, Animator.StringToHash(pose))) { animator.Play(pose, 0, .12f); animator.Update(0); }
                    var stage = avatar.StageCamera; stage.scene = scene;
                    var rt = new RenderTexture(720, 1024, 24); textures.Add(rt); stage.targetTexture = rt; stage.ResetAspect();
                    typeof(FightAvatar).GetMethod("FrameCharacter", Private).Invoke(avatar, null);
                    stage.Render();
                }
                c.Configure(true); Canvas.ForceUpdateCanvases(); c.Refresh(1);
                Require(c.Legacy.All(x => x == null || !x.activeSelf), "Old HUD hidden");
                Require(c.BossHpText.text == "450 / 450" && c.PlayerHpText.text == "1000 / 1000", "Starting HP labels");
                var camera = new GameObject("BossCombatValidationCamera").AddComponent<Camera>(); SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                camera.scene = scene; camera.transform.position = new Vector3(0,0,-50); camera.orthographic = true;
                camera.orthographicSize = 422; camera.cullingMask = ~0; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black; canvas.worldCamera = camera;
                var output = new RenderTexture(780,1688,24); textures.Add(output); camera.targetTexture = output;
                Capture(camera,output,name);
                if (!c.Preparation && c.Forest != null) GoblinForestValidation.Check(c,camera,output,Capture);
                if (!c.Preparation)
                {
                    var health = (BossCombatState)typeof(BossCombatScreen).GetField("_health",Private).GetValue(c);
                    health.PlayerRep(100); health.BossAttack(); c.Refresh(1);
                    Require(Mathf.Abs(c.BossHpFill.FillAmount-350f/450)<.001f && c.PlayerHpFill.FillAmount==.925f, "Actual HP drives fills");
                    Require(c.DamageLabels.Count(x => x.gameObject.activeSelf)==2, "Both damage numbers shown");
                    Capture(camera,output,name+"-damage");
                    if (c.Forest != null && c.Forest.Effects != null)
                    {
                        var fx = c.Forest.Effects;
                        Require(fx.HitBursts == 1, "Only boss damage triggers petals; player damage does not");
                        for (int frame = 0; frame < 6; frame++) fx.Advance(.05f);
                        Require(fx.ActivePetals == 11, "Hit emits eleven petals");
                        Capture(camera,output,name+"-petals-hit");
                    }
                }
                var sizes = c.Preparation ? new[] {new Vector2Int(320,568)} :
                    new[] {new Vector2Int(320,568), new Vector2Int(412,915), new Vector2Int(768,1024)};
                foreach (var size in sizes)
                {
                    rect.sizeDelta = size; camera.orthographicSize = size.y * .5f;
                    var resized = new RenderTexture(size.x * 2, size.y * 2, 24); textures.Add(resized); camera.targetTexture = resized;
                    Canvas.ForceUpdateCanvases(); c.Refresh(0);
                    if (!c.Preparation && c.Forest != null) GoblinForestValidation.CheckLayout(c);
                    Capture(camera, resized, name + (size.x == 320 ? "-small" : "-" + size.x + "x" + size.y));
                }
                if (!c.Preparation && c.Forest != null && c.Forest.Effects != null)
                {
                    rect.sizeDelta = new Vector2(390,844); camera.orthographicSize = 422; camera.targetTexture = output;
                    Canvas.ForceUpdateCanvases(); c.Refresh(0);
                    var fx = c.Forest.Effects;
                    var health = (BossCombatState)typeof(BossCombatScreen).GetField("_health",Private).GetValue(c);
                    while (!health.Knockout) health.PlayerRep(100);
                    int hits = fx.HitBursts; health.PlayerRep(100);
                    Require(fx.HitBursts == hits, "No duplicate hit after knockout");
                    Require(c.BossDefeatPresentationSeconds >= 1.3f, "Fall particles have time before results");
                    float contact = c.BossDefeatPresentationSeconds - 1.3f;
                    for (int frame = 0; frame < Mathf.CeilToInt((contact + .25f) / .05f); frame++) fx.Advance(.05f);
                    Require(fx.FallBursts == 1 && fx.ActivePetals >= 28, "One ground burst on defeat");
                    var animator = c.BossStage.Character.GetComponentInChildren<Animator>();
                    if (animator.HasState(0, Animator.StringToHash("Defeat"))) { animator.Play("Defeat",0,.55f); animator.Update(0); }
                    typeof(FightAvatar).GetMethod("FrameCharacter", Private).Invoke(c.BossStage, null);
                    c.BossStage.StageCamera.Render(); c.Refresh(1);
                    foreach (var label in c.DamageLabels) label.gameObject.SetActive(false);
                    Capture(camera,output,name+"-petals-fall");
                    for (int frame = 0; frame < 60; frame++) fx.Advance(.05f);
                    Require(fx.ActivePetals == 0 && fx.FallBursts == 1, "Petals expire and fall does not repeat");
                    Require(fx.transform.childCount == ForestParticleEffects.Capacity, "Particle pool remains bounded");
                    File.WriteAllText("output/goblin-forest/particles-validation.txt", "PASS: ambient leaves; real boss hit produces 11 petals; player damage ignored; knockout produces one 28-petal ground burst; no repeated KO burst; particles expire; fixed 64-slot pool; source textures wired.\n");
                }
                c.Configure(false); Require(!c.Root.activeSelf, "Boss layer disabled outside boss mode");
            }
            finally
            {
                RenderTexture.active=previous; EditorSceneManager.ClosePreviewScene(scene);
                foreach(var texture in textures){texture.Release();Object.DestroyImmediate(texture);}
            }
        }

        private static void Capture(Camera camera,RenderTexture texture,string name)
        {
            Canvas.ForceUpdateCanvases(); camera.Render();RenderTexture.active=texture;
            var image=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();
            Require(image.GetPixels32().Count(p => p.r > 30 || p.g > 30 || p.b > 30) > texture.width * texture.height / 4, "Render contains UI, not a blank camera");
            File.WriteAllBytes(Output+name+(_standingPreview ? "-standing" : "")+".png",image.EncodeToPNG());Object.DestroyImmediate(image);
        }
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}

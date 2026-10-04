using System;
using System.IO;
using System.Linq;
using PushStars.Core;
using PushStars.Fight;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace PushStars.Editor
{
    public static class ArenaValidation
    {
        private const string Output="output/arenas";
        [MenuItem("Tools/Push Stars/Arenas/Validate And Capture")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run outside Play Mode.");
            Directory.CreateDirectory(Output);
            bool had=PlayerPrefs.HasKey(ArenaProfile.SelectionKey);string saved=PlayerPrefs.GetString(ArenaProfile.SelectionKey);
            try
            {
                State(); CaptureSelection(); CaptureHome(); CaptureBattle();
                File.WriteAllText(Output+"/validation.txt","PASS: catalog, selected arena persistence, same choices, both different-choice outcomes, exactly one roll, legacy/invalid IDs, confirmed match idempotency, scene and mode resets, split battle rendering, collection and popup at 390x844 and 320x568. Online room transport is not implemented in this project.\n");
            }
            finally
            {
                if(had)PlayerPrefs.SetString(ArenaProfile.SelectionKey,saved);else PlayerPrefs.DeleteKey(ArenaProfile.SelectionKey);
                PlayerPrefs.Save();ArenaMatch.Clear();
            }
        }
        private static void State()
        {
            var cat=ArenaCatalog.Instance; Require(cat!=null && cat.Arenas.Length==10,"Ten maps");
            Require(cat.Arenas.Select(a=>a.Id).Distinct().Count()==10 && cat.Arenas.All(a=>a.Home!=null),"Unique IDs and art");
            Require(ArenaProfile.Select("underwater") && ArenaProfile.SelectedId=="underwater","Persistent selection");
            Require(!ArenaProfile.Owns("missing"),"Unknown arenas are not owned");
            Require(!ArenaProfile.Select("missing") && ArenaProfile.SelectedId=="underwater","Reject missing selection");
            ArenaMatch.BeginLocal("jungle","jungle");Require(ArenaMatch.IsResolved && ArenaMatch.ResolveLocal(()=>throw new Exception("Unneeded roll"))=="jungle","Identical maps skip roll");
            for(int side=0;side<2;side++)
            {
                ArenaMatch.BeginLocal("crystal","lava-forge");int calls=0;
                string winner=ArenaMatch.ResolveLocal(()=>{calls++;return side;});
                Require(winner==(side==0?"crystal":"lava-forge"),"Both outcomes reachable");
                Require(ArenaMatch.ResolveLocal(()=>{calls++;return 1-side;})==winner && calls==1,"No reroll");
            }
            ArenaMatch.BeginLocal(null,"unknown");Require(ArenaMatch.WinnerId=="crystal","Legacy fallback");
            Require(ArenaMatch.AcceptConfirmed("match1","jungle","ice-temple","ice-temple"),"Confirmed winner");
            Require(!ArenaMatch.AcceptConfirmed("match1","jungle","ice-temple","jungle") && ArenaMatch.WinnerId=="ice-temple","Conflicting replay rejected");
            Require(!ArenaMatch.AcceptConfirmed("match2","jungle","ice-temple","lava-forge"),"Non-candidate rejected");
            var legacy=JsonUtility.FromJson<GhostRecord>("{\"reps\":1,\"repTimes\":[1]}");
            Require(ArenaCatalog.Normalize(legacy.arenaId)=="crystal","Old ghost migration");
            Require(GhostRecord.From(new[]{1f},90,"test").arenaId=="underwater","Record includes selected map");
            FightRequest.Training(new TrainingPlan(3,60));Require(!ArenaMatch.HasChoices,"Training clears arena duel");
            FightRequest.Clear();Require(!ArenaMatch.HasChoices,"Exit clears choices");
        }
        private static void CaptureSelection()
        {
            var scene=EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/FightPreparation.unity");
            try
            {
                var panel=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<DuelReadyPanel>(true)).Single();
                panel.Show(new DuelReadyPanel.Side("BEASTCORE_DEV",120,32,52),new DuelReadyPanel.Side("OSKAT009",98,32,52),null,null,false);
                ArenaMatch.BeginLocal("crystal","lava-forge");ArenaMatch.ResolveLocal(()=>1);
                var popup=ArenaSelectionPopup.Create(panel.Root.transform.parent,panel.GetComponentInChildren<TextMeshProUGUI>(true).font);
                popup.Highlight(1);
                var canvas=panel.GetComponentInParent<Canvas>().rootCanvas;
                Capture(canvas,scene,"selection-390",390,844);
                Capture(canvas,scene,"selection-320",320,568);
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }
        private static void CaptureHome()
        {
            var scene=EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Main.unity");
            try
            {
                var mode=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ModeSelectionController>(true)).Single();
                var so=new SerializedObject(mode);
                var open=(Button)so.FindProperty("_openButton").objectReferenceValue;
                var overlay=(GameObject)so.FindProperty("_overlay").objectReferenceValue;
                var label=(TextMeshProUGUI)so.FindProperty("_homeLabel").objectReferenceValue;
                var background=(Image)so.FindProperty("_homeBackground").objectReferenceValue;
                var home=open.transform.parent.parent;
                foreach(Transform item in home.parent)if(item.name=="LeaguePanel"||item.name=="ProfilePanel")item.gameObject.SetActive(false);
                home.gameObject.SetActive(true); background.sprite=ArenaCatalog.Get("underwater").Home;
                var controller=mode.gameObject.AddComponent<ArenaHomeController>(); controller.Configure(home,overlay.transform.parent,label.font);
                var canvas=mode.GetComponentInParent<Canvas>().rootCanvas;
                Capture(canvas,scene,"home-390",390,844);
                controller.Show();
                Capture(canvas,scene,"collection-390",390,844);
                Capture(canvas,scene,"collection-320",320,568);
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }
        private static void CaptureBattle()
        {
            var scene=EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Fight.unity");
            try
            {
                var c=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<FightController>(true)).Single();
                var so=new SerializedObject(c);var bg=(Image)so.FindProperty("_baseBackground").objectReferenceValue;
                ArenaMatch.BeginLocal("underwater","underwater"); ArenaBattleGraphic.Apply(bg,(Sprite)so.FindProperty("_duelBackground").objectReferenceValue);
                Require(bg.GetComponentInChildren<ArenaBattleGraphic>()!=null,"New map split view attached");
                Capture(bg.GetComponentInParent<Canvas>().rootCanvas,scene,"battle-underwater",390,844);
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }
        internal static void Capture(Canvas canvas,UnityEngine.SceneManagement.Scene scene,string name,int width,int height)
        {
            var scaler=canvas.GetComponent<CanvasScaler>();if(scaler!=null)scaler.enabled=false;
            foreach(var mirror in canvas.GetComponentsInChildren<DeviceSimulatorMirrorFix>(true)){mirror.enabled=false;mirror.transform.localRotation=Quaternion.identity;}
            canvas.renderMode=RenderMode.WorldSpace;canvas.scaleFactor=1;
            var rect=(RectTransform)canvas.transform;rect.position=Vector3.zero;rect.localScale=Vector3.one;rect.sizeDelta=new Vector2(width,height);
            foreach(var safe in canvas.GetComponentsInChildren<SafeAreaFitter>(true))safe.enabled=false;
            var go=new GameObject("ArenaPreviewCamera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
            var camera=go.AddComponent<Camera>();if(EditorSceneManager.IsPreviewScene(scene))camera.scene=scene;camera.transform.position=new Vector3(0,0,-50);camera.orthographic=true;camera.orthographicSize=height*.5f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            var rt=new RenderTexture(width*2,height*2,24);camera.targetTexture=rt;canvas.worldCamera=camera;
            var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try
            {
                Canvas.ForceUpdateCanvases();
                foreach(var popup in canvas.GetComponentsInChildren<ArenaSelectionPopup>())popup.SendMessage("Update");
                foreach(var collection in canvas.GetComponentsInChildren<ArenaHomeController>())collection.SendMessage("Update");
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());
            }
            finally {RenderTexture.active=previous;Object.DestroyImmediate(image);camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(go);}
        }
        private static void Require(bool condition,string message) {if(!condition)throw new InvalidOperationException(message);}
    }
}

using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
namespace PushStars.Editor
{
    public static class LeaguePresentationValidation
    {
        public static void Run()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Main.unity");
            RenderTexture rt = null; var old = RenderTexture.active;
            try
            {
                var view = scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LeagueView>(true)).First();
                var entrance = view.GetComponent<LeagueEntrance>();
                view.gameObject.SetActive(true);
                foreach(var name in new[]{"DuelPanel","ProfilePanel"}) view.transform.parent.Find(name).gameObject.SetActive(false);
                var canvas = view.GetComponentInParent<Canvas>().rootCanvas;
                canvas.GetComponent<CanvasScaler>().enabled = false;
                canvas.renderMode = RenderMode.WorldSpace; canvas.scaleFactor = 1;
                var root = (RectTransform)canvas.transform;
                root.position = Vector3.zero; root.localScale = Vector3.one; root.sizeDelta = new Vector2(390,844);
                var camera = new GameObject("LeagueValidationCamera").AddComponent<Camera>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject,scene);
                camera.scene = scene; camera.transform.position = new Vector3(0,0,-50);
                camera.orthographic = true; camera.orthographicSize = 422;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                rt = new RenderTexture(780,1688,24); camera.targetTexture = rt; canvas.worldCamera = camera;
                Canvas.ForceUpdateCanvases(); view.GetComponent<LeagueLayout>().Fit(); view.Refresh();
                Require(view.Players.Length==10 && view.Names.Length==10 && view.Scores[9].text=="365", "Ten players bound");
                Require(entrance.Beats.All(b=>b.Group!=null), "All animation groups survive scene reload");
                entrance.Play();
                Require(entrance.Beats.All(b=>b.Group.alpha==0) && view.ProgressBar.Fill==0, "No first-frame flash");
                entrance.SendMessage("Sample", .15f);
                Require(entrance.Beats[0].Group.alpha>0 && entrance.Beats[1].Group.alpha==0 && view.ProgressBar.Fill==0,"Hero enters first");
                Capture(camera,rt,"entrance-hero");
                entrance.SendMessage("Sample", .36f);
                Require(entrance.Beats[1].Group.alpha>0 && entrance.Beats[2].Group.alpha==0,"Title follows hero");
                entrance.SendMessage("Sample", .7f);
                Require(entrance.Beats[2].Group.alpha>0 && view.ProgressBar.Fill>0 && view.ProgressBar.Fill<view.Progress,"Score and progress enter together");
                Capture(camera,rt,"entrance-progress");
                entrance.SendMessage("Sample",1.8f); entrance.SendMessage("Finish");
                Require(!entrance.IsPlaying && entrance.Leaderboard.enabled && entrance.Beats.All(b=>b.Group.alpha==1),"Entrance completes and unlocks scrolling");
                Capture(camera,rt,"top-ten");
                for(int i=0;i<3;i++)
                {
                    entrance.Play(); entrance.SendMessage("Sample",.19f); entrance.SendMessage("OnDisable");
                    Require(entrance.Beats.All(b=>b.Group.transform.localScale==b.RestScale),"Cancellation restores scales");
                    entrance.Play(); entrance.SendMessage("Sample",1.8f); entrance.SendMessage("Finish");
                    Require(entrance.Beats.All(b=>Vector2.Distance(((RectTransform)b.Group.transform).anchoredPosition,b.RestPosition)<.01f),"Reopening does not accumulate offsets");
                }
                var scroll = entrance.Leaderboard;
                Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition=0; Canvas.ForceUpdateCanvases();
                var last = (RectTransform)scroll.content.GetChild(9); var corners=new Vector3[4];last.GetWorldCorners(corners);
                Require(corners.All(c=> { var p=scroll.viewport.InverseTransformPoint(c);return p.y>=scroll.viewport.rect.yMin-.1f&&p.y<=scroll.viewport.rect.yMax+.1f;}),"Tenth row is reachable inside viewport");
                Require(scroll.content.GetChild(0).GetComponent<RectTransform>().anchoredPosition.y-scroll.content.GetChild(1).GetComponent<RectTransform>().anchoredPosition.y==69,"Row pitch increased to 69");
                Require(scroll.viewport.GetComponent<RectMask2D>()!=null&&scroll.viewport.GetComponent<Image>().raycastTarget,"Clipped viewport receives drag input");
                Capture(camera,rt,"top-ten-bottom");
                Directory.CreateDirectory("output/league");
                File.WriteAllText("output/league/entrance-validation.txt","PASS: scene serialization, 10 rows, staged hero/title/score/progress, cancellation and repeated entrance, 69-unit pitch, masked scrolling to rank 10.\n");
                Debug.Log("[League validation] PASS");
            }
            finally {RenderTexture.active=old;EditorSceneManager.ClosePreviewScene(scene);if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}}
        }
        static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        static void Capture(Camera camera,RenderTexture rt,string name)
        {
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;
            var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
            try {image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();Directory.CreateDirectory("output/league");File.WriteAllBytes("output/league/"+name+".png",image.EncodeToPNG());}
            finally {Object.DestroyImmediate(image);}
        }
    }
}

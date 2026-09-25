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
        public static void MigrateAndRun()
        {
            LeaguePresentationSetup.InstallWholePageScrolling();
            LeaguePresentationSetup.InstallWholePageScrolling();
            Run();
        }
        public static void Run()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Main.unity");
            RenderTexture rt = null; var old = RenderTexture.active;
            try
            {
                var view = scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LeagueView>(true)).First();
                var layout = view.GetComponent<LeagueLayout>();
                var entrance = view.GetComponent<LeagueEntrance>();
                view.gameObject.SetActive(true);
                foreach(var name in new[]{"DuelPanel","ProfilePanel"}) view.transform.parent.Find(name).gameObject.SetActive(false);
                var canvas = view.GetComponentInParent<Canvas>().rootCanvas;
                canvas.GetComponent<CanvasScaler>().enabled = false;
                canvas.renderMode = RenderMode.WorldSpace; canvas.scaleFactor = 1;
                var root = (RectTransform)canvas.transform;
                foreach (var safe in canvas.GetComponentsInChildren<SafeAreaFitter>(true))
                {
                    safe.enabled = false;
                    var safeRect = (RectTransform)safe.transform;
                    safeRect.anchorMin = Vector2.zero; safeRect.anchorMax = Vector2.one;
                    safeRect.offsetMin = safeRect.offsetMax = Vector2.zero;
                }
                root.position = Vector3.zero; root.localScale = Vector3.one; root.sizeDelta = new Vector2(390,844);
                var camera = new GameObject("LeagueValidationCamera").AddComponent<Camera>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject,scene);
                camera.scene = scene; camera.transform.position = new Vector3(0,0,-50);
                camera.orthographic = true; camera.orthographicSize = 422;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                rt = new RenderTexture(780,1688,24); camera.targetTexture = rt; canvas.worldCamera = camera;
                Canvas.ForceUpdateCanvases(); view.GetComponent<LeagueLayout>().Fit(); view.Refresh();
                Require(view.Names.Length==10 && view.Names.Count(n=>n.transform.parent.gameObject.activeSelf)==1,
                    "Only the local player is visible before online rankings are connected");
                Require(entrance.Beats.All(b=>b.Group!=null), "All animation groups survive scene reload");
                entrance.Play();
                Require(entrance.Beats.All(b=>b.Group.alpha==0) && view.ProgressBar.Fill==0, "No first-frame flash");
                entrance.SendMessage("Sample", .15f);
                Require(entrance.Beats[0].Group.alpha>0 && entrance.Beats[1].Group.alpha==0 && view.ProgressBar.Fill==0,"Hero enters first");
                Capture(camera,rt,"entrance-hero");
                entrance.SendMessage("Sample", .36f);
                Require(entrance.Beats[1].Group.alpha>0 && entrance.Beats[2].Group.alpha==0,"Title follows hero");
                entrance.SendMessage("Sample", .7f);
                Require(entrance.Beats[2].Group.alpha>0 && view.ProgressBar.Fill>=0 && view.ProgressBar.Fill<=view.Progress,"Score and progress enter together");
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
                var player = (RectTransform)layout.Rows.Find("CurrentPlayerRow"); var corners=new Vector3[4];player.GetWorldCorners(corners);
                Require(corners.All(c=> { var p=scroll.viewport.InverseTransformPoint(c);return p.y>=scroll.viewport.rect.yMin-.1f&&p.y<=scroll.viewport.rect.yMax+.1f;}),"Local player card is visible without scrolling through empty ranks");
                Require(scroll.viewport.GetComponent<RectMask2D>()!=null&&scroll.viewport.GetComponent<Image>().raycastTarget,"Clipped viewport receives drag input");
                Capture(camera,rt,"top-ten-bottom");
                Require(scroll.content == layout.Art && layout.Rows.IsChildOf(layout.Art), "Header and rows share page content");
                Require(view.GetComponentsInChildren<ScrollRect>(true).Length == 1, "Only one scroll handler in the league page");
                var nav = (RectTransform)view.transform.parent.Find("BottomNav");
                Require(nav != null && !nav.IsChildOf(scroll.content), "Navigation remains outside the scrolling page");
                var eventSystem = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EventSystem>(true)).First();
                // A long ranking is a preview-only fixture; runtime still shows only real local data.
                for (int i = 0; i < view.Names.Length; i++)
                {
                    var row = (RectTransform)view.Names[i].transform.parent;
                    row.gameObject.SetActive(true); row.anchoredPosition = new Vector2(0, -i * 69);
                }
                foreach (var size in new[] { new Vector2(390, 644), new Vector2(390, 844), new Vector2(430, 932) })
                {
                    root.sizeDelta = size;
                    Canvas.ForceUpdateCanvases(); layout.Fit(); Canvas.ForceUpdateCanvases();
                    scroll.verticalNormalizedPosition = 1; Canvas.ForceUpdateCanvases();
                    var titleBefore = view.Title.transform.position;
                    var rowBefore = player.position;
                    var navBefore = nav.position;
                    scroll.OnScroll(new PointerEventData(eventSystem) { scrollDelta = new Vector2(0, -4) });
                    Canvas.ForceUpdateCanvases(); layout.Fit();
                    Require(view.Title.transform.position.y > titleBefore.y + 1, "Wheel moves the header on " + size);
                    Require(Mathf.Abs((view.Title.transform.position.y-titleBefore.y) - (player.position.y-rowBefore.y)) < .1f,
                        "Header and rows move together on " + size);
                    Require(Vector3.Distance(nav.position, navBefore) < .01f, "Navigation stays fixed on " + size);
                    var raycaster = canvas.GetComponent<GraphicRaycaster>();
                    var drag = new PointerEventData(eventSystem) {
                        button = PointerEventData.InputButton.Left,
                        position = RectTransformUtility.WorldToScreenPoint(camera, view.Title.transform.position),
                        pointerPressRaycast = new RaycastResult { module = raycaster }
                    };
                    float beforeDrag = layout.Art.anchoredPosition.y;
                    scroll.OnInitializePotentialDrag(drag); scroll.OnBeginDrag(drag);
                    drag.position += new Vector2(0, 80); scroll.OnDrag(drag); scroll.OnEndDrag(drag);
                    Require(layout.Art.anchoredPosition.y > beforeDrag, "Drag starting over the header scrolls the page on " + size);
                    scroll.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases(); layout.Fit();
                    var last = (RectTransform)view.Names[9].transform.parent;
                    last.GetWorldCorners(corners);
                    Require(corners.All(c => { var p = scroll.viewport.InverseTransformPoint(c); return p.y >= scroll.viewport.rect.yMin-.1f && p.y <= scroll.viewport.rect.yMax+.1f; }),
                        "Last ranking card is fully reachable on " + size);
                    float bottomOffset = layout.Art.anchoredPosition.y;
                    layout.Fit();
                    Require(Mathf.Abs(layout.Art.anchoredPosition.y-bottomOffset) < .01f, "Layout does not reset the scroll position");
                }
                root.sizeDelta = new Vector2(390, 844);
                Canvas.ForceUpdateCanvases(); layout.Fit();
                scroll.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases();
                Capture(camera,rt,"whole-page-bottom");
                view.Refresh(); layout.Fit();
                Require(layout.Art.rect.height == 700f, "Hidden ranking slots leave no empty scroll tail");
                entrance.Play();
                Require(Mathf.Abs(layout.Art.anchoredPosition.y) < .01f, "Reopening resets the whole page to the top");
                entrance.SendMessage("Finish");
                Directory.CreateDirectory("output/league");
                File.WriteAllText("output/league/entrance-validation.txt","PASS: local player binding, staged entrance, cancellation/reopening, shared header/list scrolling, wheel and header drag, fixed navigation, last-row reachability at 390x644 / 390x844 / 430x932, no empty ranking tail.\n");
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

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
                Require(layout.Art.rect.height == Mathf.Max(700f, -layout.Rows.anchoredPosition.y + 77f), "Hidden ranking slots leave no empty scroll tail");
                entrance.Play();
                Require(Mathf.Abs(layout.Art.anchoredPosition.y) < .01f, "Reopening resets the whole page to the top");
                entrance.SendMessage("Finish");
                scroll.verticalNormalizedPosition = 1;
                Capture(camera, rt, "redesign-bronze");
                var style = view.GetComponent<LeagueVisualStyle>();
                if (style != null)
                {
                    var sprites = new[] { view.BronzeHero, view.SilverHero, view.GoldHero, view.DiamondHero };
                    long[] scores = { 240, 640, 980, 1640 };
                    for (int i = 0; i < PushStars.Core.Leagues.All.Length; i++)
                    {
                        var tier = PushStars.Core.Leagues.All[i];
                        style.Refresh(tier.Id, scores[i]);
                        view.SendMessage("ApplyHero", sprites[i]);
                        Canvas.ForceUpdateCanvases();
                        var heroMesh = view.Hero.canvasRenderer.GetMesh();
                        {
                            Require(heroMesh.vertexCount > 0, "Hero has rendered geometry");
                            var vertices = heroMesh.vertices;
                            float actualCupX = Mathf.Lerp(vertices.Min(v => v.x), vertices.Max(v => v.x), view.HeroCupCenterX(sprites[i]));
                            float pageCenter = layout.Art.TransformPoint(layout.Art.rect.center).x;
                            Require(Mathf.Abs(view.Hero.rectTransform.TransformPoint(new Vector3(actualCupX, 0)).x - pageCenter) < .6f,
                                "Rendered trophy, including preserveAspect padding, is centered for " + tier.Id);
                        }

                        view.Title.text = tier.DisplayName.ToUpperInvariant() + " LEAGUE";
                        view.Score.text = scores[i].ToString();
                        foreach (var score in view.Scores)
                            if (score != null && score.transform.parent.name == "CurrentPlayerRow") score.text = scores[i].ToString();
                        view.ProgressBar.Fill = PushStars.Core.Leagues.Progress(scores[i]); view.ProgressBar.Refresh();
                        Require(style.Minimum.text == tier.MinTrophies.ToString(), "Current league minimum for " + tier.Id);
                        Require(style.Maximum.text == (tier.IsTop ? "MAX" : (tier.MaxTrophies + 1).ToString()), "Only next-tier target for " + tier.Id);
                        Require(style.TierIcons[0].sprite == style.TrophySprites[i], "Start uses current league trophy for " + tier.Id);
                        if (!tier.IsTop) Require(style.TierIcons[4].sprite == style.TrophySprites[i + 1], "End uses next league trophy for " + tier.Id);
                        Require(style.TierIcons.Skip(1).Take(3).All(icon => !icon.gameObject.activeSelf), "No intermediate trophy icons for " + tier.Id);
                        Require(style.ScoreCup.sprite == style.TrophySprites[i], "Score uses a matching trophy for " + tier.Id);
                        Require(view.GetComponentsInChildren<UnityEngine.UI.Image>().All(icon => icon.name != "ScoreCrown"), "No crown score marker");
                        Require(style.TierIcons.Count(icon => icon.gameObject.activeSelf) == (tier.IsTop ? 1 : 2), "Only range endpoint cups for " + tier.Id);
                        Capture(camera, rt, "redesign-" + tier.Id + "-progress");
                        foreach (var label in new[] { view.Title, view.Score })
                        {
                            label.ForceMeshUpdate();
                            Require(!label.isTextOverflowing, "League text fits: " + label.name + " / " + tier.Id);
                        }
                    }
                    style.Refresh("bronze", 399);
                    Require(!style.Next.gameObject.activeSelf, "Remaining-to-next-league caption is hidden");
                    style.Refresh("silver", 400);
                    Require(style.Minimum.text == "400" && style.Maximum.text == "800", "Promotion resets the next goal");
                    foreach (long value in new long[] { 0, 399, 400, 640, 799, 800, 1199, 1200, 1640 })
                    {
                        var tier = PushStars.Core.Leagues.ForTrophies(value);
                        style.Refresh(tier.Id, value);
                        float expected = tier.IsTop ? 1 : (value - tier.MinTrophies) / 400f;
                        Require(Mathf.Abs(PushStars.Core.Leagues.Progress(value) - expected) < .0001f, "Per-league progress at " + value);
                        if (!tier.IsTop) for (int n = 0; n < 5; n++)
                            Require(style.TierLabels[n].text == (tier.MinTrophies + 100 * n).ToString(), "Quarter milestone at " + value);
                    }
                    view.Refresh();
                    var swipe = view.Hero.GetComponent<LeagueHeroSwipe>();
                    Require(swipe != null && view.Hero.raycastTarget && swipe.PageScroll == scroll, "Hero receives gestures and forwards vertical scrolling");
                    while (view.BrowseLeague(-1)) { }
                    var gesture = new PointerEventData(eventSystem) { button = PointerEventData.InputButton.Left,
                        pressPosition = new Vector2(600, 600), position = new Vector2(600, 600),
                        pointerPressRaycast = new RaycastResult { module = canvas.GetComponent<GraphicRaycaster>() } };
                    swipe.OnInitializePotentialDrag(gesture); swipe.OnBeginDrag(gesture);
                    var restingHero = view.Hero.rectTransform.anchoredPosition;
                    gesture.position += new Vector2(-300, 10); swipe.OnDrag(gesture);
                    Require(view.ViewedLeagueId == "bronze" && swipe.SlideOffset < -50, "Hero follows finger before changing the selected league");
                    var neighbour = view.Hero.transform.parent.Find("LeagueSwipePreview");
                    Require(neighbour != null && neighbour.GetComponent<Image>().sprite == view.SilverHero, "Next illustration enters alongside the current one");
                    Capture(camera, rt, "carousel-mid-drag");
                    swipe.OnEndDrag(gesture);
                    Require(swipe.IsSettling && view.ViewedLeagueId == "bronze", "Release starts a slide rather than replacing the image");
                    float releaseOffset = swipe.SlideOffset;
                    swipe.SendMessage("Advance", .1f);
                    Require(swipe.SlideOffset < releaseOffset && view.ViewedLeagueId == "bronze", "Outgoing image keeps moving left while settling");
                    Capture(camera, rt, "carousel-settling");
                    swipe.SendMessage("Advance", .3f);
                    Require(!swipe.IsMoving && view.Hero.transform.parent.Find("LeagueSwipePreview") == null, "Completed slide removes the temporary neighbour");
                    Require(view.ViewedLeagueId == "silver" && view.Hero.sprite == view.SilverHero, "Left hero swipe opens Silver");
                    Require(style.Minimum.text == "400" && style.Maximum.text == "800", "Browsing changes the visible progress range");
                    // Server-shaped data belongs only to this preview scene; it is never persisted or uploaded.
                    view.SendMessage("ApplyStandings", new PushStars.Services.LeaguePage {
                        league = "silver", total = 2, seeded = true,
                        season = new PushStars.Services.LeagueSeason { id = "2026-10", name = "OCTOBER", endAtMs = long.MaxValue },
                        own = new PushStars.Services.LeagueRow { uid = "preview-own", league = "bronze", trophies = 240, rank = 3 },
                        rows = new[] {
                            new PushStars.Services.LeagueRow { uid = "preview-a", displayName = "PREVIEW A", league = "silver", trophies = 780, rank = 1 },
                            new PushStars.Services.LeagueRow { uid = "preview-b", displayName = "PREVIEW B", league = "silver", trophies = 740, rank = 2 }
                        }
                    });
                    Require(layout.Rows.Find("OnlinePlayer_preview-a") != null && view.Online.text == "2 PLAYERS", "Selected league renders its returned leaders");
                    Require(view.transform.Find("PinnedOwnRank") == null && view.Progress == 0, "Browsing a higher league does not move the player's rank or fill");
                    Capture(camera, rt, "swipe-silver-leaders-fixture");
                    view.BrowseLeague(1);
                    Require(view.ViewedLeagueId == "gold" && layout.Rows.Find("OnlinePlayer_preview-a") == null, "Changing league immediately removes the previous leaders");
                    Require(view.Score.text == "240", "Browsing keeps the player's trophy total");
                    view.BrowseLeague(1);
                    Require(!view.BrowseLeague(1) && view.ViewedLeagueId == "diamond", "No swipe past Diamond");
                    gesture.pressPosition = new Vector2(200, 600); gesture.position = gesture.pressPosition;
                    swipe.OnBeginDrag(gesture); gesture.position += new Vector2(300, 0); swipe.OnDrag(gesture); swipe.OnEndDrag(gesture); swipe.SendMessage("Advance", .3f);
                    Require(view.ViewedLeagueId == "gold", "Right hero swipe opens the previous league");
                    var goldRest = view.Hero.rectTransform.anchoredPosition;
                    gesture.pressPosition = new Vector2(400, 600); gesture.position = gesture.pressPosition;
                    swipe.OnBeginDrag(gesture); gesture.position += new Vector2(-24, 0); swipe.OnDrag(gesture); swipe.OnEndDrag(gesture);
                    Require(swipe.IsSettling, "A short drag animates back"); swipe.SendMessage("Advance", .3f);
                    Require(view.ViewedLeagueId == "gold" && Vector2.Distance(goldRest, view.Hero.rectTransform.anchoredPosition) < .01f, "Short drag restores the exact optical alignment");
                    gesture.position = gesture.pressPosition;
                    swipe.OnBeginDrag(gesture); gesture.position += new Vector2(-150, 0); swipe.OnDrag(gesture); swipe.CancelSlide();
                    Require(!swipe.IsMoving && Vector2.Distance(goldRest, view.Hero.rectTransform.anchoredPosition) < .01f && scroll.enabled, "Cancelled drag restores artwork and scroll input");
                    root.sizeDelta = new Vector2(390, 644); Canvas.ForceUpdateCanvases(); layout.Fit();
                    scroll.verticalNormalizedPosition = 1;
                    gesture.pressPosition = RectTransformUtility.WorldToScreenPoint(camera, view.Hero.transform.position); gesture.position = gesture.pressPosition;
                    swipe.OnInitializePotentialDrag(gesture); swipe.OnBeginDrag(gesture);
                    gesture.position += new Vector2(5, 50); swipe.OnDrag(gesture);
                    gesture.position += new Vector2(0, 120); swipe.OnDrag(gesture); swipe.OnEndDrag(gesture);
                    Require(view.ViewedLeagueId == "gold" && layout.Art.anchoredPosition.y > 1, "Vertical hero drag scrolls without switching leagues");
                    view.SendMessage("OnDisable"); view.Refresh();
                    Require(view.ViewedLeagueId == PushStars.Core.LocalProfile.League.Id, "Reopening starts at the player's own league");
                    foreach (var size in new[] { new Vector2(390, 644), new Vector2(430, 932) })
                    {
                        root.sizeDelta = size; camera.orthographicSize = size.y / 2;
                        camera.targetTexture = null; RenderTexture.active = null; rt.Release(); rt.width = (int)size.x * 2; rt.height = (int)size.y * 2; rt.Create(); camera.targetTexture = rt;
                        Canvas.ForceUpdateCanvases(); layout.Fit(); scroll.verticalNormalizedPosition = 1;
                        Capture(camera, rt, "redesign-" + (int)size.x + "x" + (int)size.y);
                    }
                }
                Directory.CreateDirectory("output/league");
                File.WriteAllText("output/league/entrance-validation.txt","PASS: local player binding, staged entrance, cancellation/reopening, shared header/list scrolling, wheel and header drag, fixed navigation, last-row reachability at 390x644 / 390x844 / 430x932, no empty ranking tail, all four tier themes, text fit, promotion targets, finger-following carousel, outgoing and incoming frames, short-drag snapback and cancellation, optical trophy centering, hero swipe in both directions and at boundaries, vertical gesture forwarding, selected-league leaders and stale row cleanup.\n");
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

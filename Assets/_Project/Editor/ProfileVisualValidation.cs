using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PushStars.Core;
using PushStars.UI;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    public static class ProfileVisualValidation
    {
        public static void Run()
        {
            ValidateInsights();
            ValidateAchievements();
            Require(CountryCatalog.Entries.Count==250 && CountryCatalog.Entries.All(e=>CountryFlags.Get(e.code)!=null),"All bundled countries have a flag");
            Require(CountryCatalog.Normalize(" md ")=="MD" && CountryCatalog.Normalize("unknown")=="" && CountryFlags.Get("unknown")==null,"Country codes normalize without invented fallback flags");
            var scene=EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Main.unity");
            var language=typeof(Localization).GetField("_currentLanguage",BindingFlags.NonPublic|BindingFlags.Static);
            var previous=language.GetValue(null);
            RenderTexture target=null;Camera camera=null;var oldTarget=RenderTexture.active;
            try
            {
                var dash=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProfileDashboard>(true)).First();
                foreach(var name in new[]{"DuelPanel","LeaguePanel"})dash.transform.parent.Find(name).gameObject.SetActive(false);
                dash.gameObject.SetActive(true);
                var canvas=dash.GetComponentInParent<Canvas>().rootCanvas;
                canvas.GetComponent<CanvasScaler>().enabled=false;canvas.renderMode=RenderMode.WorldSpace;canvas.scaleFactor=1;
                var root=(RectTransform)canvas.transform;root.position=Vector3.zero;root.localScale=Vector3.one;
                foreach(var safe in canvas.GetComponentsInChildren<SafeAreaFitter>(true))
                {safe.enabled=false;var r=(RectTransform)safe.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
                camera=new GameObject("ProfilePreviewCamera").AddComponent<Camera>();SceneManager.MoveGameObjectToScene(camera.gameObject,scene);
                camera.scene=scene;camera.transform.position=new Vector3(0,0,-50);camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color32(13,23,65,255);canvas.worldCamera=camera;
                var fit=dash.GetComponentInChildren<DashboardWidthFit>();var scroll=dash.GetComponentInChildren<ScrollRect>();
                var country=dash.GetComponent<ProfileCountryPicker>();
                var today=new DateTime(2026,10,3);
                var fixture=new List<MatchRecord>();
                int[] daily={22,0,34,16,45,24,38};
                for(int i=0;i<7;i++)if(daily[i]>0)fixture.Add(new MatchRecord{Mode=i%2==0?"training":"ghost",Exercise="pushups",MyReps=daily[i],OpponentReps=12,Won=true,OpponentName="NOVA",DurationSec=60,CreatedAt=today.AddDays(i-6).AddHours(12),IsRecord=i==4});
                fixture.Add(new MatchRecord{Mode="assessment",Exercise="pushups",MyReps=8,DurationSec=60,CreatedAt=today.AddDays(-12),IsRecord=true});
                fixture.Add(new MatchRecord{Mode="boss",Exercise="pushups",MyReps=19,OpponentReps=22,DurationSec=60,CreatedAt=today.AddDays(-8)});
                var populated=new UserProfile{Exists=true,DisplayName="Player",TotalReps=1284,TotalWins=24,TotalLosses=8,Trophies=642,Xp=840};
                var sources=new Dictionary<TMP_Text,string>();
                foreach(var size in new[]{new Vector2(390,844),new Vector2(320,568),new Vector2(430,932)})
                {
                    root.sizeDelta=size;camera.orthographicSize=size.y/2;camera.aspect=size.x/size.y;
                    camera.targetTexture=null;RenderTexture.active=oldTarget;if(target!=null)UnityEngine.Object.DestroyImmediate(target);
                    target=new RenderTexture((int)size.x*2,(int)size.y*2,24);camera.targetTexture=target;
                    foreach(string lang in new[]{"en","ru","pt-BR"})
                    {
                        language.SetValue(null,lang);
                        dash.RenderSnapshot(populated,fixture,45,today);dash.SelectFilter(0);
                        foreach(var entry in sources)if(entry.Key!=null)entry.Key.text=entry.Value;
                        sources.Clear();
                        country.RefreshHeader();
                        foreach(var label in dash.GetComponentsInChildren<TMP_Text>(true).Concat(dash.Achievements.Collection.GetComponentsInChildren<TMP_Text>(true)).Concat(country.Root.GetComponentsInChildren<TMP_Text>(true)))
                        {sources[label]=label.text;if(label.name!="PlayerName")label.text=Localization.Text(label.text);}
                        Canvas.ForceUpdateCanvases();fit.Fit();Canvas.ForceUpdateCanvases();scroll.verticalNormalizedPosition=1;Canvas.ForceUpdateCanvases();
                        var pattern=dash.transform.Find("ProfileBackground/Pattern").GetComponent<Image>();
                        float sourceRatio=pattern.sprite.rect.width/pattern.sprite.rect.height;
                        Require(Mathf.Abs(pattern.rectTransform.rect.width/pattern.rectTransform.rect.height-sourceRatio)<.001f,"Background preserves its source proportions");
                        Require(dash.Activity.text=="179","Weekly reps exclude old workouts");
                        Require(dash.WinRate.text=="75%","Win rate uses actual games");
                        Capture(camera,target,$"profile-{(int)size.x}-{lang}");
                        scroll.verticalNormalizedPosition=0;Canvas.ForceUpdateCanvases();
                        Require(WithinViewport(scroll,(RectTransform)dash.History.Find("Match_7")),"Last workout clears bottom navigation at "+size);
                        if(size.x==390)Capture(camera,target,"history-"+lang);
                        Require(dash.GetComponentsInChildren<ScrollRect>().Length==1,"Single active scroll owner");
                        Require(!dash.transform.parent.Find("BottomNav").IsChildOf(scroll.content),"Navigation stays fixed");
                        var achievements=dash.Achievements;
                        Require(achievements.Featured.Length==8 && achievements.CollectionTiles.Length==16,"Eight featured medals and sixteen total");
                        float previousPosition=scroll.verticalNormalizedPosition;
                        achievements.OpenAt(15);
                        achievements.Collection.GetComponentInChildren<AchievementCollectionFit>(true).Fit();
                        Canvas.ForceUpdateCanvases();
                        achievements.CollectionScroll.verticalNormalizedPosition=0;
                        foreach(var label in achievements.Collection.GetComponentsInChildren<TMP_Text>(true))label.text=Localization.Text(label.text);
                        Canvas.ForceUpdateCanvases();
                        Require(WithinViewport(achievements.CollectionScroll,(RectTransform)achievements.CollectionTiles[15].Button.transform),"Final medal reachable at "+size);
                        Require(!scroll.enabled,"Collection suspends profile scrolling");
                        Capture(camera,target,$"achievements-{(int)size.x}-{lang}");
                        achievements.Close();
                        Require(scroll.enabled && Mathf.Abs(scroll.verticalNormalizedPosition-previousPosition)<.001f,"Closing collection restores profile position");
                        country.Open();Canvas.ForceUpdateCanvases();
                        Require(!scroll.enabled && CountryRows(country)==250,"Country picker suspends profile and lists every country");
                        country.Search.SetTextWithoutNotify("Молдова");country.Rebuild();
                        Require(CountryRows(country)==1 && country.List.content.Find("Country_MD").gameObject.activeSelf,"Country search understands translated names");
                        country.Search.SetTextWithoutNotify("ZZZZ");country.Rebuild();
                        Require(CountryRows(country)==0 && country.Empty.gameObject.activeSelf,"Empty country search is explicit");
                        country.Search.SetTextWithoutNotify("Moldova");country.Rebuild();
                        foreach(var label in country.Root.GetComponentsInChildren<TMP_Text>(true))if(label!=country.Search.textComponent)label.text=Localization.Text(label.text);
                        Capture(camera,target,$"country-{(int)size.x}-{lang}");
                        country.Close();
                        Require(scroll.enabled && Mathf.Abs(scroll.verticalNormalizedPosition-previousPosition)<.001f,"Country picker restores profile scroll position");
                    }
                }
                language.SetValue(null,"en");
                foreach(var entry in sources)if(entry.Key!=null)entry.Key.text=entry.Value;
                dash.SelectFilter(1);Require(Count(dash)==5,"Training includes assessments");
                dash.SelectFilter(2);Require(Count(dash)==2,"Battles include ghost matches");
                dash.SelectFilter(3);Require(Count(dash)==1,"Boss filter excludes ghosts");
                dash.RenderSnapshot(new UserProfile(),new List<MatchRecord>(),0,today);dash.SelectFilter(0);
                Require(dash.Empty.gameObject.activeSelf && dash.WinRate.text=="—","Honest new-player state");
                root.sizeDelta=new Vector2(390,844);camera.orthographicSize=422;camera.aspect=390f/844;
                camera.targetTexture=null;RenderTexture.active=oldTarget;UnityEngine.Object.DestroyImmediate(target);target=new RenderTexture(780,1688,24);camera.targetTexture=target;
                Canvas.ForceUpdateCanvases();fit.Fit();scroll.verticalNormalizedPosition=1;Canvas.ForceUpdateCanvases();Capture(camera,target,"empty-profile");
                dash.Refresh();language.SetValue(null,"ru");
                foreach(var label in dash.GetComponentsInChildren<TMP_Text>(true))if(label.name!="PlayerName")label.text=Localization.Text(label.text);
                Canvas.ForceUpdateCanvases();fit.Fit();scroll.verticalNormalizedPosition=1;Canvas.ForceUpdateCanvases();Capture(camera,target,"current-profile-ru");
                Debug.Log("[Profile] PASS: three phone sizes, EN/RU/PT-BR, live/empty/populated states, filters, activity and bottom reachability.");
            }
            finally
            {language.SetValue(null,previous);RenderTexture.active=oldTarget;if(camera!=null)camera.targetTexture=null;if(target!=null)UnityEngine.Object.DestroyImmediate(target);EditorSceneManager.ClosePreviewScene(scene);}
        }
        static int Count(ProfileDashboard d)=>d.History.Cast<Transform>().Count(t=>t.name.StartsWith("Match_")&&t.gameObject.activeSelf);
        static int CountryRows(ProfileCountryPicker picker)=>picker.List.content.Cast<Transform>().Count(t=>t.gameObject.activeSelf && t.name.StartsWith("Country_"));
        static bool WithinViewport(ScrollRect s,RectTransform r)
        {var corners=new Vector3[4];r.GetWorldCorners(corners);return corners.All(v=>{var p=s.viewport.InverseTransformPoint(v);return p.y>=s.viewport.rect.yMin-.2f&&p.y<=s.viewport.rect.yMax+.2f;});}
        static void Capture(Camera c,RenderTexture t,string name)
        {Canvas.ForceUpdateCanvases();c.Render();RenderTexture.active=t;var image=new Texture2D(t.width,t.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,t.width,t.height),0,0);image.Apply();Directory.CreateDirectory("artifacts/profile-redesign");File.WriteAllBytes("artifacts/profile-redesign/"+name+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);}
        static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException("[Profile] "+message);}
        static void ValidateInsights()
        {
            var today=new DateTime(2026,10,3);
            var rows=new List<MatchRecord>{new MatchRecord{CreatedAt=today.AddDays(-1).AddHours(12),MyReps=8},new MatchRecord{CreatedAt=today.AddDays(-1).AddHours(14),MyReps=12},new MatchRecord{CreatedAt=today.AddDays(-2).AddHours(12),MyReps=10},new MatchRecord{CreatedAt=today.AddDays(-7),MyReps=90},new MatchRecord{CreatedAt=today.AddDays(1),MyReps=100}};
            var insight=ProfileInsights.From(rows,today,90);
            Require(insight.WeekReps==30&&insight.ActiveDays==2&&insight.StreakDays==2,"Calendar days deduplicate sets, include yesterday, exclude future activity");
            Require(insight.BestSet==90,"Older personal best survives weekly window");
            Require(ProfileInsights.From(rows,today.AddDays(2),90).StreakDays==1,"Current consecutive run follows the most recent active day");
        }
        static void ValidateAchievements()
        {
            var today=new DateTime(2026,10,3);
            var catalog=ProfileAchievementCatalog.Entries;
            Require(catalog.Select(e=>e.Id).Distinct().Count()==16,"Achievement IDs are unique");
            var empty=ProfileAchievementCatalog.Progress(new UserProfile(),new List<MatchRecord>(),0,today);
            Require(empty.All(v=>v==0),"New players receive no fabricated achievements");
            Require(ProfileAchievementCatalog.BestEarned(empty).Length==0,"Unplayed profiles show no prestige medals");
            var training=new MatchRecord{MatchId="a",Mode="training",Exercise="pushups",MyReps=10,CreatedAt=today.AddDays(-4)};
            var rows=new List<MatchRecord>{training,training,
                new MatchRecord{MatchId="b",Mode="ghost",MyReps=25,Won=true,CreatedAt=today.AddDays(-4)},
                new MatchRecord{MatchId="c",Mode="pvp",MyReps=8,Draw=true,CreatedAt=today.AddDays(-2)},
                new MatchRecord{MatchId="d",Mode="boss",MyReps=12,Won=true,CreatedAt=today.AddDays(-1)},
                new MatchRecord{Mode="assessment",MyReps=5,CreatedAt=today.AddDays(-1)},
                new MatchRecord{Mode="boss",MyReps=999,Won=true,CreatedAt=today.AddDays(1)},
                new MatchRecord{Mode="training",MyReps=999,Exercise="squats",CreatedAt=today},
                new MatchRecord{Mode="training",MyReps=0,CreatedAt=today}};
            var progress=ProfileAchievementCatalog.Progress(new UserProfile(),rows,40,today);
            long Value(string id)=>progress[Array.FindIndex(catalog,e=>e.Id==id)];
            Require(Value("reps-100")==60 && Value("set-25")==40,"Duplicates/future/other exercises excluded; saved best retained");
            Require(Value("days-5")==3 && Value("modes-3")==3,"Unique days and modes, with battle modes grouped");
            Require(Value("win-1")==2 && Value("boss-1")==1,"Draws never count as wins");
            rows.RemoveAt(6);
            var afterRest=ProfileAchievementCatalog.Progress(new UserProfile(),rows,40,today.AddDays(30));
            Require(afterRest[4]==progress[4],"Rest days do not erase accumulated active days");
            var veteran=ProfileAchievementCatalog.Progress(new UserProfile{TotalReps=5000,TotalWins=100},new List<MatchRecord>(),50,today);
            Require(veteran[14]>=catalog[14].Goal && veteran[15]>=catalog[15].Goal,"Legacy aggregate milestones remain available");
            var top=ProfileAchievementCatalog.BestEarned(veteran);
            Require(top.Length==3 && top.All(i=>veteran[i]>=catalog[i].Goal),"Header displays at most three actually earned medals");
            Require(top.Distinct().Count()==3 && top.Select(i=>catalog[i].ProgressMetric).Distinct().Count()==3,"Header excludes lower tiers of the same achievement family");
            Require(top.SequenceEqual(new[]{15,14,9}),"Most demanding win, volume and strength milestones lead");
            Require(ProfileAchievementCatalog.BestEarned(veteran,1).SequenceEqual(new[]{15}),"Requested medal limit is respected");
        }
    }
}

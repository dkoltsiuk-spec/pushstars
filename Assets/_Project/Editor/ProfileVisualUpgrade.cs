using System;
using System.Linq;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    /// <summary>Compact reference-led profile with matte panels and collectible sports emblems.</summary>
    public static partial class ProfileVisualUpgrade
    {
        const string ArtPath = "Assets/_Project/UI/Sprites/ProfileBrawl/";
        static TMP_FontAsset _black, _regular, _italic;
        static Material _title, _body, _caption, _sticker;
        static Sprite _solid;
        static readonly Color Muted = new Color32(185,201,247,255);
        static readonly Color Gold = new Color32(255,215,41,255);
        static readonly Color Dark = new Color32(9,16,103,185);

        [MenuItem("Push Stars/UI/Upgrade Athlete Profile")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/_Project/Scenes/Main.unity") throw new InvalidOperationException("Open Main first.");
            var d = scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProfileDashboard>(true)).First();
            var identity = d.GetComponent<ProfileIdentityEditor>();
            var art = (RectTransform)d.transform.Find("ProfileScroll/Viewport/Content/Art");
            var gear = art.GetComponentsInChildren<Button>(true).First(b=>b.name=="GearButton");
            var avatar = (RectTransform)identity.Avatar.transform.parent.parent;
            Transform[] keep = { identity.NameLabel.transform, identity.NameShadow.transform, avatar,
                identity.EditAvatar.transform, identity.EditName.transform, gear.transform };
            foreach(var item in keep) Undo.SetTransformParent(item,art,"Keep profile identity controls");
            foreach(var item in art.Cast<Transform>().Where(t=>!keep.Contains(t)).ToArray()) Undo.DestroyObjectImmediate(item.gameObject);
            Undo.RecordObject(d,"Reference profile layout");Undo.RecordObject(identity,"Reference identity art");
            _black=Font("Rubik Black");_regular=Font("Rubik Medium");_italic=Font("Rubik BoldItalic");
            _title=Material(_black,"Title",.20f,.025f);
            _body=Material(_regular,"Body",0,0);
            _caption=Material(_black,"Caption",.12f,0);
            _sticker=Material(_italic,"Sticker",.14f,0);
            _solid=SolidSprite();
            var bg=d.transform.Find("ProfileBackground");
            foreach(var child in bg.Cast<Transform>().ToArray())Undo.DestroyObjectImmediate(child.gameObject);
            var backdrop=bg.GetComponent<Image>();backdrop.sprite=null;backdrop.type=Image.Type.Simple;backdrop.color=new Color32(29,24,153,255);
            if(bg.GetComponent<RectMask2D>()==null)Undo.AddComponent<RectMask2D>(bg.gameObject);
            var pattern=Pic(bg,"Pattern",Background(),0,0,390,844);
            var cover=pattern.gameObject.AddComponent<AspectRatioFitter>();
            cover.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;
            cover.aspectRatio=pattern.sprite.rect.width/pattern.sprite.rect.height;
            var wash=Flat(bg,"PatternWash",0,0,0,0,new Color32(24,57,211,45));
            wash.rectTransform.anchorMin=Vector2.zero;wash.rectTransform.anchorMax=Vector2.one;wash.rectTransform.offsetMin=wash.rectTransform.offsetMax=Vector2.zero;

            var page=Rect(art,"AthleteDashboard",0,0,390,1044);page.SetAsFirstSibling();
            // Open name treatment; keep space for its inline edit gear and the settings button.
            Place(identity.NameLabel.rectTransform,106,24,175,38);
            Style(identity.NameLabel,25,true,Color.white);identity.NameLabel.richText=false;
            identity.NameShadow.gameObject.SetActive(false);d.PlayerName=identity.NameLabel;
            Place(avatar,18,20,74,74);avatar.localRotation=Quaternion.identity;
            var avatarFrame=avatar.GetComponent<Image>();avatarFrame.sprite=null;avatarFrame.color=Color.black;avatarFrame.type=Image.Type.Simple;
            var mask=(RectTransform)identity.Avatar.transform.parent;Place(mask,4,4,66,66);
            var maskImage=mask.GetComponent<Image>();maskImage.sprite=null;maskImage.color=new Color32(31,169,211,255);maskImage.type=Image.Type.Simple;
            Place(identity.Avatar.rectTransform,0,0,66,66);
            identity.AvatarTextures=Enumerable.Repeat(AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath+"characters.png"),4).ToArray();
            identity.AvatarCrops=new[]{new Rect(.16f,.69f,.31f,.31f),new Rect(.602f,.686f,.30f,.30f),new Rect(.12f,.16f,.31f,.31f),new Rect(.57f,.176f,.29f,.29f)};
            identity.Avatar.texture=identity.AvatarTextures[0];identity.Avatar.uvRect=identity.AvatarCrops[0];
            Place((RectTransform)identity.EditAvatar.transform,63,9,44,44);EditGear(identity.EditAvatar);
            Place((RectTransform)identity.EditName.transform,307,10,44,44);EditGear(identity.EditName);
            identity.FollowNameEnd=true;
            SettingsGear(gear);
            d.PlayerId=Text(page,"PlayerId","LOCAL PROFILE",13,97,91,17,10,true,Muted);d.PlayerId.alignment=TextAlignmentOptions.Center;
            Text(page,"XpCaption","LEVEL PROGRESS",106,111,137,16,10,true,Muted);
            d.Xp=Text(page,"Xp","",241,110,85,18,10,true,Gold);d.Xp.alignment=TextAlignmentOptions.Right;
            Rail(page,"XpTrack",105,133,221,12,new Color32(3,16,62,255));
            d.XpFill=Rail(page,"XpFill",107,135,217,8,Gold,true);
            Pic(page,"LevelBadge",S("ProfileSettings/Group 532"),339,109,34,44);
            d.Level=Text(page,"Level","",340,111,31,39,20,true);d.Level.alignment=TextAlignmentOptions.Center;

            identity.ShowcasePortrait=null;identity.ShowcaseSprites=Array.Empty<Sprite>();
            d.ProfileEmblem=null;d.RankBadges=Array.Empty<Sprite>();
            d.LeagueButton=null;d.LeagueIcon=null;d.LeagueCups=Array.Empty<Sprite>();
            d.Trophies=null;d.LeagueName=null;d.BestSet=null;d.NextMilestone=null;d.RecordFill=null;
            d.Wins=null;d.Streak=null;d.StreakNote=null;
            Pic(page,"RepsIcon",Icon("dumbbell"),22,180,39,37);
            d.Reps=Text(page,"LifetimeReps","0",72,173,113,32,25,true);
            Text(page,"LifetimeCaption","TOTAL REPS",72,203,111,16,10,true,Muted);
            Pic(page,"RateIcon",Icon("shield"),207,177,38,42);
            d.WinRate=Text(page,"WinRate","",257,173,114,32,25,true);
            Text(page,"RateCaption","WIN RATE",257,203,112,16,10,true,Muted);

            var week=Flat(page,"WeeklyActivity",16,239,358,204,Dark).transform;
            Text(week,"Title","YOUR WEEK",13,10,182,26,19,true);
            d.ActiveDays=Text(week,"ActiveDays","",199,14,145,20,10,true,Gold);d.ActiveDays.alignment=TextAlignmentOptions.Right;
            d.Activity=Text(week,"WeekReps","0",15,39,122,42,34,true);
            d.ActivityNote=Text(week,"ActivityNote","",143,52,199,19,11,true,Muted);d.ActivityNote.alignment=TextAlignmentOptions.Right;
            Rail(week,"Grid",15,113,328,1,new Color32(103,120,203,80));Rail(week,"Baseline",15,173,328,1,new Color32(103,120,203,110));
            d.Bars=new RectTransform[7];d.DayLabels=new TextMeshProUGUI[7];d.DayValues=new TextMeshProUGUI[7];
            for(int i=0;i<7;i++)
            {
                float x=23+i*46;
                var bar=Rail(week,"Bar"+i,x,172,24,3,new Color32(32,148,255,255));bar.rectTransform.pivot=new Vector2(0,0);bar.rectTransform.anchoredPosition=new Vector2(x,-172);d.Bars[i]=bar.rectTransform;
                d.DayLabels[i]=Text(week,"DayLabel"+i,"",x-10,179,44,16,9,true,i==6?Gold:Muted);d.DayLabels[i].alignment=TextAlignmentOptions.Center;
                d.DayValues[i]=Text(week,"DayValue"+i,"",x-10,91,44,18,10,true);d.DayValues[i].alignment=TextAlignmentOptions.Center;
            }
            d.ActivityHeight=60;
            var empty=Rect(week,"WeeklyEmpty",16,86,326,77);
            Text(empty,"Encouragement","A fresh week. Let's move!",0,0,326,21,11,false,Muted).alignment=TextAlignmentOptions.Center;
            d.TrainButton=Button(empty,"TrainButton","TO TRAINING",80,27,166,41);
            d.WeeklyEmpty=empty.gameObject;

            BuildAchievements(page,d,identity);

            Text(page,"HistoryTitle","WORKOUT HISTORY",18,821,304,29,21,true);
            d.HistoryCount=Text(page,"HistoryCount","",327,824,45,22,16,true,Muted);d.HistoryCount.alignment=TextAlignmentOptions.Right;
            d.Filters=new Button[4];string[] filters={"ALL","TRAINING","BATTLES","BOSS"};
            for(int i=0;i<4;i++)d.Filters[i]=Button(page,"Filter"+i,filters[i],16+i*92,862,82,44);
            d.HistoryTop=924;d.RowHeight=92;d.History=Rect(page,"History",16,d.HistoryTop,358,100);
            var row=Rect(d.History,"MatchTemplate",0,0,358,81);
            Flat(row,"Shadow",1,4,358,77,new Color32(0,3,44,255));
            Flat(row,"Row",0,0,358,77,new Color32(22,32,119,255));
            Pic(row,"Result",Icon("dumbbell"),10,19,37,39);
            Text(row,"Opponent","",57,10,182,24,14,true);
            Text(row,"Detail","",57,38,284,18,10,false,Muted);
            Text(row,"Score","",240,8,103,27,21,true).alignment=TextAlignmentOptions.Right;
            Text(row,"Outcome","",57,60,115,15,9,true,Muted);
            Text(row,"Record","",178,60,164,15,9,true,Gold).alignment=TextAlignmentOptions.Right;
            d.RowTemplate=row.gameObject;row.gameObject.SetActive(false);
            d.Empty=Text(d.History,"Empty","",8,4,342,78,16,true,Muted);d.Empty.alignment=TextAlignmentOptions.Center;d.Empty.textWrappingMode=TextWrappingModes.Normal;
            d.MoreHistory=Button(d.History,"MoreHistory","SHOW MORE",0,92,358,44);
            d.TrainingIcon=Icon("dumbbell");d.AssessmentIcon=Icon("shield");d.WinIcon=Icon("trophy");d.LossIcon=Icon("bronze");
            d.SelectedPlate=Plate("selected");d.IdlePlate=Plate("idle");
            d.PreviewNote=null;d.AthleteDesign=true;
            d.Shell=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MainShellView>(true)).First();
            foreach(var b in d.GetComponentsInChildren<Button>(true))b.navigation=new Navigation{mode=Navigation.Mode.None};
            d.Refresh();d.SelectFilter(0);art.parent.GetComponent<DashboardWidthFit>().Fit();
            identity.SendMessage("RefreshAvatar",SendMessageOptions.DontRequireReceiver);
            identity.RefreshNameEditPosition();
            EditorUtility.SetDirty(d);EditorUtility.SetDirty(identity);AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            Debug.Log("[Profile] Compact layout and proportional background saved.");
        }

        static void BuildAchievements(Transform page,ProfileDashboard d,ProfileIdentityEditor identity)
        {
            var view=d.GetComponent<ProfileAchievements>();
            if(view==null)view=Undo.AddComponent<ProfileAchievements>(d.gameObject);
            Undo.RecordObject(view,"Achievement collection");
            if(view.Collection!=null)Undo.DestroyObjectImmediate(view.Collection);
            d.Achievements=view;
            var catalog=ProfileAchievementCatalog.Entries;
            var bronze=AssetDatabase.LoadAllAssetsAtPath(ArtPath+"achievements-bronze.png").OfType<Sprite>().ToArray();
            var silver=AssetDatabase.LoadAllAssetsAtPath(ArtPath+"achievements-silver.png").OfType<Sprite>().ToArray();
            view.Bronze=catalog.Select(e=>bronze.First(s=>s.name==e.Id)).ToArray();
            view.Silver=catalog.Select(e=>silver.First(s=>s.name==e.Id)).ToArray();
            const string libraryPath="Assets/_Project/Resources/ProfileAchievementArt.asset";
            var library=AssetDatabase.LoadAssetAtPath<ProfileAchievementArt>(libraryPath);
            if(library==null){library=ScriptableObject.CreateInstance<ProfileAchievementArt>();AssetDatabase.CreateAsset(library,libraryPath);}
            library.Silver=view.Silver;EditorUtility.SetDirty(library);
            var ribbon=Rect(page,"IdentityRibbon",104,70,222,38);
            BuildCountryPicker(d,identity,ribbon);
            view.HeaderBadges=new Image[3];view.HeaderBadgeButtons=new Button[3];
            for(int i=0;i<3;i++)
            {
                var badgeHit=Flat(ribbon,"TopAchievement"+i,46+i*44,0,44,38,Color.clear);badgeHit.raycastTarget=true;
                view.HeaderBadgeButtons[i]=badgeHit.gameObject.AddComponent<Button>();view.HeaderBadgeButtons[i].targetGraphic=badgeHit;
                view.HeaderBadges[i]=Pic(badgeHit.transform,"Medal",null,9,2,26,32);
            }
            view.ProfileScroll=d.transform.Find("ProfileScroll").GetComponent<ScrollRect>();
            var shelf=Flat(page,"AchievementShelf",16,469,358,310,new Color32(3,11,78,250)).transform;
            Flat(shelf,"Header",8,7,342,31,new Color32(7,19,98,255));
            Text(shelf,"Title","MILESTONES",14,10,252,24,18,true);
            d.AchievementCount=Text(shelf,"AchievementCount","",271,12,72,21,14,true);
            d.AchievementCount.alignment=TextAlignmentOptions.Right;view.ProfileCount=d.AchievementCount;
            d.MilestoneIcons=Array.Empty<Graphic>();d.MilestoneValues=Array.Empty<TextMeshProUGUI>();d.MilestoneFills=Array.Empty<Image>();
            view.Featured=new ProfileAchievements.Tile[8];
            for(int i=0;i<8;i++)view.Featured[i]=AchievementTile(shelf,i,8+(i%4)*87,45+(i/4)*120,view.Bronze[i]);
            view.AllButton=Button(shelf,"AllAchievements","SEE MORE",118,291,122,38);
            var moreRect=(RectTransform)view.AllButton.transform;
            moreRect.anchorMin=moreRect.anchorMax=new Vector2(.5f,0);
            moreRect.pivot=new Vector2(.5f,.5f);moreRect.anchoredPosition=Vector2.zero;
            var moreLabel=view.AllButton.GetComponentInChildren<TextMeshProUGUI>();
            Style(moreLabel,16,true,Color.white);moreLabel.fontSharedMaterial=_title;moreLabel.alignment=TextAlignmentOptions.Center;

            var overlay=Flat(identity.ModalParent,"ProfileAchievementCollection",0,0,0,0,new Color32(3,9,55,255));
            Stretch(overlay.rectTransform,Vector2.zero,Vector2.zero);overlay.raycastTarget=true;
            view.Collection=overlay.gameObject;
            var safe=Rect(overlay.transform,"SafeArea",0,0,0,0);Stretch(safe,Vector2.zero,Vector2.zero);
            safe.gameObject.AddComponent<SafeAreaFitter>();
            var design=Rect(safe,"CollectionDesign",0,0,390,844);
            design.anchorMin=design.anchorMax=design.pivot=new Vector2(.5f,1);design.anchoredPosition=Vector2.zero;
            var fitter=safe.gameObject.AddComponent<AchievementCollectionFit>();fitter.Design=design;fitter.Fit();
            Flat(design,"Header",0,0,390,82,new Color32(12,27,113,255));
            var back=Pic(design,"Back",S("ProfileSettings/Group 549"),14,18,57,44);back.raycastTarget=true;
            view.CloseButton=back.gameObject.AddComponent<Button>();view.CloseButton.targetGraphic=back;
            Text(design,"Title","MILESTONES",106,13,272,30,23,true);
            view.CollectionCount=Text(design,"CollectionCount","",108,47,261,21,12,true,Muted);
            Text(design,"Hint","Tap a medal to see its goal.",16,91,358,21,12,false,Muted).alignment=TextAlignmentOptions.Center;
            var viewport=Rect(design,"CollectionViewport",0,0,0,0);Stretch(viewport,new Vector2(16,172),new Vector2(-16,-122));
            var hit=viewport.gameObject.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;
            viewport.gameObject.AddComponent<RectMask2D>();
            view.CollectionScroll=viewport.gameObject.AddComponent<ScrollRect>();
            view.CollectionScroll.viewport=viewport;view.CollectionScroll.horizontal=false;
            view.CollectionScroll.movementType=ScrollRect.MovementType.Clamped;view.CollectionScroll.scrollSensitivity=25;
            var content=Rect(viewport,"Medals",0,0,358,Mathf.CeilToInt(catalog.Length/4f)*122);
            view.CollectionScroll.content=content;
            view.CollectionTiles=new ProfileAchievements.Tile[catalog.Length];
            for(int i=0;i<catalog.Length;i++)view.CollectionTiles[i]=AchievementTile(content,i,5+(i%4)*87,(i/4)*122,view.Bronze[i]);
            var detail=Flat(design,"AchievementDetail",16,0,358,146,new Color32(15,29,106,255));
            detail.rectTransform.anchorMin=detail.rectTransform.anchorMax=detail.rectTransform.pivot=new Vector2(0,0);
            detail.rectTransform.anchoredPosition=new Vector2(16,14);
            view.DetailIcon=Pic(detail.transform,"Medal",view.Bronze[0],10,23,69,82);
            view.DetailTitle=Text(detail.transform,"DetailTitle","",89,10,257,26,17,true);
            view.DetailDescription=Text(detail.transform,"Condition","",89,40,257,47,12,false,Muted);
            view.DetailDescription.textWrappingMode=TextWrappingModes.Normal;
            view.DetailDescription.fontSizeMin=10;
            view.DetailProgress=Text(detail.transform,"Progress","",89,90,253,19,13,true);
            Rail(detail.transform,"Track",89,116,253,5,new Color32(4,13,64,255));
            view.DetailFill=Rail(detail.transform,"Fill",89,116,253,5,new Color32(163,187,255,255),true);
            view.DetailState=Text(detail.transform,"Status","",89,126,253,15,9,true,Muted);
            foreach(var button in overlay.GetComponentsInChildren<Button>(true))button.navigation=new Navigation{mode=Navigation.Mode.None};
            overlay.gameObject.SetActive(false);EditorUtility.SetDirty(view);
        }
        static ProfileAchievements.Tile AchievementTile(Transform parent,int index,float x,float y,Sprite sprite)
        {
            var root=Flat(parent,"Achievement_"+index,x,y,81,115,Color.clear);root.raycastTarget=true;
            var tile=new ProfileAchievements.Tile();tile.Button=root.gameObject.AddComponent<Button>();tile.Button.targetGraphic=root;
            tile.Selection=Flat(root.transform,"Selection",2,2,77,108,new Color32(111,145,246,48));tile.Selection.enabled=false;
            tile.Icon=Pic(root.transform,"Medal",sprite,10,0,61,74);
            tile.Title=Text(root.transform,"Caption","",0,76,81,25,10,true,new Color32(220,226,249,255));
            tile.Title.textWrappingMode=TextWrappingModes.Normal;tile.Title.alignment=TextAlignmentOptions.Center;
            tile.Title.fontSizeMin=9;
            tile.Progress=Text(root.transform,"Progress","",0,102,81,13,8,true,Muted);tile.Progress.alignment=TextAlignmentOptions.Center;
            return tile;
        }
        static void Stretch(RectTransform rect,Vector2 min,Vector2 max)
        {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=min;rect.offsetMax=max;}
        static TMP_FontAsset Font(string name)=>AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/"+name+" TMP.asset");
        static Sprite S(string name)=>AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/"+name+".png");
        static Sprite Icon(string name)=>AssetDatabase.LoadAllAssetsAtPath(ArtPath+"icons.png").OfType<Sprite>().First(s=>s.name==name);
        static Sprite Character(string name)=>AssetDatabase.LoadAllAssetsAtPath(ArtPath+"characters.png").OfType<Sprite>().First(s=>s.name==name);
        static Sprite Plate(string name)=>AssetDatabase.LoadAllAssetsAtPath(ArtPath+"plates.png").OfType<Sprite>().First(s=>s.name==name);
        static Sprite Background()=>AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath+"background.png");
        static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
        {var go=new GameObject(name,typeof(RectTransform));Undo.RegisterCreatedObjectUndo(go,"Reference profile");go.layer=5;go.transform.SetParent(parent,false);var rt=(RectTransform)go.transform;Place(rt,x,y,w,h);return rt;}
        static void Place(RectTransform rt,float x,float y,float w,float h)
        {Undo.RecordObject(rt,"Profile layout");rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(0,1);rt.localScale=Vector3.one;rt.anchoredPosition=new Vector2(x,-y);rt.sizeDelta=new Vector2(w,h);}
        static Image Pic(Transform parent,string name,Sprite sprite,float x,float y,float w,float h)
        {var image=Rect(parent,name,x,y,w,h).gameObject.AddComponent<Image>();image.sprite=sprite;image.preserveAspect=true;image.raycastTarget=false;return image;}
        static Image Flat(Transform p,string name,float x,float y,float w,float h,Color color)
        {var image=Pic(p,name,null,x,y,w,h);image.color=color;return image;}
        static Image Rail(Transform p,string name,float x,float y,float w,float h,Color color,bool fill=false)
        {var image=Flat(p,name,x,y,w,h,color);if(fill){image.sprite=_solid;image.preserveAspect=false;image.type=Image.Type.Filled;image.fillMethod=Image.FillMethod.Horizontal;}return image;}
        static Button Button(Transform p,string name,string caption,float x,float y,float w,float h)
        {var image=Pic(p,name,Plate("action"),x,y,w,h);image.preserveAspect=false;image.type=Image.Type.Sliced;image.pixelsPerUnitMultiplier=5;image.raycastTarget=true;var button=image.gameObject.AddComponent<Button>();button.targetGraphic=image;Text(image.transform,"Label",caption,4,0,w-8,h-4,12,true).alignment=TextAlignmentOptions.Center;return button;}
        static void EditGear(Button button)
        {button.image.color=Color.clear;foreach(var c in button.transform.Cast<Transform>().ToArray())Undo.DestroyObjectImmediate(c.gameObject);Pic(button.transform,"EditGear",S("ProfileSettings/Group 553"),10,10,24,24);}
        public static void SettingsGear(Button button)
        {
            Place((RectTransform)button.transform,320,24,57,44);
            Undo.RecordObject(button.image,"Settings button plate");
            button.image.sprite=S("ProfileSettings/Group 637");button.image.color=Color.white;
            button.image.type=Image.Type.Simple;button.image.preserveAspect=true;button.image.raycastTarget=true;
            foreach(var child in button.transform.Cast<Transform>().ToArray())Undo.DestroyObjectImmediate(child.gameObject);
        }
        static TextMeshProUGUI Text(Transform p,string name,string value,float x,float y,float w,float h,float size,bool bold,Color? color=null)
        {var t=Rect(p,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();Style(t,size,bold,color??Color.white);t.text=value;return t;}
        static void Style(TextMeshProUGUI t,float size,bool bold,Color color)
        {t.font=bold?_black:_regular;t.fontSharedMaterial=bold?(size>=17?_title:_caption):_body;t.fontSize=t.fontSizeMax=size;t.fontSizeMin=size*.75f;t.enableAutoSizing=true;t.color=color;t.alignment=TextAlignmentOptions.MidlineLeft;t.textWrappingMode=TextWrappingModes.NoWrap;t.overflowMode=TextOverflowModes.Ellipsis;t.raycastTarget=false;}
        static Material Material(TMP_FontAsset font,string name,float outline,float dilate)
        {string path=ArtPath+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat==null){mat=new Material(font.material);AssetDatabase.CreateAsset(mat,path);}mat.SetFloat("_OutlineWidth",outline);mat.SetFloat("_FaceDilate",dilate);mat.SetColor("_OutlineColor",Color.black);mat.SetColor("_FaceColor",Color.white);mat.DisableKeyword("UNDERLAY_ON");mat.DisableKeyword("UNDERLAY_INNER");ShaderUtilities.UpdateShaderRatios(mat);EditorUtility.SetDirty(mat);return mat;}
        static Sprite SolidSprite()
        {
            string path=ArtPath+"ProgressPixel.asset";
            var existing=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();if(existing!=null)return existing;
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name="ProgressPixel",filterMode=FilterMode.Point};
            texture.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white});texture.Apply();AssetDatabase.CreateAsset(texture,path);
            var sprite=Sprite.Create(texture,new Rect(0,0,2,2),new Vector2(.5f,.5f),100);sprite.name="Solid";AssetDatabase.AddObjectToAsset(sprite,texture);AssetDatabase.SaveAssets();return sprite;
        }
    }
}

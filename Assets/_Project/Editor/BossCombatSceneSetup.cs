using System;
using System.IO;
using System.Linq;
using PushStars.Fight;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    public static class BossCombatSceneSetup
    {
        private const string Sprites = "Assets/_Project/UI/Sprites/";
        [MenuItem("Tools/Push Stars/Boss/Install Health Screens")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            Directory.CreateDirectory("output/boss-combat");
            string dividerPath=Sprites+"BossMap/vs-divider.png";
            AssetDatabase.ImportAsset(dividerPath);
            var dividerImporter=(TextureImporter)AssetImporter.GetAtPath(dividerPath);
            if(dividerImporter.textureType!=TextureImporterType.Sprite)
            {
                dividerImporter.textureType=TextureImporterType.Sprite;dividerImporter.spriteImportMode=SpriteImportMode.Single;
                dividerImporter.mipmapEnabled=false;dividerImporter.alphaIsTransparency=true;dividerImporter.SaveAndReimport();
            }
            foreach (bool prep in new[] { true, false })
            {
                string path = "Assets/_Project/Scenes/" + (prep ? "FightPreparation" : "Fight") + ".unity";
                var scene = SceneManager.GetSceneByPath(path); bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                File.Copy(path, "output/boss-combat/" + (prep ? "Preparation" : "Fight") + "-before-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".unity");
                Build(scene, prep);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("output/boss-combat/install.txt", "Boss preparation and battle health layouts installed; legacy PVP and solo layouts retained.\n");
        }

        private static T Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).First();
        private static UnityEngine.Object Ref(Component component, string field) => new SerializedObject(component).FindProperty(field).objectReferenceValue;

        private static void Build(Scene scene, bool prep)
        {
            Component host = prep ? Find<DuelReadyPanel>(scene) : Find<FightHud>(scene);
            var canvas = host.GetComponent<Canvas>();
            var c = host.GetComponent<BossCombatScreen>(); if (c == null) c = host.gameObject.AddComponent<BossCombatScreen>();
            c.Preparation = prep;
            var root = Rect(canvas.transform, "BossCombatLayout", 0, 0, 0, 0); Stretch(root);
            c.Root = root.gameObject;
            var bg = Rect(root, "Backdrop", 0, 0, 0, 0); Stretch(bg);
            var graphic = Get<BossBackdropGraphic>(bg); graphic.Preparation = prep; graphic.raycastTarget = false; graphic.SetVerticesDirty();
            var content = Rect(root, "Content", 0, 0, 390, 844); c.Content = content;
            for (int row = 0; row < 7; row++) for (int col = 0; col < 4; col++)
            {
                var bolt = Picture(content, "Pattern" + row + "_" + col, "icon_lightning_BG", -180 + col * 115 + (row % 2) * 35, -360 + row * 135, 82, 106);
                bolt.color = new Color(1, 1, 1, .12f); bolt.rectTransform.localRotation = Quaternion.Euler(0, 0, -12);
            }
            var avatars = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FightAvatar>(true)).ToArray();
            c.BossStage = avatars.Single(a => new SerializedObject(a).FindProperty("_opponentStage").boolValue);
            c.PlayerStage = avatars.Single(a => a != c.BossStage);
            if (prep)
            {
                c.Legacy = new[] { ((DuelReadyPanel)host).Root };
                c.SourcePlayerName = (TMP_Text)Ref(host, "_playerName");
            }
            else
            {
                var playerHalf = (RectTransform)Ref(host, "_playerHalf"); var opponentHalf = (GameObject)Ref(host, "_opponentHalf");
                c.Legacy = new[] { (GameObject)Ref(host,"_playerPanel"), (GameObject)Ref(host,"_opponentPanel"), (GameObject)Ref(host,"_timerPlate"),
                    playerHalf.GetComponentInChildren<RawImage>(true).gameObject, opponentHalf.GetComponentInChildren<RawImage>(true).gameObject };
                c.SourcePlayerName = (TMP_Text)Ref(host,"_playerName");
                c.Guidance = ((GameObject)Ref(host,"_bannerRoot")).GetComponent<RectTransform>();
                c.GuidanceText = (TMP_Text)Ref(host,"_bannerText");
                c.Countdown = (TMP_Text)Ref(host,"_countdown");
            }
            c.BossPortrait = Raw(content, "BossPortrait", prep ? 104 : 15, prep ? 179 : 153, prep ? 205 : 252, prep ? 312 : 276);
            c.PlayerPortrait = Raw(content, "PlayerPortrait", prep ? -105 : 0, prep ? -213 : -177, prep ? 205 : 324, prep ? 290 : 280);
            if (!prep)
            {
                var shadow = Picture(content,"BossShadow","ground_shadow",15,10,155,20); shadow.color = new Color(0,0,0,.45f); shadow.transform.SetSiblingIndex(c.BossPortrait.transform.GetSiblingIndex());
                var playerShadow = Picture(content,"PlayerShadow","ground_shadow",0,-305,190,20); playerShadow.color = new Color(0,0,0,.45f); playerShadow.transform.SetSiblingIndex(c.PlayerPortrait.transform.GetSiblingIndex());
            }
            if (prep)
            {
                var divider=Picture(content,"VsDivider","BossMap/vs-divider",0,20,405,102);
                divider.transform.SetSiblingIndex(Mathf.Max(c.PlayerPortrait.transform.GetSiblingIndex(),c.BossPortrait.transform.GetSiblingIndex())+1);
                var vs=Picture(content, "VS", "VS_for_serching", 0, 18, 92, 92);
                vs.transform.SetSiblingIndex(divider.transform.GetSiblingIndex()+1);
            }
            c.BossName = Label(content, "BossName", prep ? -67 : -56, prep ? 331 : 359, prep ? 242 : 266, prep ? 78 : 30, prep ? 29 : 16, Color.yellow, true);
            c.BossName.text = prep ? "BOSS GOBLIN\nARCH" : "BOSS GOBLIN ARCH";
            c.PlayerName = Label(content,"PlayerName",prep ? 78 : -35,prep ? -102 : -46,prep ? 217 : 270,prep ? 65 : 28,prep ? 25 : 16,Color.yellow,true);
            c.PlayerName.text = "BEASTCORE_DEV";
            Hp(content,"BossHP",prep ? -84 : -60,prep ? 266 : 328,prep ? 190 : 246,out c.BossHpFill,out c.BossHpText);
            Picture(content,"BossIcon","BossMap/goblin",prep ? -177 : -179,prep ? 266 : 328,37,37);
            Hp(content,"PlayerHP",prep ? 90 : -60,prep ? -155 : -20,prep ? 187 : 246,out c.PlayerHpFill,out c.PlayerHpText);
            c.PlayerIcon = Raw(content,"PlayerIcon",prep ? -5 : -181,prep ? -155 : -20,40,42);
            c.Stars = Get<BossStarsGraphic>(Rect(content,"Stars",prep ? -120 : 92,prep ? 228 : 359,prep ? 124 : 86,prep ? 28 : 21)); c.Stars.raycastTarget = false;
            c.Stars.Filled = 1; c.Stars.SetVerticesDirty();
            if (prep)
            {
                Label(content,"BestCaption",112,-205,154,24,13,new Color(.55f,.56f,.73f)).text = "MAX PUSHUP";
                Picture(content,"PushupIcon","pushup_icon",91,-239,33,35);
                c.Best = Label(content,"Best",148,-237,71,46,37,Color.white); c.Best.text = "32";
                Label(content,"RewardCaption",127,-287,118,24,13,new Color(.55f,.56f,.73f)).text = "WIN BONUS";
                Picture(content,"RewardIcon","exp_lightning",104,-316,25,31);
                c.Reward = Label(content,"Reward",153,-315,90,28,21,Color.yellow); c.Reward.text = "+50";
            }
            else
            {
                c.Reps = Label(content,"Reps",135,-83,109,113,85,Color.yellow); c.Reps.text = "0";
                Label(content,"FormCaption",-142,-104,82,24,13,new Color(.65f,.67f,.87f)).text="FORM";
                c.Form=Label(content,"Form",-138,-134,90,40,34,Color.yellow); c.Form.text="—";
                Label(content,"TempoCaption",-142,-184,82,24,13,new Color(.65f,.67f,.87f)).text="TEMPO";
                c.Tempo=Label(content,"Tempo",-116,-216,132,43,32,Color.yellow); c.Tempo.text="—";
                c.Timer=Label(content,"Timer",145,302,76,25,16,new Color(1,1,1,.75f));c.Timer.text="1:00";
            }
            c.Action = Button(content,"Action",prep ? 17 : 0,-378,110,48,"READY"); c.ActionLabel = c.Action.GetComponentInChildren<TMP_Text>();
            c.Home = Button(content,"Home",154,403,69,25,"HOME");
            c.DamageLabels = new TMP_Text[6];
            for(int i=0;i<c.DamageLabels.Length;i++)
            {
                c.DamageLabels[i]=Label(content,"Damage"+i,126,245,120,68,45,new Color(1,.22f,.015f),true);
                c.DamageLabels[i].text="-100";c.DamageLabels[i].rectTransform.localRotation=Quaternion.Euler(0,0,10);
                c.DamageLabels[i].gameObject.SetActive(false);
            }
            if (!prep) ApplyBattlePlayerPolish(c);
            if (!prep && File.Exists(GoblinForestSetup.Folder + "background.png")) GoblinForestSetup.Apply(c);
            root.gameObject.SetActive(false); EditorUtility.SetDirty(c);
        }

        public static void ApplyBattlePlayerPolish(BossCombatScreen screen)
        {
            if (screen.Preparation) return;
            var portrait = screen.PlayerPortrait.rectTransform;
            portrait.anchoredPosition = new Vector2(0, -177);
            portrait.sizeDelta = new Vector2(324, 280);
            var shadow = screen.Content.Find("PlayerShadow") as RectTransform;
            if (shadow != null) shadow.anchoredPosition = new Vector2(0, -305);

            const string materialPath = Sprites + "BossMap/BattleRepsLabel.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Material>(Sprites + "BossMap/FightLabel.mat"));
                material.name = "BattleRepsLabel";
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetFloat("_OutlineWidth", .075f);
            material.SetFloat("_UnderlayDilate", .1f);
            material.SetFloat("_UnderlayOffsetY", -.25f);
            material.SetFloat("_UnderlayOffsetX", 0);
            material.SetColor("_UnderlayColor", new Color(0, 0, 0, .65f));
            screen.Reps.fontSharedMaterial = material;
            screen.Reps.UpdateMeshPadding();
            screen.Reps.SetAllDirty();
            EditorUtility.SetDirty(material);
            EditorUtility.SetDirty(screen);
        }

        private static T Get<T>(RectTransform rect) where T : Component => rect.GetComponent<T>() ?? rect.gameObject.AddComponent<T>();
        private static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
        {
            var r=parent.Find(name) as RectTransform;
            if(r==null){r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.gameObject.layer=5;}
            r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);return r;
        }
        private static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
        private static Image Picture(Transform p,string n,string sprite,float x,float y,float w,float h)
        {var i=Get<Image>(Rect(p,n,x,y,w,h));i.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Sprites+sprite+".png");i.preserveAspect=true;i.raycastTarget=false;return i;}
        private static RawImage Raw(Transform p,string n,float x,float y,float w,float h)
        {var i=Get<RawImage>(Rect(p,n,x,y,w,h));i.raycastTarget=false;return i;}
        private static TMP_Text Label(Transform p,string n,float x,float y,float w,float h,int size,Color color,bool italic=false)
        {
            var t=Get<TextMeshProUGUI>(Rect(p,n,x,y,w,h));
            t.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSetup.BoldAsset);
            t.fontSharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Sprites+"BossMap/FightLabel.mat");
            t.fontSize=size;t.color=color;t.fontStyle=italic?FontStyles.Italic:FontStyles.Normal;
            t.alignment=TextAlignmentOptions.Left;t.raycastTarget=false;t.overflowMode=TextOverflowModes.Overflow;return t;
        }
        private static Button Button(Transform p,string n,float x,float y,float w,float h,string text)
        {
            var i=Picture(p,n,"btn_start",x,y,w,h);i.raycastTarget=true;var b=Get<Button>(i.rectTransform);
            var label=Label(i.transform,"Label",0,0,w-8,h-4,n=="Home"?12:17,Color.white);label.text=text;label.alignment=TextAlignmentOptions.Center;
            return b;
        }
        private static void Hp(Transform p,string name,float x,float y,float width,out BossHealthBarGraphic fill,out TMP_Text number)
        {
            var border=Rect(p,name,x,y,width,30);
            var old=border.GetComponent<Image>();if(old)UnityEngine.Object.DestroyImmediate(old);
            foreach(string obsolete in new[]{"Track","Fill"}){var child=border.Find(obsolete);if(child)UnityEngine.Object.DestroyImmediate(child.gameObject);}
            fill=Get<BossHealthBarGraphic>(border);fill.FillAmount=1;fill.raycastTarget=false;
            number=Label(border,"Value",0,0,width-16,24,11,new Color(1,1,1,.86f));number.alignment=TextAlignmentOptions.Center;number.text="450 / 450";
        }
    }
}

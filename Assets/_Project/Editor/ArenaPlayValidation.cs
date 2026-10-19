using System;
using System.IO;
using System.Linq;
using PushStars.Core;
using PushStars.Fight;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace PushStars.Editor
{
    [InitializeOnLoad]
    public static class ArenaPlayValidation
    {
        private const string Key="PushStars.ArenaPlayQA",Report="output/arenas/play-validation.txt",Wallet="rewards.case_ledger.v1",Bought="colosseum";
        private static int _step; private static double _due,_deadline; private static string _error,_winner; private static long _gems;
        static ArenaPlayValidation()
        {
            EditorApplication.playModeStateChanged+=state=>
            {
                if(!SessionState.GetBool(Key,false))return;
                if(state==PlayModeStateChange.EnteredPlayMode)
                {
                    _step=0;_due=EditorApplication.timeSinceStartup+4;_deadline=_due+50;_error=null;
                    Application.runInBackground=true; Application.logMessageReceived+=Error;EditorApplication.update+=Tick;
                }
                if(state==PlayModeStateChange.EnteredEditMode)
                {
                    EditorApplication.update-=Tick;Application.logMessageReceived-=Error;
                    RestorePreference(ArenaProfile.SelectionKey,Key+".arena");RestorePreference("selected_game_mode",Key+".mode",true);
                    RestorePreference(Wallet,Key+".wallet");RestorePreference("arena.owned."+Bought,Key+".legacy",true);PlayerPrefs.Save();CaseRewards.ReloadFromSave();
                    var path=SessionState.GetString(Key+".start","");EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(path)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
                    Application.runInBackground=SessionState.GetBool(Key+".background",false);SessionState.EraseBool(Key);
                }
            };
        }
        [MenuItem("Tools/Push Stars/Arenas/Validate Play Flow")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play first.");
            Directory.CreateDirectory("output/arenas");File.WriteAllText(Report,"Arena Play validation; editor preview battle, camera and rewards excluded.\n");
            SavePreference(ArenaProfile.SelectionKey,Key+".arena");SavePreference("selected_game_mode",Key+".mode",true);
            // The run buys an arena with test gems; the real wallet is put back on exit.
            SavePreference(Wallet,Key+".wallet");SavePreference("arena.owned."+Bought,Key+".legacy",true);
            SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));SessionState.SetBool(Key+".background",Application.runInBackground);
            SelectedGameMode.Current=GameMode.Pvp;
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/_Project/Scenes/Main.unity");
            SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
        private static void SavePreference(string pref,string key,bool integer=false)
        {SessionState.SetBool(key+".had",PlayerPrefs.HasKey(pref));SessionState.SetString(key,integer?PlayerPrefs.GetInt(pref).ToString():PlayerPrefs.GetString(pref));}
        private static void RestorePreference(string pref,string key,bool integer=false)
        {if(!SessionState.GetBool(key+".had",false))PlayerPrefs.DeleteKey(pref);else if(integer)PlayerPrefs.SetInt(pref,int.Parse(SessionState.GetString(key,"0")));else PlayerPrefs.SetString(pref,SessionState.GetString(key,""));}
        private static void Error(string condition,string trace,LogType type) {if(type==LogType.Exception||type==LogType.Error)_error=condition;}
        private static void Tick()
        {
            if(EditorApplication.timeSinceStartup<_due)return;
            try
            {
                if(_error!=null)throw new Exception(_error);if(EditorApplication.timeSinceStartup>_deadline)throw new Exception("Timed out.");
                _due=EditorApplication.timeSinceStartup+.4;
                switch(_step++)
                {
                    case 0:
                        var home=Object.FindFirstObjectByType<AvatarCollectionScreen>(FindObjectsInactive.Include);Check(home!=null,"Avatar collection in real Main");
                        var shop=Object.FindFirstObjectByType<ShopScreen>(FindObjectsInactive.Include);Check(shop!=null,"Shop in real Main");
                        var arena=ArenaCatalog.Get(Bought);Check(arena.Access==ArenaAccess.Purchase&&arena.Price>0,"Bought arena is sold for gems");
                        Check(ArenaProfile.Owns(ArenaCatalog.DefaultId)&&!ArenaProfile.Owns(Bought),"Default arena free, paid arena locked");
                        Check(CaseRewards.TryCreditGems("qa:arena-play:"+Guid.NewGuid(),arena.Price),"Test gems credited");
                        _gems=CaseRewards.GemsBalance;
                        home.ShowArenas();Check(home.IsOpen&&home.ShowingArenas,"ARENAS tab opens");
                        Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name==Bought).onClick.Invoke();
                        Check(shop.IsOpen&&!ArenaProfile.Owns(Bought),"Locked arena card opens the shop");
                        var section=(RectTransform)shop.Content.Find("ARENAS");Check(section!=null,"Shop has an ARENAS section");
                        shop.Content.anchoredPosition=new Vector2(shop.Content.anchoredPosition.x,-section.anchoredPosition.y-12);_due+=1;break;
                    case 1:
                        shop=Object.FindFirstObjectByType<ShopScreen>(FindObjectsInactive.Include);
                        ArenaValidation.Capture(shop.GetComponentInParent<Canvas>().rootCanvas,shop.gameObject.scene,"play-shop-arenas",390,844);
                        Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name==Bought+"ArenaOffer").onClick.Invoke();_due+=.8;break;
                    case 2:
                        shop=Object.FindFirstObjectByType<ShopScreen>(FindObjectsInactive.Include);home=Object.FindFirstObjectByType<AvatarCollectionScreen>(FindObjectsInactive.Include);
                        var dialog=shop.transform.root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="ArenaDialog");Check(dialog.gameObject.activeSelf,"Shop card opens buy dialog");
                        var canvas=shop.GetComponentInParent<Canvas>().rootCanvas;
                        ArenaValidation.Capture(canvas,shop.gameObject.scene,"play-shop-buy",390,844);
                        var buy=dialog.Find("Panel/Action").GetComponent<Button>();buy.onClick.Invoke();
                        Check(ArenaProfile.Owns(Bought)&&CaseRewards.OwnsArena(Bought)&&CaseRewards.GemsBalance==_gems-ArenaCatalog.Get(Bought).Price,"BUY spends gems and unlocks in the ledger");
                        ArenaValidation.Capture(canvas,shop.gameObject.scene,"play-shop-bought",390,844);
                        buy.onClick.Invoke();Check(!shop.IsOpen&&home.IsOpen&&home.ShowingArenas,"After buying, OPEN ARENAS shows the ARENAS tab");
                        var card=Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name==Bought);card.onClick.Invoke();
                        Check(ArenaProfile.SelectedId==Bought,"Card tap selects and persists");
                        var mode=Object.FindFirstObjectByType<ModeSelectionController>();var image=(Image)new SerializedObject(mode).FindProperty("_homeBackground").objectReferenceValue;
                        Check(image.sprite==ArenaCatalog.Get(Bought).Home && image.color==Color.white,"Home follows selected arena without tint");
                        ArenaValidation.Capture(mode.GetComponentInParent<Canvas>().rootCanvas,mode.gameObject.scene,"play-collection",390,844);
                        home.Hide();ArenaValidation.Capture(mode.GetComponentInParent<Canvas>().rootCanvas,mode.gameObject.scene,"play-home",390,844);
                        FightRequest.Clear();ArenaMatch.BeginLocal("jungle","underwater");SceneManager.LoadScene("FightPreparation");_due+=2;break;
                    case 3:
                        ClickReady();_due=.2+EditorApplication.timeSinceStartup;break;
                    case 4:
                        var popup=Object.FindFirstObjectByType<ArenaSelectionPopup>();Check(popup!=null,"Different maps show roulette");
                        _winner=ArenaMatch.WinnerId;Check(_winner=="jungle"||_winner=="underwater","Winner is one candidate");
                        ArenaValidation.Capture(popup.GetComponentInParent<Canvas>().rootCanvas,popup.gameObject.scene,"play-roulette",390,844);
                        _due+=3;break;
                    case 5:
                        Check(SceneManager.GetActiveScene().name=="Fight","READY loads battle after roulette");
                        Check(ArenaMatch.WinnerId==_winner && ArenaMatch.PresentationComplete,"Winner survives scene load");
                        var graphic=Object.FindFirstObjectByType<ArenaBattleGraphic>();Check(graphic!=null && graphic.mainTexture==ArenaCatalog.Get(_winner).Home.texture,"Battle uses winning arena");
                        ArenaValidation.Capture(graphic.GetComponentInParent<Canvas>().rootCanvas,graphic.gameObject.scene,"play-battle",390,844);
                        ArenaMatch.BeginLocal("ice-temple","ice-temple");SceneManager.LoadScene("FightPreparation");_due+=2;break;
                    case 6: ClickReady();_due+=1;break;
                    case 7:
                        Check(SceneManager.GetActiveScene().name=="Fight" && Object.FindFirstObjectByType<ArenaSelectionPopup>()==null,"Same map skips roulette");
                        Check(ArenaMatch.WinnerId=="ice-temple","Same-map battle preserved");Finish("PASS");break;
                }
            }
            catch(Exception e){Finish("FAIL: "+e);}
        }
        private static void ClickReady()
        {
            var panel=Object.FindFirstObjectByType<DuelReadyPanel>();Check(panel!=null,"Preparation loaded");
            var ready=(Button)new SerializedObject(panel).FindProperty("_readyButton").objectReferenceValue;ready.onClick.Invoke();
        }
        private static void Check(bool ok,string message){if(!ok)throw new Exception(message);File.AppendAllText(Report,"OK: "+message+"\n");}
        private static void Finish(string text){EditorApplication.update-=Tick;Application.logMessageReceived-=Error;File.AppendAllText(Report,text+"\n");EditorApplication.isPlaying=false;}
    }
}

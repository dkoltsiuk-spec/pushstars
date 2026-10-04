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
        private const string Key="PushStars.ArenaPlayQA",Report="output/arenas/play-validation.txt";
        private static int _step; private static double _due,_deadline; private static string _error,_winner;
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
                    RestorePreference(ArenaProfile.SelectionKey,Key+".arena");RestorePreference("selected_game_mode",Key+".mode",true);PlayerPrefs.Save();
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
                        var home=Object.FindFirstObjectByType<ArenaHomeController>();Check(home!=null,"MAPS entry created in real Main");
                        home.Show();Check(home.IsOpen,"Collection opens");
                        var card=Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name=="jungle");card.onClick.Invoke();
                        Check(ArenaProfile.SelectedId=="jungle","Card tap selects and persists");
                        var mode=Object.FindFirstObjectByType<ModeSelectionController>();var image=(Image)new SerializedObject(mode).FindProperty("_homeBackground").objectReferenceValue;
                        Check(image.sprite==ArenaCatalog.Get("jungle").Home && image.color==Color.white,"Home follows selected arena without tint");
                        ArenaValidation.Capture(mode.GetComponentInParent<Canvas>().rootCanvas,mode.gameObject.scene,"play-collection",390,844);
                        home.Hide();ArenaValidation.Capture(mode.GetComponentInParent<Canvas>().rootCanvas,mode.gameObject.scene,"play-home",390,844);
                        FightRequest.Clear();ArenaMatch.BeginLocal("jungle","underwater");SceneManager.LoadScene("FightPreparation");_due+=2;break;
                    case 1:
                        ClickReady();_due=.2+EditorApplication.timeSinceStartup;break;
                    case 2:
                        var popup=Object.FindFirstObjectByType<ArenaSelectionPopup>();Check(popup!=null,"Different maps show roulette");
                        _winner=ArenaMatch.WinnerId;Check(_winner=="jungle"||_winner=="underwater","Winner is one candidate");
                        ArenaValidation.Capture(popup.GetComponentInParent<Canvas>().rootCanvas,popup.gameObject.scene,"play-roulette",390,844);
                        _due+=3;break;
                    case 3:
                        Check(SceneManager.GetActiveScene().name=="Fight","READY loads battle after roulette");
                        Check(ArenaMatch.WinnerId==_winner && ArenaMatch.PresentationComplete,"Winner survives scene load");
                        var graphic=Object.FindFirstObjectByType<ArenaBattleGraphic>();Check(graphic!=null && graphic.mainTexture==ArenaCatalog.Get(_winner).Home.texture,"Battle uses winning arena");
                        ArenaValidation.Capture(graphic.GetComponentInParent<Canvas>().rootCanvas,graphic.gameObject.scene,"play-battle",390,844);
                        ArenaMatch.BeginLocal("ice-temple","ice-temple");SceneManager.LoadScene("FightPreparation");_due+=2;break;
                    case 4: ClickReady();_due+=1;break;
                    case 5:
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

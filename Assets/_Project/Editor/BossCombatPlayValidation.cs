using System;
using System.IO;
using System.Reflection;
using PushStars.Core;
using PushStars.CV;
using PushStars.Fight;
using PushStars.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PushStars.Editor
{
    /// <summary>Runs real UI/controller wiring with tracking disabled and rewards suppressed.</summary>
    [InitializeOnLoad]
    public static class BossCombatPlayValidation
    {
        private const string Key = "PushStars.BossCombatPlayTest";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int _step;
        private static double _due;
        private static bool _background;
        static BossCombatPlayValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Key,false)) return;
                if (state==PlayModeStateChange.EnteredPlayMode)
                {
                    _background=Application.runInBackground;Application.runInBackground=true;
                    _step=0;_due=EditorApplication.timeSinceStartup+1.5;EditorApplication.update+=Tick;
                }
                if (state==PlayModeStateChange.EnteredEditMode)
                {
                    foreach(string pref in new[]{"boss_progress","selected_game_mode"})
                    {
                        if(SessionState.GetBool(Key+pref+".had",false))PlayerPrefs.SetInt(pref,SessionState.GetInt(Key+pref,0));
                        else PlayerPrefs.DeleteKey(pref);
                    }
                    PlayerPrefs.Save();SessionState.SetBool(Key,false);
                }
            };
        }
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path!=AuthoredScenes.MainPath)
                throw new InvalidOperationException("Run from authored Main outside Play mode.");
            Directory.CreateDirectory("output/boss-combat");
            foreach(string pref in new[]{"boss_progress","selected_game_mode"})
            {SessionState.SetBool(Key+pref+".had",PlayerPrefs.HasKey(pref));SessionState.SetInt(Key+pref,PlayerPrefs.GetInt(pref));}
            PlayerPrefs.SetInt("boss_progress",0);SelectedGameMode.Current=GameMode.Boss;
            SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
        private static void ArmAfterAwake(Scene scene,LoadSceneMode mode)
        {
            if(scene.name!="Fight")return;
            SceneManager.sceneLoaded-=ArmAfterAwake;
            // FightController.Awake sees no request and disables tracking BEFORE any CV Start.
            // sceneLoaded precedes Start, so Start still configures real boss HP and UI.
            FightRequest.Boss();
        }
        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if(EditorApplication.timeSinceStartup<_due)return;
            _due=EditorApplication.timeSinceStartup+1.5;
            try
            {
                switch(_step++)
                {
                    case 0: FightRequest.Boss();FightScreenNavigation.Navigate(FightScreen.Preparation);break;
                    case 1:
                    {
                        var screen=UnityEngine.Object.FindFirstObjectByType<BossCombatScreen>();
                        Require(screen && screen.Active && screen.Preparation,"New boss preparation is active");
                        Require(screen.BossHpText.text=="450 / 450","Preparation HP");
                        screen.Home.onClick.Invoke();break;
                    }
                    case 2:
                    {
                        Require(SceneManager.GetActiveScene().path==AuthoredScenes.MainPath,"HOME uses current Main");
                        var map=UnityEngine.Object.FindFirstObjectByType<BossMapController>();
                        Require(map && map.Home.activeInHierarchy,"Boss island restored");
                        FightRequest.Clear();SceneManager.sceneLoaded+=ArmAfterAwake;SceneManager.LoadScene("Fight");break;
                    }
                    case 3:
                    {
                        var screen=UnityEngine.Object.FindFirstObjectByType<BossCombatScreen>();
                        var fight=UnityEngine.Object.FindFirstObjectByType<FightController>();
                        var session=UnityEngine.Object.FindFirstObjectByType<PushupSession>();
                        Require(screen && screen.Active && !screen.Preparation && fight.BossHealth!=null,"Real boss battle bindings");
                        Require(session && !session.enabled,"Tracking/session stays disabled for simulation");
                        foreach(var source in session.GetComponents<MonoBehaviour>())
                            if(source is IPoseSource)Require(!source.enabled,"No live pose source");
                        Require(!fight.BossReady,"READY gates fight start");screen.Action.onClick.Invoke();
                        Require(fight.BossReady,"READY click accepted");
                        Require(screen.Countdown && screen.Countdown.transform.IsChildOf(screen.Content),"Countdown is above boss layout");
                        var type=typeof(FightController);
                        // Do not let this simulation award XP, alter progression or navigate Results.
                        type.GetField("_screenPreview",Private).SetValue(fight,true);
                        var phase=type.GetField("_phase",Private);phase.SetValue(fight,Enum.Parse(phase.FieldType,"Live"));
                        type.GetField("_liveStartTime",Private).SetValue(fight,Time.time);
                        var accepted=type.GetMethod("HandleRep",Private);
                        for(int i=1;i<=7;i++)accepted.Invoke(fight,new object[]{i});
                        Require(fight.BossHealth.BossHp==0 && fight.PlayerBattleReps==7,"Accepted reps damage boss and reach KO");
                        accepted.Invoke(fight,new object[]{8});
                        Require(fight.PlayerBattleReps==7,"Extra rep after KO is ignored");
                        break;
                    }
                    case 4:
                    {
                        var screen=UnityEngine.Object.FindFirstObjectByType<BossCombatScreen>();
                        Require(screen.BossHpText.text=="0 / 450" && screen.BossHpFill.FillAmount==0,"Zero HP displayed and fill empty");
                        if (screen.Forest != null && screen.Forest.Effects != null)
                        {
                            Require(screen.Forest.Effects.HitBursts == 7, "Real accepted reps emit seven hit bursts");
                            Require(screen.Forest.Effects.FallBursts == 1, "Update emits one timed ground burst on KO");
                        }
                        Require(PlayerPrefs.GetInt("boss_progress")==0,"Simulation did not grant progression");
                        FightRequest.Clear();SceneManager.LoadScene("Main");break;
                    }
                    case 5:
                        File.WriteAllText("output/boss-combat/play-validation.txt","PASS: real preparation with 450 HP; new HOME returns to current boss island; real battle READY gate and countdown layer; seven accepted reps reach KO, extra rep ignored, HP UI reaches zero; CV disabled, no reward/progression side effects.\n");
                        Finish();break;
                }
            }
            catch(Exception e){File.WriteAllText("output/boss-combat/play-validation.txt","FAIL: "+e);Finish();}
        }
        private static void Finish()
        {SceneManager.sceneLoaded-=ArmAfterAwake;Application.runInBackground=_background;EditorApplication.update-=Tick;EditorApplication.isPlaying=false;}
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}

using System;
using System.Linq;
using PushStars.Core;
using PushStars.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    public sealed partial class DuelReadyPanel
    {
        private string[] _playerAchievementIds=Array.Empty<string>(), _opponentAchievementIds=Array.Empty<string>();
        [SerializeField,HideInInspector] private Image[] _playerMedals, _opponentMedals;

        private void ResolveIdentity(in Side player,in Side opponent,bool loadLocalProfile)
        {
            _playerAchievementIds=player.AchievementIds??Array.Empty<string>();
            _opponentAchievementIds=opponent.AchievementIds??Array.Empty<string>();
            if(!string.IsNullOrEmpty(player.CountryCode))_playerFlagSprite=CountryFlags.Get(player.CountryCode);
            if(!string.IsNullOrEmpty(opponent.CountryCode))_opponentFlagSprite=CountryFlags.Get(opponent.CountryCode);
            if(!loadLocalProfile || !Application.isPlaying)return;
            _playerFlagSprite=CountryFlags.Get(ProfileCountry.Code);
            var progress=ProfileAchievementCatalog.Progress(LocalProfile.Profile,LocalProfile.History,LocalProfile.BestReps,DateTime.Today);
            _playerAchievementIds=ProfileAchievementCatalog.BestEarned(progress).Select(i=>ProfileAchievementCatalog.Entries[i].Id).ToArray();
        }
        private static void PlaceIdentityStrip(TextMeshProUGUI name,Image flag,string[] achievementIds,ref Image[] medals)
        {
            if(name==null)return;
            var art=Resources.Load<ProfileAchievementArt>("ProfileAchievementArt");
            var sprites=achievementIds.Distinct().Select(id=>art!=null?art.ForId(id):null).Where(s=>s!=null).Take(3).ToArray();
            if(medals==null || medals.Length!=3)medals=new Image[3];
            for(int i=0;i<3;i++)
            {
                if(medals[i]==null)
                {
                    var existing=name.transform.Find("IdentityMedal"+i);
                    medals[i]=existing!=null?existing.GetComponent<Image>():new GameObject("IdentityMedal"+i,typeof(RectTransform),typeof(Image)).GetComponent<Image>();
                    medals[i].transform.SetParent(name.transform,false);medals[i].gameObject.layer=name.gameObject.layer;
                    medals[i].raycastTarget=false;medals[i].preserveAspect=true;
                }
                medals[i].gameObject.SetActive(i<sprites.Length);
                if(i<sprites.Length)medals[i].sprite=sprites[i];
            }
            name.ForceMeshUpdate();if(name.textInfo.lineCount==0)return;
            var line=name.textInfo.lineInfo[name.textInfo.lineCount-1];
            float width=(flag!=null && flag.enabled?32:0)+sprites.Length*30;
            float x=name.alignment==TextAlignmentOptions.TopRight?name.rectTransform.rect.xMax-width:name.rectTransform.rect.xMin;
            float y=line.descender-14;
            if(flag!=null && flag.enabled){Position(flag,new Vector2(x,y),new Vector2(26,18));x+=32;}
            for(int i=0;i<sprites.Length;i++)Position(medals[i],new Vector2(x+i*30,y),new Vector2(22,27));
            void Position(Image image,Vector2 position,Vector2 size)
            {
                var rect=image.rectTransform;rect.SetParent(name.transform,false);
                rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(0,.5f);
                rect.anchoredPosition=position;rect.sizeDelta=size;
            }
        }
    }
}

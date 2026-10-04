using System;
using System.Collections.Generic;
using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    public sealed class ProfileAchievements : MonoBehaviour
    {
        [Serializable]
        public sealed class Tile
        {
            public Image Icon, Selection;
            public TextMeshProUGUI Title, Progress;
            public Button Button;
        }
        public Tile[] Featured, CollectionTiles;
        public Sprite[] Bronze, Silver;
        public TextMeshProUGUI ProfileCount, CollectionCount;
        public Button AllButton, CloseButton;
        public GameObject Collection;
        public ScrollRect CollectionScroll, ProfileScroll;
        public Image DetailIcon, DetailFill;
        public Image[] HeaderBadges;
        public Button[] HeaderBadgeButtons;
        public TextMeshProUGUI DetailTitle, DetailDescription, DetailProgress, DetailState;
        private long[] _values;
        private int _selected;
        private bool _profileScrollWasEnabled;
        private int[] _headerIndices=Array.Empty<int>();

        private void Awake()
        {
            AllButton.onClick.AddListener(Open);
            CloseButton.onClick.AddListener(Close);
            for(int i=0;i<Featured.Length;i++) { int index=i; Featured[i].Button.onClick.AddListener(()=>OpenAt(index)); }
            for(int i=0;i<CollectionTiles.Length;i++) { int index=i; CollectionTiles[i].Button.onClick.AddListener(()=>Select(index)); }
            for(int i=0;i<HeaderBadgeButtons.Length;i++) {int slot=i;HeaderBadgeButtons[i].onClick.AddListener(()=>{if(slot<_headerIndices.Length)OpenAt(_headerIndices[slot]);});}
        }
        private void OnDisable() { Close(); }
        private void Update() { if(Collection!=null && Collection.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Close(); }
        private void OnDestroy() { if(Application.isPlaying && Collection!=null) Destroy(Collection); }

        public void Bind(UserProfile profile, IReadOnlyList<MatchRecord> history, int best, DateTime today)
        {
            _values=ProfileAchievementCatalog.Progress(profile,history,best,today);
            int unlocked=0;
            for(int i=0;i<_values.Length;i++)
            {
                if(Earned(i)) unlocked++;
                if(i<Featured.Length) Draw(Featured[i],i);
                Draw(CollectionTiles[i],i);
            }
            ProfileCount.text=unlocked+" / "+_values.Length;
            CollectionCount.text=unlocked+" / "+_values.Length+" UNLOCKED";
            _headerIndices=ProfileAchievementCatalog.BestEarned(_values);
            for(int i=0;i<HeaderBadges.Length;i++)
            {
                HeaderBadgeButtons[i].gameObject.SetActive(i<_headerIndices.Length);
                if(i<_headerIndices.Length)HeaderBadges[i].sprite=Silver[_headerIndices[i]];
            }
            Select(Mathf.Clamp(_selected,0,_values.Length-1));
        }
        private bool Earned(int i)=>_values[i]>=ProfileAchievementCatalog.Entries[i].Goal;
        private void Draw(Tile tile,int i)
        {
            var entry=ProfileAchievementCatalog.Entries[i];
            tile.Icon.sprite=Earned(i)?Silver[i]:Bronze[i];
            tile.Title.text=entry.Title;
            tile.Progress.text=Earned(i)?"UNLOCKED":Math.Min(_values[i],entry.Goal)+" / "+entry.Goal;
            tile.Progress.color=Earned(i)?new Color32(204,213,255,255):new Color32(160,172,206,255);
        }
        public void Open()
        {
            int next=-1;
            for(int i=0;i<_values.Length;i++) if(!Earned(i)){next=i;break;}
            OpenAt(next<0?0:next);
        }
        public void OpenAt(int index)
        {
            if(!Collection.activeSelf)
            {
                _profileScrollWasEnabled=ProfileScroll.enabled;
                ProfileScroll.StopMovement();ProfileScroll.enabled=false;
            }
            Collection.SetActive(true);Collection.transform.SetAsLastSibling();
            Select(index);
            Canvas.ForceUpdateCanvases();
            // Keep the chosen medal visible on shorter phones.
            var content=CollectionScroll.content;
            float overflow=Mathf.Max(0,content.rect.height-CollectionScroll.viewport.rect.height);
            float rowTop=(index/4)*122f;
            CollectionScroll.verticalNormalizedPosition=overflow<=0?1:1-Mathf.Clamp01(rowTop/overflow);
        }
        public void Close()
        {
            if(Collection==null || !Collection.activeSelf) return;
            Collection.SetActive(false);
            if(ProfileScroll!=null)ProfileScroll.enabled=_profileScrollWasEnabled;
        }
        public void Select(int index)
        {
            if(_values==null || index<0 || index>=_values.Length) return;
            _selected=index;
            var entry=ProfileAchievementCatalog.Entries[index];
            bool earned=Earned(index);
            DetailIcon.sprite=earned?Silver[index]:Bronze[index];
            DetailTitle.text=entry.Title;DetailDescription.text=entry.Description;
            DetailProgress.text=Math.Min(_values[index],entry.Goal)+" / "+entry.Goal;
            DetailState.text=earned?"UNLOCKED":"IN PROGRESS";
            DetailFill.fillAmount=Mathf.Clamp01((float)_values[index]/entry.Goal);
            for(int i=0;i<CollectionTiles.Length;i++)CollectionTiles[i].Selection.enabled=i==index;
        }
    }
}

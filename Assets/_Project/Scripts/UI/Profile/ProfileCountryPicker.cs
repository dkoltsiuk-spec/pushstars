using System;
using System.Collections.Generic;
using System.Linq;
using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    public sealed class ProfileCountryPicker : MonoBehaviour
    {
        public Button HeaderButton, CloseButton, AutoButton;
        public Image HeaderFlag;
        public TextMeshProUGUI HeaderUnknown, CurrentCountry, DetectionNote, Empty;
        public TMP_InputField Search;
        public GameObject Root, RowTemplate;
        public ScrollRect List, ProfileScroll;
        private sealed class CountryRow
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Flag, Background;
            public TextMeshProUGUI Name, Code;
        }
        private readonly List<CountryRow> _rows=new List<CountryRow>();
        private bool _scrollEnabled;
        private string _lastCode, _lastLanguage;
        private void Awake()
        {
            HeaderButton.onClick.AddListener(Open);
            CloseButton.onClick.AddListener(Close);
            AutoButton.onClick.AddListener(()=>{ProfileCountry.UseDeviceRegion();RefreshHeader();Rebuild();});
            Search.onValueChanged.AddListener(_=>Rebuild());
        }
        private void OnEnable(){ProfileCountry.Changed+=RefreshHeader;RefreshHeader();}
        private void OnDisable(){ProfileCountry.Changed-=RefreshHeader;Close();}
        private void OnDestroy(){if(Application.isPlaying && Root!=null)Destroy(Root);}
        private void OnApplicationFocus(bool focused){if(focused)ProfileCountry.RefreshDetection();}
        private void LateUpdate()
        {
            if(_lastCode!=ProfileCountry.Code || _lastLanguage!=Localization.Language)
            {RefreshHeader();if(Root.activeSelf)Rebuild();}
            if(Root.activeSelf && Input.GetKeyDown(KeyCode.Escape))Close();
        }
        public void RefreshHeader()
        {
            if(HeaderFlag==null)return;
            _lastCode=ProfileCountry.Code;_lastLanguage=Localization.Language;
            HeaderFlag.sprite=CountryFlags.Get(_lastCode);HeaderFlag.enabled=HeaderFlag.sprite!=null;
            HeaderUnknown.gameObject.SetActive(!HeaderFlag.enabled);
            var entry=CountryCatalog.Find(_lastCode);
            CurrentCountry.text=entry==null?"CHOOSE COUNTRY":entry.DisplayName(Localization.Language);
            DetectionNote.text=entry==null?"Select your profile country.":ProfileCountry.IsAutomatic?"From your device region. Tap a country to change it.":"Your selected profile country.";
        }
        public void Open()
        {
            if(!Root.activeSelf){_scrollEnabled=ProfileScroll.enabled;ProfileScroll.StopMovement();ProfileScroll.enabled=false;}
            ProfileCountry.RefreshDetection();Root.SetActive(true);Root.transform.SetAsLastSibling();
            Root.GetComponentInChildren<AchievementCollectionFit>(true).Fit();
            Search.SetTextWithoutNotify("");Rebuild();
        }
        public void Close()
        {if(Root==null || !Root.activeSelf)return;Root.SetActive(false);if(ProfileScroll!=null)ProfileScroll.enabled=_scrollEnabled;}
        public void Choose(string code){ProfileCountry.Set(code);RefreshHeader();Close();}
        public void Rebuild()
        {
            string query=Search.text.Trim();
            var matches=CountryCatalog.Entries.Where(e=>Matches(e,query)).OrderBy(e=>e.DisplayName(Localization.Language),StringComparer.CurrentCultureIgnoreCase);
            int index=0;
            foreach(var entry in matches)
            {
                if(index==_rows.Count)
                {
                    var clone=Instantiate(RowTemplate,List.content);
                    var created=new CountryRow{Root=clone,Rect=(RectTransform)clone.transform,Background=clone.GetComponent<Image>(),
                        Flag=clone.transform.Find("Flag").GetComponent<Image>(),Name=clone.transform.Find("CountryName").GetComponent<TextMeshProUGUI>(),
                        Code=clone.transform.Find("Code").GetComponent<TextMeshProUGUI>()};
                    clone.GetComponent<Button>().onClick.AddListener(()=>Choose(created.Code.text));_rows.Add(created);
                }
                var row=_rows[index];row.Root.name="Country_"+entry.code;row.Root.SetActive(true);
                row.Rect.anchoredPosition=new Vector2(0,-index*48);
                row.Flag.sprite=CountryFlags.Get(entry.code);row.Name.text=entry.DisplayName(Localization.Language);row.Code.text=entry.code;
                row.Background.color=entry.code==ProfileCountry.Code?new Color32(53,82,169,255):new Color32(17,32,98,255);index++;
            }
            for(int i=index;i<_rows.Count;i++){_rows[i].Root.SetActive(false);_rows[i].Root.name="PooledCountry_"+i;}
            List.content.sizeDelta=new Vector2(List.content.sizeDelta.x,Math.Max(1,index)*48);
            Empty.gameObject.SetActive(index==0);List.verticalNormalizedPosition=1;
        }
        private static bool Matches(CountryCatalog.Entry e,string q)=>string.IsNullOrEmpty(q)
            || e.code.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0 || e.en.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0
            || e.ru.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0 || e.ptBR.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0
            || (e.code=="MD" && "Молдова Молдавия".IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0);
    }
}

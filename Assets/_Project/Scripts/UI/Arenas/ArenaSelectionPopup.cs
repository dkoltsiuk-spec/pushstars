using System;
using System.Collections;
using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    public sealed class ArenaSelectionPopup : MonoBehaviour
    {
        private RectTransform _art;
        private Image[] _frames;
        private Sprite _silver, _gold;
        private TextMeshProUGUI _title, _caption;
        private CanvasGroup _group;
        public bool IsFinished { get; private set; }
        public static ArenaSelectionPopup Create(Transform parent, TMP_FontAsset font = null)
        {
            var root = ArenaUi.Rect(parent, "ArenaSelection", Vector2.zero, Vector2.zero); ArenaUi.Stretch(root);
            var popup = root.gameObject.AddComponent<ArenaSelectionPopup>(); popup.Build(font); return popup;
        }
        private void Build(TMP_FontAsset font)
        {
            _group = gameObject.AddComponent<CanvasGroup>();
            var shade = ArenaUi.Image(transform,"Dim",Vector2.zero,Vector2.zero,new Color(0,0,0,.72f));
            ArenaUi.Stretch(shade.rectTransform); shade.raycastTarget = true;
            _art = ArenaUi.Rect(transform,"SelectionPanel",new Vector2(390,270),Vector2.zero);
            var source = ArenaCatalog.Instance.SelectionPanel;
            ArenaUi.Image(_art,"SuppliedFrame",new Vector2(440,246),Vector2.zero,Color.white,source);
            _silver = ArenaUi.Slice(source,new Rect(.184f,.220f,.293f,.539f));
            _gold = ArenaUi.Slice(source,new Rect(.525f,.220f,.293f,.539f));
            _frames = new Image[2];
            for(int i=0;i<2;i++)
            {
                var card = ArenaUi.Rect(_art,i==0?"PlayerMap":"OpponentMap",new Vector2(129,133),new Vector2(i==0?-75:75,-3));
                var cover = card.gameObject.AddComponent<ArenaRoundedGraphic>(); cover.color=new Color32(28,25,45,255); cover.Radius=19;
                _frames[i]=ArenaUi.Image(card,"Frame",new Vector2(129,133),Vector2.zero,Color.white,_silver);
                ArenaUi.Thumbnail(card,ArenaCatalog.Get(i==0?ArenaMatch.PlayerId:ArenaMatch.OpponentId).Home,new Vector2(101,107));
            }
            _title = ArenaUi.Label(_art,"Title","RANDOM MAP SELECT",new Vector2(194,30),new Vector2(7,90),15);
            _caption = ArenaUi.Label(_art,"Winner","",new Vector2(340,30),new Vector2(0,-123),16);
            ArenaUi.Label(_art,"Player","YOUR MAP",new Vector2(125,18),new Vector2(-75,-82),10);
            ArenaUi.Label(_art,"Opponent","OPPONENT MAP",new Vector2(125,18),new Vector2(75,-82),10);
            if(font!=null) foreach(var t in GetComponentsInChildren<TextMeshProUGUI>()) t.font=font;
            Fit();
        }
        private void Update() => Fit();
        private void Fit()
        {
            var size=((RectTransform)transform).rect.size;
            _art.localScale=Vector3.one*Mathf.Max(.01f,Mathf.Min(size.x/390f,size.y/320f));
        }
        public void Highlight(int index)
        {
            for(int i=0;i<2;i++) { _frames[i].sprite=i==index || index==2?_gold:_silver; }
        }
        public IEnumerator Play()
        {
            IsFinished=false; transform.SetAsLastSibling();
            ArenaMatch.ResolveLocal();
            _title.text=ArenaMatch.SameChoice?"SAME MAP!":"RANDOM MAP SELECT";
            float start=Time.unscaledTime;
            while(Time.unscaledTime-start<.18f) { _group.alpha=(Time.unscaledTime-start)/.18f; yield return null; }
            _group.alpha=1;
            if(!ArenaMatch.SameChoice)
            {
                for(int i=0;i<12;i++) { Highlight(i%2); yield return new WaitForSecondsRealtime(Mathf.Lerp(.055f,.19f,i/11f)); }
            }
            Highlight(ArenaMatch.SameChoice?2:ArenaMatch.WinnerId==ArenaMatch.PlayerId?0:1);
            _caption.text=ArenaCatalog.Get(ArenaMatch.WinnerId).Title.ToUpperInvariant();
            yield return new WaitForSecondsRealtime(.65f);
            ArenaMatch.PresentationComplete=true; IsFinished=true;
        }
        private void OnDestroy()
        {
            if(_silver!=null) { if(Application.isPlaying)Destroy(_silver);else DestroyImmediate(_silver); }
            if(_gold!=null) { if(Application.isPlaying)Destroy(_gold);else DestroyImmediate(_gold); }
        }
    }
}

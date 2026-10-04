using System.Collections.Generic;
using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    public sealed class ArenaHomeController : MonoBehaviour
    {
        private GameObject _overlay;
        private RectTransform _sheet;
        private TMP_FontAsset _font;
        private readonly List<(string id, ArenaRoundedGraphic frame, TextMeshProUGUI label)> _cards = new List<(string, ArenaRoundedGraphic, TextMeshProUGUI)>();
        public bool IsOpen => _overlay!=null && _overlay.activeSelf;
        public void Configure(Transform home, Transform overlayParent, TMP_FontAsset font)
        {
            if(ArenaCatalog.Instance==null || _overlay!=null) return;
            _font=font;
            var spare=home.Find("SpareSlot"); if(spare!=null) spare.gameObject.SetActive(false);
            var entry = ArenaUi.Rect(home,"ArenaMapsButton",new Vector2(72,38),new Vector2(-18,-139));
            entry.anchorMin=entry.anchorMax=entry.pivot=Vector2.one;
            var plate=entry.gameObject.AddComponent<ArenaRoundedGraphic>(); plate.color=new Color32(28,43,81,255); plate.Radius=10;
            var button=entry.gameObject.AddComponent<Button>(); button.targetGraphic=plate; button.onClick.AddListener(Show);
            var title=ArenaUi.Label(entry,"Label","MAPS",new Vector2(65,30),Vector2.zero,17); title.font=font;
            var root=ArenaUi.Rect(overlayParent,"ArenaCollection",Vector2.zero,Vector2.zero); ArenaUi.Stretch(root); _overlay=root.gameObject;
            var shade=ArenaUi.Image(root,"Dim",Vector2.zero,Vector2.zero,new Color(0,0,0,.86f)); ArenaUi.Stretch(shade.rectTransform); shade.raycastTarget=true;
            _sheet=ArenaUi.Rect(root,"Collection",new Vector2(370,660),Vector2.zero);
            var panel=_sheet.gameObject.AddComponent<ArenaRoundedGraphic>(); panel.color=new Color32(17,25,52,255); panel.Radius=18;
            ArenaUi.Label(_sheet,"Title","CHOOSE YOUR MAP",new Vector2(310,40),new Vector2(0,290),24);
            ArenaUi.Label(_sheet,"Hint","Your home. Your choice for the next duel.",new Vector2(320,25),new Vector2(0,254),12);
            var close=ArenaUi.Image(_sheet,"Close",new Vector2(300,40),new Vector2(0,-293),new Color32(20,101,199,255));
            var closeButton=close.gameObject.AddComponent<Button>(); close.raycastTarget=true; closeButton.onClick.AddListener(Hide);
            ArenaUi.Label(close.transform,"Label","DONE",new Vector2(290,38),Vector2.zero,19);
            var viewport=ArenaUi.Rect(_sheet,"Viewport",new Vector2(342,488),new Vector2(0,-3)); viewport.gameObject.AddComponent<RectMask2D>();
            var content=ArenaUi.Rect(viewport,"Content",new Vector2(342,Mathf.Ceil(ArenaCatalog.Instance.Arenas.Length/2f)*148),Vector2.zero);
            content.anchorMin=content.anchorMax=content.pivot=new Vector2(.5f,1);
            var scroll=viewport.gameObject.AddComponent<ScrollRect>(); scroll.content=content; scroll.viewport=viewport; scroll.horizontal=false; scroll.movementType=ScrollRect.MovementType.Clamped;
            int i=0;
            foreach(var arena in ArenaCatalog.Instance.Arenas)
            {
                var id=arena.Id;
                var card=ArenaUi.Rect(content,id,new Vector2(160,138),new Vector2(i%2==0?-86:86,-74-148*(i/2)));
                card.anchorMin=card.anchorMax=new Vector2(.5f,1);
                var frame=card.gameObject.AddComponent<ArenaRoundedGraphic>(); frame.Radius=12;
                var imageRoot=ArenaUi.Rect(card,"Preview",new Vector2(150,104),new Vector2(0,12));
                ArenaUi.Thumbnail(imageRoot,arena.Home,new Vector2(150,104));
                var label=ArenaUi.Label(card,"Name",arena.Title,new Vector2(150,24),new Vector2(0,-53),13);
                var select=card.gameObject.AddComponent<Button>(); select.targetGraphic=frame;
                select.onClick.AddListener(()=>{ if(ArenaProfile.Select(id)) Refresh(); });
                _cards.Add((id,frame,label)); i++;
            }
            foreach(var label in _overlay.GetComponentsInChildren<TextMeshProUGUI>()) label.font=font;
            _overlay.SetActive(false); ArenaProfile.Changed+=Refresh;
        }
        public void Show() { if(_overlay==null) return; _overlay.SetActive(true); _overlay.transform.SetAsLastSibling(); Refresh(); Fit(); }
        public void Hide() { if(_overlay!=null) _overlay.SetActive(false); }
        private void Refresh()
        {
            foreach(var c in _cards)
            {
                bool selected=c.id==ArenaProfile.SelectedId, owned=ArenaProfile.Owns(c.id);
                c.frame.color=selected?new Color32(255,196,24,255):new Color32(54,69,108,255);
                c.label.color=selected?new Color32(27,25,48,255):Color.white;
                c.label.text=ArenaCatalog.Get(c.id).Title+(owned?"":" · LOCKED");
            }
        }
        private void Fit()
        {
            var size=((RectTransform)_overlay.transform).rect.size;
            _sheet.localScale=Vector3.one*Mathf.Min(size.x/390f,size.y*.93f/660f);
        }
        private void Update() { if(!IsOpen)return; Fit(); if(Input.GetKeyDown(KeyCode.Escape))Hide(); }
        private void OnDisable()=>Hide();
        private void OnDestroy() { ArenaProfile.Changed-=Refresh; if(_overlay!=null) { if(Application.isPlaying)Destroy(_overlay);else DestroyImmediate(_overlay); } }
    }
}

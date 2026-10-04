using PushStars.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Two floor views of the same arena, divided at the authored opponent/player seam.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ArenaBattleGraphic : MaskableGraphic
    {
        private Sprite _sprite;
        public override Texture mainTexture => _sprite!=null?_sprite.texture:Texture2D.whiteTexture;
        public void SetArena(ArenaDefinition arena) { _sprite=arena.Home; SetAllDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if(_sprite==null)return;
            var r=rectTransform.rect; float seam=r.yMin+r.height*.48f;
            Quad(vh,new Rect(r.xMin,seam,r.width,r.yMax-seam),false);
            Quad(vh,new Rect(r.xMin,r.yMin,r.width,seam-r.yMin),true);
            int index=vh.currentVertCount;
            var tint=new Color32(22,38,64,255);
            vh.AddVert(new Vector3(r.xMin,seam-2),tint,Vector2.zero); vh.AddVert(new Vector3(r.xMax,seam-2),tint,Vector2.zero);
            vh.AddVert(new Vector3(r.xMax,seam+2),tint,Vector2.zero); vh.AddVert(new Vector3(r.xMin,seam+2),tint,Vector2.zero);
            vh.AddTriangle(index,index+1,index+2); vh.AddTriangle(index,index+2,index+3);
        }
        private void Quad(VertexHelper vh,Rect r,bool mirror)
        {
            var t=_sprite.textureRect;
            // Retain the floor in the lower third; crop unused ceiling rather than squash the art.
            float height=Mathf.Min(t.height,t.width*r.height/r.width);
            float y=t.y+(t.height-height)*.23f;
            float u0=t.x/_sprite.texture.width,u1=t.xMax/_sprite.texture.width;
            if(mirror) {float temp=u0;u0=u1;u1=temp;}
            float v0=y/_sprite.texture.height,v1=(y+height)/_sprite.texture.height;
            int n=vh.currentVertCount;
            vh.AddVert(new Vector3(r.xMin,r.yMin),Color.white,new Vector2(u0,v0));
            vh.AddVert(new Vector3(r.xMax,r.yMin),Color.white,new Vector2(u1,v0));
            vh.AddVert(new Vector3(r.xMax,r.yMax),Color.white,new Vector2(u1,v1));
            vh.AddVert(new Vector3(r.xMin,r.yMax),Color.white,new Vector2(u0,v1));
            vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);
        }
        public static void Apply(Image background,Sprite fallback)
        {
            var arena=ArenaCatalog.Get(ArenaMatch.IsResolved?ArenaMatch.WinnerId:ArenaProfile.SelectedId);
            if(arena==null || arena.Battle!=null)
            {
                background.sprite=arena?.Battle??fallback; background.color=Color.white; return;
            }
            var r=new GameObject("SelectedArenaBattle",typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(background.transform,false); r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
            var graphic=r.gameObject.AddComponent<ArenaBattleGraphic>();graphic.raycastTarget=false;graphic.SetArena(arena);
            background.color=Color.white;
        }
    }
}

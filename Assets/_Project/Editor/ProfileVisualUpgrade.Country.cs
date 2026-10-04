using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Editor
{
    public static partial class ProfileVisualUpgrade
    {
        static void BuildCountryPicker(ProfileDashboard d,ProfileIdentityEditor identity,Transform ribbon)
        {
            var view=d.GetComponent<ProfileCountryPicker>()??Undo.AddComponent<ProfileCountryPicker>(d.gameObject);
            Undo.RecordObject(view,"Country picker");
            if(view.Root!=null)Undo.DestroyObjectImmediate(view.Root);
            var header=Flat(ribbon,"Country",0,0,44,38,Color.clear);header.raycastTarget=true;
            view.HeaderButton=header.gameObject.AddComponent<Button>();view.HeaderButton.targetGraphic=header;
            view.HeaderFlag=Pic(header.transform,"Flag",null,6,9,30,20);
            view.HeaderUnknown=Text(header.transform,"Unknown","COUNTRY",0,7,44,24,8,true,Muted);
            view.HeaderUnknown.alignment=TextAlignmentOptions.Center;
            view.ProfileScroll=d.transform.Find("ProfileScroll").GetComponent<ScrollRect>();
            var overlay=Flat(identity.ModalParent,"ProfileCountryPicker",0,0,0,0,new Color32(3,9,55,255));
            Stretch(overlay.rectTransform,Vector2.zero,Vector2.zero);overlay.raycastTarget=true;view.Root=overlay.gameObject;
            var safe=Rect(overlay.transform,"SafeArea",0,0,0,0);Stretch(safe,Vector2.zero,Vector2.zero);safe.gameObject.AddComponent<SafeAreaFitter>();
            var design=Rect(safe,"CountryDesign",0,0,390,844);design.anchorMin=design.anchorMax=design.pivot=new Vector2(.5f,1);design.anchoredPosition=Vector2.zero;
            var fit=safe.gameObject.AddComponent<AchievementCollectionFit>();fit.Design=design;fit.Fit();
            Flat(design,"Header",0,0,390,80,new Color32(12,27,113,255));
            view.CloseButton=Button(design,"Back","BACK",14,18,78,44);
            Text(design,"Title","PROFILE COUNTRY",107,12,268,27,20,true);
            view.CurrentCountry=Text(design,"CurrentCountry","",108,45,258,21,13,true,Muted);
            view.DetectionNote=Text(design,"DetectionNote","",18,91,354,36,12,false,Muted);view.DetectionNote.textWrappingMode=TextWrappingModes.Normal;
            view.AutoButton=Button(design,"Automatic","USE DEVICE REGION",18,139,354,43);
            var field=Flat(design,"Search",18,197,354,44,new Color32(26,44,125,255));field.raycastTarget=true;
            view.Search=field.gameObject.AddComponent<TMP_InputField>();view.Search.targetGraphic=field;
            var area=Rect(field.transform,"TextArea",12,2,330,40);area.gameObject.AddComponent<RectMask2D>();
            var text=Text(area,"Text","",0,0,330,40,15,false);text.richText=false;
            var hint=Text(area,"Placeholder","Search country or code",0,0,330,40,14,false,Muted);
            view.Search.textViewport=area;view.Search.textComponent=text;view.Search.placeholder=hint;view.Search.characterLimit=60;
            var viewport=Rect(design,"CountryList",0,0,0,0);Stretch(viewport,new Vector2(18,16),new Vector2(-18,-255));
            var scrollHit=viewport.gameObject.AddComponent<Image>();scrollHit.color=Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();view.List=viewport.gameObject.AddComponent<ScrollRect>();
            view.List.viewport=viewport;view.List.horizontal=false;view.List.movementType=ScrollRect.MovementType.Clamped;view.List.scrollSensitivity=30;
            var content=Rect(viewport,"Content",0,0,354,48);view.List.content=content;
            var row=Flat(content,"CountryTemplate",0,0,354,44,new Color32(17,32,98,255));row.raycastTarget=true;
            row.gameObject.AddComponent<Button>().targetGraphic=row;
            Pic(row.transform,"Flag",null,11,11,30,21);
            Text(row.transform,"CountryName","",53,3,252,38,15,false);
            Text(row.transform,"Code","",311,3,34,38,11,true,Muted).alignment=TextAlignmentOptions.Center;
            view.RowTemplate=row.gameObject;row.gameObject.SetActive(false);
            view.Empty=Text(viewport,"Empty","NO COUNTRIES FOUND",8,16,338,42,15,true,Muted);view.Empty.alignment=TextAlignmentOptions.Center;
            foreach(var button in overlay.GetComponentsInChildren<Button>(true))button.navigation=new Navigation{mode=Navigation.Mode.None};
            overlay.gameObject.SetActive(false);view.RefreshHeader();EditorUtility.SetDirty(view);
        }
    }
}

using UnityEngine;

namespace PushStars.UI
{
    [ExecuteAlways]
    public sealed class AchievementCollectionFit : MonoBehaviour
    {
        public RectTransform Design;
        private void OnEnable()=>Fit();
        private void LateUpdate()=>Fit();
        public void Fit()
        {
            if(Design==null)return;
            var bounds=((RectTransform)transform).rect;
            float scale=Mathf.Max(1,bounds.width)/390f;
            Design.localScale=Vector3.one*scale;
            Design.sizeDelta=new Vector2(390,bounds.height/scale);
        }
    }
}

using System;
using UnityEngine;

namespace PushStars.UI
{
    public sealed class ProfileAchievementArt : ScriptableObject
    {
        public Sprite[] Silver;
        public Sprite ForId(string id)
        {
            int i=Array.FindIndex(ProfileAchievementCatalog.Entries,e=>e.Id==id);
            return i>=0 && i<Silver.Length?Silver[i]:null;
        }
    }
}

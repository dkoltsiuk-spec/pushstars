using System.Collections.Generic;
using PushStars.Core;
using UnityEngine;

namespace PushStars.UI
{
    public static class CountryFlags
    {
        private static readonly Dictionary<string,Sprite> Cache=new Dictionary<string,Sprite>();
        public static Sprite Get(string code)
        {
            code=CountryCatalog.Normalize(code);
            if(string.IsNullOrEmpty(code))return null;
            if(!Cache.TryGetValue(code,out var sprite))
            {sprite=Resources.Load<Sprite>("CountryFlags/"+code.ToLowerInvariant());Cache[code]=sprite;}
            return sprite;
        }
    }
}

using System;
using System.Globalization;
using System.Runtime.InteropServices;
using PushStars.Core;
using PushStars.Services;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>Profile country defaults to the device region, never inferred from app language or GPS.</summary>
    public static class ProfileCountry
    {
        private static string _detected;
        public static event Action Changed;
        private static string Key
        {
            get
            {
                if(ServiceLocator.TryGet<FirebaseAuthService>(out var auth) && !string.IsNullOrEmpty(auth.Uid))
                    return "profile.identity."+auth.Uid+".country";
                return "profile.identity.local.country";
            }
        }
        public static string Override=>CountryCatalog.Normalize(PlayerPrefs.GetString(Key,""));
        public static bool IsAutomatic=>string.IsNullOrEmpty(Override);
        public static string DetectedCode=>_detected??(_detected=Detect());
        public static string Code=>IsAutomatic?DetectedCode:Override;
        public static void Set(string code)
        {
            string valid=CountryCatalog.Normalize(code);
            if(string.IsNullOrEmpty(valid))return;
            PlayerPrefs.SetString(Key,valid);PlayerPrefs.Save();Changed?.Invoke();
        }
        public static void UseDeviceRegion()
        {PlayerPrefs.DeleteKey(Key);PlayerPrefs.Save();RefreshDetection();}
        public static void RefreshDetection(){_detected=Detect();Changed?.Invoke();}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset(){_detected=null;Changed=null;}
        private static string Detect()
        {
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using(var locale=new AndroidJavaClass("java.util.Locale"))
                using(var current=locale.CallStatic<AndroidJavaObject>("getDefault"))
                    return CountryCatalog.Normalize(current.Call<string>("getCountry"));
#elif UNITY_IOS && !UNITY_EDITOR
                return CountryCatalog.Normalize(Marshal.PtrToStringAnsi(_pushStarsCountryCode()));
#else
                return CountryCatalog.Normalize(RegionInfo.CurrentRegion.TwoLetterISORegionName);
#endif
            }
            catch(Exception) {return "";}
        }
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern IntPtr _pushStarsCountryCode();
#endif
    }
}

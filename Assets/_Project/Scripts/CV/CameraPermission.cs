using System.Collections;
using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Camera permission, one call for every platform. Android only answers through its own
    /// runtime-permission API; <see cref="Application.RequestUserAuthorization"/> covers iOS and
    /// the Editor.
    /// </summary>
    public static class CameraPermission
    {
        public static bool Granted
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera);
#else
                return Application.HasUserAuthorization(UserAuthorization.WebCam);
#endif
            }
        }

        /// <summary>Shows the system prompt when the camera is not allowed yet and waits for the
        /// answer. Read <see cref="Granted"/> afterwards.</summary>
        public static IEnumerator Request()
        {
            if (Granted) yield break;
#if UNITY_ANDROID && !UNITY_EDITOR
            bool answered = false;
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += _ => answered = true;
            callbacks.PermissionDenied += _ => answered = true;
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera, callbacks);
            while (!answered) yield return null;
#else
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
#endif
        }
    }
}
